using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace TargetDummy
{
    /// <summary>
    /// Borrows one of the game's own models for a dummy: the Robe for the Target Dummy, the player droid for the Ally
    /// Dummy. The prefab is copied once while inactive (so none of its scripts ever wake up), and a clean copy is made
    /// of just its bones, meshes and materials. What's left is a lifeless model in its rest pose that renders exactly
    /// like the real one, with no AI, sound, physics or network.
    /// </summary>
    internal class BorrowedModel
    {
        internal static readonly BorrowedModel Robe = new BorrowedModel("Robe", new[] { "Enemies/Enemy - Robe" }, () => Settings.HideRobeParts.Value, true);
        internal static readonly BorrowedModel Droid = new BorrowedModel("Droid",
            new[] { "PlayerAvatar Dummy", "PlayerAvatarMenu", "PlayerAvatar" }, () => Settings.HideDroidParts.Value, false);

        // the Robe's stats, read from its EnemyHealth, so the Target Dummy's numbers match a real Robe
        internal static float RobeDamageResistance { get; private set; }
        internal static float RobeObjectHurtMultiplier { get; private set; } = 1f;
        internal static bool RobeObjectHurt { get; private set; } = true;

        private static GameObject holder;

        private readonly string label;
        private readonly string[] paths;
        private readonly System.Func<string> hideSetting;
        private readonly bool readRobeStats;
        private GameObject template;
        private bool tried;

        private BorrowedModel(string label, string[] paths, System.Func<string> hideSetting, bool readRobeStats)
        {
            this.label = label; this.paths = paths; this.hideSetting = hideSetting; this.readRobeStats = readRobeStats;
        }

        /// <summary>A fresh copy of the lifeless model under parent, or null if it couldn't be built.</summary>
        internal GameObject Spawn(Transform parent)
        {
            if (!tried)
            {
                tried = true;
                try { template = Build(); }
                catch (System.Exception e) { Plugin.Log.LogError($"Building the {label} for the dummy failed: {e}"); }
            }
            if (template == null) return null;
            GameObject copy = Object.Instantiate(template, parent, false);
            copy.name = label;
            return copy;
        }

        private GameObject Build()
        {
            GameObject prefab = null;
            string used = null;
            foreach (string p in paths)
            {
                prefab = Resources.Load<GameObject>(p);
                if (prefab != null) { used = p; break; }
            }
            if (prefab == null) { Plugin.Log.LogError($"No {label} model found ({string.Join(", ", paths)}); the dummy will be just the stand."); return null; }

            if (holder == null)
            {
                holder = new GameObject("TargetDummy.PrefabHolder");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
            }

            GameObject clone = Object.Instantiate(prefab, holder.transform);   // inactive parent: no Awake/OnEnable runs
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;

            if (readRobeStats) ReadRobeStats(clone);

            // pick the body: mesh/skinned renderers that are switched on in the prefab, minus effects, map icons and hidden parts
            string[] hide = SplitList(hideSetting());
            var all = clone.GetComponentsInChildren<Renderer>(true);
            var keep = new List<Renderer>();
            foreach (Renderer r in all)
                if (Usable(r, clone.transform, hide) && r.enabled && ActiveUnder(r.transform, clone.transform)) keep.Add(r);
            if (keep.Count == 0)
            {
                // the visuals may be switched off until it spawns: fall back to every mesh that isn't an effect
                foreach (Renderer r in all)
                    if (Usable(r, clone.transform, hide) && !LooksLikeEffect(r.transform, clone.transform)) keep.Add(r);
            }
            if (Settings.LogRobeParts.Value) LogParts(used, all, keep, clone.transform);
            if (keep.Count == 0) { Plugin.Log.LogError($"The {label} has no visible meshes; the dummy will be just the stand."); Object.Destroy(clone); return null; }

            GameObject clean = MirrorVisuals(clone, keep);
            clean.name = "TargetDummy " + label;
            Object.Destroy(clone);   // the original copy never woke up; throw it away
            return clean;
        }

        private static void ReadRobeStats(GameObject clone)
        {
            EnemyHealth health = clone.GetComponentInChildren<EnemyHealth>(true);
            if (health == null) { Plugin.Log.LogWarning("The Robe has no EnemyHealth; numbers use raw damage."); return; }
            RobeObjectHurt = health.objectHurt;
            RobeObjectHurtMultiplier = health.objectHurtMultiplier;
            var resist = AccessTools.Field(typeof(EnemyHealth), "damageResistance");
            if (resist != null) RobeDamageResistance = (float)resist.GetValue(health);
            Plugin.Log.LogInfo($"Robe: {health.health} hp, damage resistance {RobeDamageResistance:0.##}, thrown-object multiplier {RobeObjectHurtMultiplier:0.##}.");
        }

        private static bool Usable(Renderer r, Transform root, string[] hide)
        {
            if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) return false;
            string layer = LayerMask.LayerToName(r.gameObject.layer).ToLowerInvariant();
            if (layer.Contains("map") || layer.Contains("ui")) return false;           // minimap icons and the like
            if (Matches(r, hide)) return false;
            for (Transform t = r.transform; t != null && t != root; t = t.parent)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("map") || n.Contains("icon") || n.Contains("flashlight")) return false;
            }
            return true;
        }

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
        /// A clean copy: the same transform tree (so the bones keep their rest pose) with only the kept meshes and
        /// renderers on it. Nothing else from the prefab, so no script on the dummy can ever wake up.
        /// </summary>
        private static GameObject MirrorVisuals(GameObject source, List<Renderer> keep)
        {
            var map = new Dictionary<Transform, Transform>();
            GameObject root = MirrorTree(source.transform, holder.transform, map);

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
                else
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    target.gameObject.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    target.gameObject.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                }
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

        private void LogParts(string path, Renderer[] all, List<Renderer> keep, Transform root)
        {
            string setting = label == "Robe" ? "HideRobeParts" : "HideDroidParts";
            var sb = new StringBuilder($"{label} pieces from '{path}' (+ = shown on the dummy). Hide any with {setting} in TargetDummy.cfg:");
            foreach (Renderer r in all)
                sb.Append("\n  ").Append(keep.Contains(r) ? "+ " : "- ").Append(Path(r.transform, root))
                  .Append(" [").Append(r.GetType().Name).Append(", layer ").Append(LayerMask.LayerToName(r.gameObject.layer)).Append(']');
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
