using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace HatterMiscellaneousPack
{
    /// <summary>
    /// The Extraction Compass's emblem: a miniature of the level's real extraction screen (the emoji screen with the
    /// haul display above it), copied at runtime from an ExtractionPoint in the level, the way the Enemy Compass borrows
    /// the Robe. Only meshes, materials and the two screen texts are copied; no script. The texts are then kept in step
    /// with the real ones, so the miniature shows the same emoji and haul as the extraction point.
    ///
    /// Which pieces make up "the screen" is found from the two screen texts (ExtractionPoint.emojiScreen and
    /// haulGoalScreen): meshes whose middle lies in a box round them (wider, and reaching down for the stand), except the
    /// spotlights, tube, platform, ramp, button and effects. Every piece is logged with + (used) or -; if the pick is
    /// wrong, [Compasses] ExtractionScreenParts lists the pieces to use instead.
    /// </summary>
    internal class ExtractionMini : MonoBehaviour
    {
        private TMP_Text emojiCopy, haulCopy;
        private TMP_Text emojiSource, haulSource;
        private float nextSync;
        private static bool logged;

        /// <summary>A miniature standing on <paramref name="parent"/>'s origin, <paramref name="height"/> tall, facing its +Z; null if the level has no extraction point.</summary>
        internal static ExtractionMini Spawn(Transform parent, float height)
        {
            ExtractionPoint source = null;
            foreach (ExtractionPoint ep in Object.FindObjectsByType<ExtractionPoint>(FindObjectsSortMode.None))
            {
                if (ep == null || ep.emojiScreen == null) continue;
                source = ep;
                break;
            }
            if (source == null) return null;

            Transform frame = source.transform;
            Matrix4x4 toFrame = frame.worldToLocalMatrix;
            List<Renderer> pieces = Pick(source, out string report);
            if (!logged) { logged = true; Plugin.Log.LogInfo(report); }
            if (pieces.Count == 0 && source.emojiScreen == null) return null;

            var root = new GameObject("Little Extraction Screen (from the level)");
            var inner = new GameObject("Pieces").transform;   // in the extraction point's own frame
            inner.SetParent(root.transform, false);
            bool any = false;
            Bounds b = default;
            void Grow(Bounds wb)
            {
                // the world bounds' corners, in the extraction point's frame
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = toFrame.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }

            foreach (Renderer r in pieces)
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var go = new GameObject(r.name);
                go.transform.SetParent(inner, false);
                Place(go.transform, toFrame * r.transform.localToWorldMatrix);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Grow(r.bounds);
            }

            int meshCount = inner.childCount;
            var mini = root.AddComponent<ExtractionMini>();
            mini.emojiSource = source.emojiScreen;
            mini.haulSource = source.haulGoalScreen;
            mini.emojiCopy = CopyText(source.emojiScreen, inner, toFrame, Grow);
            mini.haulCopy = CopyText(source.haulGoalScreen, inner, toFrame, Grow);
            // only a believable screen replaces the built-in emblem: some solid pieces, roughly screen-sized
            float tallest = any ? Mathf.Max(b.size.x, b.size.y, b.size.z) : 0f;
            Plugin.Log.LogInfo($"Extraction Compass: copied {meshCount} piece(s) from '{source.name}', {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m.");
            if (!any || meshCount == 0 || b.size.y < 0.15f || tallest > 4f)
            {
                Plugin.Log.LogWarning("Extraction Compass: the copied screen doesn't look right (no pieces, or the wrong size); the built-in dollar sign stays. Send the 'extraction point's pieces' list to fix the pick, or set ExtractionScreenParts.");
                Object.Destroy(root);
                return null;
            }

            // which way the screen faces (a text reads from in front of it: its -forward), turned to face the parent's +Z
            Vector3 face = source.emojiScreen != null ? toFrame.MultiplyVector(-source.emojiScreen.transform.forward) : Vector3.forward;
            face.y = 0f;
            Quaternion turn = face.sqrMagnitude > 1e-4f ? Quaternion.FromToRotation(face.normalized, Vector3.forward) : Quaternion.identity;
            float f = height / b.size.y;
            Vector3 bottom = new Vector3(b.center.x, b.min.y, b.center.z);
            root.transform.SetParent(parent, false);
            root.transform.localRotation = turn;
            root.transform.localScale = Vector3.one * f;
            root.transform.localPosition = turn * (-bottom * f);
            SetLayer(root.transform, parent.gameObject.layer);
            mini.Sync();
            return mini;
        }

        /// <summary>Position, rotation and scale from a matrix (no shear expected).</summary>
        private static void Place(Transform t, Matrix4x4 m)
        {
            t.localPosition = m.GetColumn(3);
            t.localRotation = m.rotation;
            t.localScale = m.lossyScale;
        }

        /// <summary>A copy of a screen text (the text component and its sub-meshes, nothing else).</summary>
        private static TMP_Text CopyText(TMP_Text source, Transform parent, Matrix4x4 toFrame, System.Action<Bounds> grow)
        {
            if (source == null) return null;
            GameObject go = Object.Instantiate(source.gameObject, parent, false);
            go.name = source.name;
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && !(mb is TMP_Text) && !(mb is TMP_SubMesh)) Object.DestroyImmediate(mb);
            foreach (Transform child in go.transform)
                if (child.GetComponent<TMP_SubMesh>() == null) Object.Destroy(child.gameObject);
            go.SetActive(true);
            Place(go.transform, toFrame * source.transform.localToWorldMatrix);
            Renderer r = source.GetComponent<Renderer>();
            if (r != null && r.bounds.size.sqrMagnitude > 1e-6f) grow(r.bounds);
            else grow(new Bounds(source.transform.position, Vector3.one * 0.2f));
            return go.GetComponent<TMP_Text>();
        }

        /// <summary>The screen's pieces: the configured names, or the meshes round the two screen texts.</summary>
        private static List<Renderer> Pick(ExtractionPoint ep, out string report)
        {
            var result = new List<Renderer>();
            Transform root = ep.transform;
            string[] names = SplitList(Settings.CompassExtractionParts.Value);

            // the region: round the two texts, wider, and reaching down for the stand
            Bounds region = new Bounds(ep.emojiScreen.transform.position, Vector3.one * 0.3f);
            foreach (TMP_Text t in new[] { ep.emojiScreen, ep.haulGoalScreen })
            {
                if (t == null) continue;
                Renderer tr = t.GetComponent<Renderer>();
                region.Encapsulate(tr != null && tr.bounds.size.sqrMagnitude > 1e-6f ? tr.bounds : new Bounds(t.transform.position, Vector3.one * 0.3f));
            }
            region.Expand(new Vector3(0.35f, 0.2f, 0.35f));
            region.SetMinMax(region.min - Vector3.up * 0.8f, region.max);

            var exclude = new List<Transform>();
            foreach (Transform t in new[] { ep.spotlightHead1, ep.spotlightHead2, ep.extractionTube, ep.platform, ep.ramp,
                                            ep.button != null ? ep.button.transform : null, ep.haulBar,
                                            ep.hurtColliders != null ? ep.hurtColliders.transform : null,
                                            ep.extractionArea != null ? ep.extractionArea.transform : null,
                                            ep.grossUp != null ? ep.grossUp.transform : null,
                                            ep.emojiScreenGlitch != null ? ep.emojiScreenGlitch.transform : null,
                                            ep.roomVolume != null ? ep.roomVolume.transform : null })
                if (t != null) exclude.Add(t);
            // the spotlights' own stands: whatever holds each spotlight head, unless it also holds the screen
            foreach (Transform head in new[] { ep.spotlightHead1, ep.spotlightHead2 })
                if (head != null && head.parent != null && head.parent != root && !ep.emojiScreen.transform.IsChildOf(head.parent)) exclude.Add(head.parent);

            var sb = new StringBuilder("Extraction Compass: the extraction point's pieces (+ = on the compass; set [Compasses] ExtractionScreenParts to choose others):");
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || r.GetComponent<TMP_Text>() != null || r.GetComponent<TMP_SubMesh>() != null) continue;
                string path = Path(r.transform, root);
                bool use;
                if (names.Length > 0)
                {
                    string lower = path.ToLowerInvariant();
                    use = false;
                    foreach (string n in names) if (lower.Contains(n)) { use = true; break; }
                }
                else
                {
                    use = r.enabled && r.gameObject.activeInHierarchy && region.Contains(r.bounds.center);
                    foreach (Transform x in exclude) if (r.transform == x || r.transform.IsChildOf(x)) { use = false; break; }
                }
                if (use) result.Add(r);
                sb.Append("\n  ").Append(use ? "+ " : "- ").Append(path);
            }
            report = sb.ToString();
            return result;
        }

        private void Update()
        {
            if (Time.time >= nextSync) Sync();
        }

        /// <summary>Shows what the real screens show.</summary>
        private void Sync()
        {
            nextSync = Time.time + 0.5f;
            if (emojiCopy != null && emojiSource != null && emojiCopy.text != emojiSource.text) emojiCopy.text = emojiSource.text;
            if (haulCopy != null && haulSource != null && haulCopy.text != haulSource.text) haulCopy.text = haulSource.text;
        }

        private static string[] SplitList(string s)
        {
            var list = new List<string>();
            foreach (string p in (s ?? "").Split(',')) { string q = p.Trim().ToLowerInvariant(); if (q.Length > 0) list.Add(q); }
            return list.ToArray();
        }

        private static string Path(Transform t, Transform root)
        {
            string p = t.name;
            for (t = t.parent; t != null && t != root; t = t.parent) p = t.name + "/" + p;
            return p;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }
    }
}
