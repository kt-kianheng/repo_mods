using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HatterMiscellaneousPack
{
    /// <summary>
    /// A lifeless copy of the game's own Robe (the Enemy Compass's emblem), as the TargetDummy mod does: the enemy prefab
    /// is copied once under an inactive parent (so none of its scripts wake up), and a clean copy is made of just its
    /// bones, meshes and materials in their rest pose. No AI, sound, physics or network.
    /// </summary>
    internal static class RobeMini
    {
        private const string RobePath = "Enemies/Enemy - Robe";
        private static GameObject holder, template;
        private static bool tried;

        /// <summary>A copy standing on <paramref name="parent"/>'s origin, <paramref name="height"/> tall (parent's units), or null.</summary>
        internal static GameObject Spawn(Transform parent, float height, float yaw)
        {
            if (!tried)
            {
                tried = true;
                try { template = Build(); }
                catch (System.Exception e) { Plugin.Log.LogError($"Enemy Compass: building the little Robe failed: {e}"); }
            }
            if (template == null) return null;

            // measured at the world origin, unturned, then placed under the parent with the same local numbers
            GameObject copy = Object.Instantiate(template);
            copy.name = "Little Robe";
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            copy.transform.localScale = Vector3.one;
            copy.SetActive(true);
            bool any = false;
            Bounds b = default;
            void Add(Bounds local, Matrix4x4 toWorld)
            {
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = toWorld.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            // measured from the meshes (a renderer's own bounds aren't up to date the frame it's made)
            // skinned meshes: the mesh's own bounds where it sits (their authored render bounds are often padded, which
            // made the Robe come out smaller than meant)
            foreach (SkinnedMeshRenderer smr in copy.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (smr.sharedMesh != null) Add(smr.sharedMesh.bounds, smr.transform.localToWorldMatrix);
            foreach (MeshFilter mf in copy.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) Add(mf.sharedMesh.bounds, mf.transform.localToWorldMatrix);
            if (!any || b.size.y < 1e-4f)
            {
                Plugin.Log.LogWarning("Enemy Compass: the little Robe has no size; the built-in emblem stays.");
                Object.Destroy(copy);
                return null;
            }
            Plugin.Log.LogInfo($"Enemy Compass: the Robe measures {b.size.y:0.00} m tall; shown {height:0.000} tall on the compass.");
            float f = height / b.size.y;
            Vector3 bottom = new Vector3(b.center.x, b.min.y, b.center.z);
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            copy.transform.SetParent(parent, false);
            copy.transform.localRotation = turn;
            copy.transform.localScale = Vector3.one * f;
            copy.transform.localPosition = turn * (-bottom * f);
            SetLayer(copy.transform, parent.gameObject.layer);
            return copy;
        }

        private static GameObject Build()
        {
            GameObject prefab = Resources.Load<GameObject>(RobePath);
            if (prefab == null) { Plugin.Log.LogError($"Enemy Compass: '{RobePath}' not found; the compass has no little Robe."); return null; }
            if (holder == null)
            {
                holder = new GameObject("HatterMiscellaneousPack.RobeHolder");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
            }
            GameObject clone = Object.Instantiate(prefab, holder.transform);   // inactive parent: no Awake/OnEnable runs
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;

            string[] hide = SplitList(Settings.CompassHideRobeParts.Value);
            var all = clone.GetComponentsInChildren<Renderer>(true);
            var keep = new List<Renderer>();
            foreach (Renderer r in all)
                if (Usable(r, clone.transform, hide) && r.enabled && ActiveUnder(r.transform, clone.transform)) keep.Add(r);
            if (keep.Count == 0)
                foreach (Renderer r in all)
                    if (Usable(r, clone.transform, hide) && !LooksLikeEffect(r.transform, clone.transform)) keep.Add(r);

            var sb = new StringBuilder($"Enemy Compass: Robe pieces (+ = shown on the compass; hide any with HideRobeParts in [Compasses]):");
            foreach (Renderer r in all) sb.Append("\n  ").Append(keep.Contains(r) ? "+ " : "- ").Append(Path(r.transform, clone.transform));
            Plugin.Log.LogInfo(sb.ToString());
            if (keep.Count == 0) { Object.Destroy(clone); return null; }

            GameObject clean = Mirror(clone, keep);
            clean.name = "HatterMiscellaneousPack Robe";
            Object.Destroy(clone);
            return clean;
        }

        private static bool Usable(Renderer r, Transform root, string[] hide)
        {
            if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) return false;
            string layer = LayerMask.LayerToName(r.gameObject.layer).ToLowerInvariant();
            if (layer.Contains("map") || layer.Contains("ui")) return false;
            string name = r.name.ToLowerInvariant();
            foreach (string w in hide) if (name.Contains(w)) return false;
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

        /// <summary>The same transform tree with only the kept meshes and renderers on it (inactive, under the holder).</summary>
        private static GameObject Mirror(GameObject source, List<Renderer> keep)
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
                    for (int i = 0; i < bones.Length; i++) mapped[i] = bones[i] != null && map.TryGetValue(bones[i], out Transform bt) ? bt : null;
                    dst.bones = mapped;
                    dst.rootBone = src.rootBone != null && map.TryGetValue(src.rootBone, out Transform rb) ? rb : null;
                    dst.localBounds = src.localBounds;
                    dst.quality = src.quality;
                    dst.updateWhenOffscreen = false;
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
            root.SetActive(true);   // still inactive in the hierarchy (the holder is off)
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

        private static string Path(Transform t, Transform root)
        {
            string p = t.name;
            for (t = t.parent; t != null && t != root; t = t.parent) p = t.name + "/" + p;
            return p;
        }
    }
}
