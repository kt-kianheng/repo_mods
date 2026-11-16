using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace TargetDummy
{
    /// <summary>
    /// Borrows the game's own Robe for the dummy. The Robe prefab is copied once while inactive (so none of its
    /// scripts ever wake up), and a clean copy is made of just its bones, meshes and materials. What's left is a
    /// lifeless Robe in its rest pose that renders exactly like the real one, with no AI, sound, physics or network.
    /// Its damage resistance and impact multiplier are read too, so the numbers match what a real Robe would take.
    /// </summary>
    internal static class RobeVisual
    {
        private const string RobePath = "Enemies/Enemy - Robe";

        private static GameObject holder;
        private static GameObject template;
        private static bool tried;

        /// <summary>The Robe's damage resistance (0 = takes full damage).</summary>
        internal static float DamageResistance { get; private set; }
        /// <summary>Multiplier the game applies to damage from thrown objects hitting a Robe.</summary>
        internal static float ObjectHurtMultiplier { get; private set; } = 1f;
        /// <summary>Whether a Robe takes damage from thrown objects at all.</summary>
        internal static bool ObjectHurt { get; private set; } = true;
        internal static int MaxHealth { get; private set; }

        /// <summary>A fresh copy of the lifeless Robe under parent, or null if it couldn't be built.</summary>
        internal static GameObject Spawn(Transform parent)
        {
            GameObject t = Template();
            if (t == null) return null;
            GameObject robe = Object.Instantiate(t, parent, false);
            robe.name = "Robe";
            return robe;
        }

        private static GameObject Template()
        {
            if (template != null || tried) return template;
            tried = true;
            try { template = Build(); }
            catch (System.Exception e) { Plugin.Log.LogError($"Building the Robe for the dummy failed: {e}"); }
            return template;
        }

        private static GameObject Build()
        {
            GameObject prefab = Resources.Load<GameObject>(RobePath);
            if (prefab == null) { Plugin.Log.LogError($"'{RobePath}' not found; the dummy will be just the stand."); return null; }

            holder = new GameObject("TargetDummy.PrefabHolder");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            holder.hideFlags = HideFlags.HideAndDontSave;

            GameObject clone = Object.Instantiate(prefab, holder.transform);   // inactive parent: no Awake/OnEnable runs
            clone.name = "TargetDummy Robe";
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;

            ReadHealth(clone);

            // pick the body: mesh/skinned renderers that are switched on in the prefab, minus anything the player hid
            string[] hide = SplitList(Settings.HideRobeParts.Value);
            var all = clone.GetComponentsInChildren<Renderer>(true);
            var keep = new List<Renderer>();
            foreach (Renderer r in all)
                if (IsBodyRenderer(r) && r.enabled && ActiveUnder(r.transform, clone.transform) && !Matches(r, hide)) keep.Add(r);
            if (keep.Count == 0)
            {
                // the Robe's visuals may be switched off until it spawns: fall back to every mesh that isn't an effect
                foreach (Renderer r in all)
                    if (IsBodyRenderer(r) && !LooksLikeEffect(r.transform, clone.transform) && !Matches(r, hide)) keep.Add(r);
            }
            if (Settings.LogRobeParts.Value) LogParts(all, keep, clone.transform);
            if (keep.Count == 0) { Plugin.Log.LogError("The Robe has no visible meshes; the dummy will be just the stand."); Object.Destroy(clone); return null; }

            GameObject clean = MirrorVisuals(clone, keep);
            Object.Destroy(clone);   // the original copy never woke up; throw it away
            return clean;
        }

        private static void ReadHealth(GameObject clone)
        {
            EnemyHealth health = clone.GetComponentInChildren<EnemyHealth>(true);
            if (health == null) { Plugin.Log.LogWarning("The Robe has no EnemyHealth; numbers use raw damage."); return; }
            MaxHealth = health.health;
            ObjectHurt = health.objectHurt;
            ObjectHurtMultiplier = health.objectHurtMultiplier;
            var resist = AccessTools.Field(typeof(EnemyHealth), "damageResistance");
            if (resist != null) DamageResistance = (float)resist.GetValue(health);
            Plugin.Log.LogInfo($"Robe: {MaxHealth} hp, damage resistance {DamageResistance:0.##}, thrown-object multiplier {ObjectHurtMultiplier:0.##}.");
        }

        private static bool IsBodyRenderer(Renderer r) => r is SkinnedMeshRenderer || r is MeshRenderer;

        private static bool ActiveUnder(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }

        private static readonly string[] EffectWords = { "particle", "effect", "fx", "death", "hurt", "attack", "debug", "gizmo", "flash", "spawn", "despawn", "trail", "beam", "smoke", "hit" };

        private static bool LooksLikeEffect(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
            {
                string n = t.name.ToLowerInvariant();
                foreach (string w in EffectWords) if (n.Contains(w)) return true;
            }
            return false;
        }

        private static string[] SplitList(string s)
        {
            var list = new List<string>();
            foreach (string p in (s ?? "").Split(',')) { string q = p.Trim().ToLowerInvariant(); if (q.Length > 0) list.Add(q); }
            return list.ToArray();
        }

        private static bool Matches(Renderer r, string[] words)
        {
            if (words.Length == 0) return false;
            string n = r.name.ToLowerInvariant();
            foreach (string w in words) if (n.Contains(w)) return true;
            return false;
        }

        /// <summary>
        /// Builds a clean copy of the Robe: the same transform tree (so the bones keep their rest pose) with only the
        /// kept meshes and renderers on it. Nothing else from the prefab, so no script on the dummy can ever wake up.
        /// </summary>
        private static GameObject MirrorVisuals(GameObject source, List<Renderer> keep)
        {
            var map = new Dictionary<Transform, Transform>();
            GameObject root = MirrorTree(source.transform, holder.transform, map);
            root.name = "TargetDummy Robe";

            foreach (Renderer r in keep)
            {
                if (!map.TryGetValue(r.transform, out Transform target)) continue;
                if (r is SkinnedMeshRenderer src)
                {
                    var dst = target.gameObject.AddComponent<SkinnedMeshRenderer>();
                    dst.sharedMesh = src.sharedMesh;
                    var bones = src.bones;
                    var mapped = new Transform[bones.Length];
                    for (int i = 0; i < bones.Length; i++) mapped[i] = bones[i] != null && map.TryGetValue(bones[i], out Transform b) ? b : null;
                    dst.bones = mapped;
                    dst.rootBone = src.rootBone != null && map.TryGetValue(src.rootBone, out Transform rb) ? rb : null;
                    dst.localBounds = src.localBounds;
                    dst.quality = src.quality;
                    dst.updateWhenOffscreen = false;   // keep the authored bounds: they're valid straight away
                    dst.sharedMaterials = src.sharedMaterials;
                    if (src.sharedMesh != null)
                        for (int i = 0; i < src.sharedMesh.blendShapeCount; i++) dst.SetBlendShapeWeight(i, src.GetBlendShapeWeight(i));
                }
                else if (r is MeshRenderer)
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    target.gameObject.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    target.gameObject.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                }
                // make sure the piece and its parents are switched on
                for (Transform t = target; t != null && t != root.transform; t = t.parent) t.gameObject.SetActive(true);
            }
            root.SetActive(true);
            SetLayer(root.transform, 0);
            return root;
        }

        private static GameObject MirrorTree(Transform src, Transform parent, Dictionary<Transform, Transform> map)
        {
            var go = new GameObject(src.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = src.localPosition;
            go.transform.localRotation = src.localRotation;
            go.transform.localScale = src.localScale;
            map[src] = go.transform;
            foreach (Transform child in src) MirrorTree(child, go.transform, map);
            go.SetActive(src.gameObject.activeSelf);
            return go;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        private static void LogParts(Renderer[] all, List<Renderer> keep, Transform root)
        {
            var sb = new StringBuilder("Robe pieces (+ = shown on the dummy). Hide any with HideRobeParts in TargetDummy.cfg:");
            foreach (Renderer r in all)
            {
                sb.Append("\n  ").Append(keep.Contains(r) ? "+ " : "- ").Append(Path(r.transform, root)).Append(" [").Append(r.GetType().Name).Append(']');
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static string Path(Transform t, Transform root)
        {
            string p = t.name;
            for (t = t.parent; t != null && t != root; t = t.parent) p = t.name + "/" + p;
            return p;
        }
    }
}
