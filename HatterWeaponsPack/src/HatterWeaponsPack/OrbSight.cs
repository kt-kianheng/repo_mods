using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// A little screen on top of the Orb Cannon, behind the optic like a rear iron sight, facing the holder: it shows
    /// the loaded orb's size (S, M or L in glowing pink), a dim dash when empty and dim dots while cooling.
    /// Built at runtime under the cannon's visuals (it sits on the sleeve, so it follows the orb-sized tube's radius).
    /// </summary>
    internal class OrbSight
    {
        private const float SleeveRadius = 0.132f;   // the model's sleeve (tools/build_orb_cannon.py: R + 0.012)
        private const float BlenderY = 0.10f;        // behind the optic's eyepiece (Blender y; Unity z = -y)

        private TextMeshPro text;
        private Material screenMat;
        private Transform root;
        private bool headMode;               // the Friendship Cannon: shows the loaded player's head instead of a letter
        private GameObject head;
        private PlayerAvatar headOf;
        private static readonly Color OnBackBlue = new Color(0.02f, 0.1f, 0.16f, 1f);
        private static readonly FieldInfo DeathHeadField = AccessTools.Field(typeof(PlayerAvatar), "playerDeathHead");
        private static readonly Color Off = new Color(0.02f, 0.02f, 0.025f, 1f);
        private static readonly Color OnBack = new Color(0.16f, 0.01f, 0.09f, 1f);

        internal static OrbSight Create(Transform tube, Material metal) => Create(tube, metal, false);

        internal static OrbSight Create(Transform tube, Material metal, bool headMode)
        {
            Transform visuals = tube.parent;
            if (visuals == null) return null;
            float f = Mathf.Abs(tube.localScale.x);
            var s = new OrbSight { headMode = headMode };
            int layer = tube.gameObject.layer;

            var root = new GameObject("Orb Sight");
            root.layer = layer;
            root.transform.SetParent(visuals, false);
            root.transform.localPosition = new Vector3(0f, f * SleeveRadius, -BlenderY);
            s.root = root.transform;

            Box(root.transform, "Sight Post", new Vector3(0f, 0.011f, 0f), new Vector3(0.012f, 0.022f, 0.012f), metal, layer);
            Box(root.transform, "Sight Frame", new Vector3(0f, 0.042f, 0f), new Vector3(0.054f, 0.04f, 0.008f), metal, layer);
            Box(root.transform, "Sight Hood", new Vector3(0f, 0.064f, -0.006f), new Vector3(0.054f, 0.004f, 0.016f), metal, layer);

            // the screen: a dark panel just behind the frame's back face (towards the holder)
            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Sight Screen";
            Object.Destroy(screen.GetComponent<Collider>());
            screen.layer = layer;
            screen.transform.SetParent(root.transform, false);
            screen.transform.localPosition = new Vector3(0f, 0.042f, -0.0045f);
            screen.transform.localScale = new Vector3(0.046f, 0.032f, 1f);
            Shader sprites = Shader.Find("Sprites/Default");
            if (sprites != null)
            {
                s.screenMat = new Material(sprites) { name = "HWP Orb Sight Screen", color = Off };
                var sr = screen.GetComponent<MeshRenderer>();
                sr.sharedMaterial = s.screenMat;
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // the letter
            var textGo = new GameObject("Sight Text", typeof(RectTransform));
            textGo.layer = layer;
            textGo.transform.SetParent(root.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0.042f, -0.0050f);
            s.text = textGo.AddComponent<TextMeshPro>();
            s.text.rectTransform.sizeDelta = new Vector2(0.04f, 0.028f);
            s.text.alignment = TextAlignmentOptions.Center;
            s.text.enableAutoSizing = true;
            s.text.fontSizeMin = 0.01f;
            s.text.fontSizeMax = 2f;
            s.text.fontStyle = FontStyles.Bold;
            s.text.textWrappingMode = TextWrappingModes.NoWrap;
            s.text.text = "-";
            if (headMode) s.ShowHead(null, false); else s.Show(-1);
            return s;
        }

        private static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, int layer)
        {
            GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name;
            Object.Destroy(b.GetComponent<Collider>());
            b.layer = layer;
            b.transform.SetParent(parent, false);
            b.transform.localPosition = pos;
            b.transform.localScale = size;
            if (mat != null) b.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>0/1/2 = small/medium/large loaded; -1 empty; -2 cooling.</summary>
        internal void Show(int kind)
        {
            bool loaded = kind >= 0;
            if (screenMat != null) screenMat.color = loaded ? OnBack : Off;
            if (text == null) return;
            text.text = kind == 0 ? "S" : kind == 1 ? "M" : kind == 2 ? "L" : kind == -2 ? "..." : "-";
            text.color = loaded ? OrbTubes.LiquidPinkLight : new Color(0.35f, 0.3f, 0.33f, 1f);
        }

        /// <summary>
        /// The Friendship Cannon: a little copy of the loaded player's head (their death head's meshes, in their
        /// colours, slowly turning) on a blue-lit screen; a dim dash when empty, dim dots while cooling.
        /// </summary>
        internal void ShowHead(PlayerAvatar p, bool cooling)
        {
            if (screenMat != null) screenMat.color = p != null ? OnBackBlue : Off;
            if (text != null)
            {
                text.text = p != null ? "" : cooling ? "..." : "-";
                text.color = new Color(0.35f, 0.3f, 0.33f, 1f);
            }
            if (p == headOf && (head != null || p == null)) return;
            if (head != null) Object.Destroy(head);
            head = null;
            headOf = p;
            if (p == null || root == null) return;
            try { head = CopyHead(p); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Friendship Cannon: copying {p.name}'s head for the screen failed: {e.Message}"); }
        }

        private GameObject CopyHead(PlayerAvatar p) => CopyHeadModel(p, root, 0.026f, new Vector3(0f, 0.042f, -0.016f), true);

        /// <summary>
        /// A copy of a player's death head (their colours; scripts stripped, legs hidden), 'size' across, centred at
        /// localPosition under parent; with spin, it sways slowly (the sight screen). Null if there's no head to copy.
        /// </summary>
        internal static GameObject CopyHeadModel(PlayerAvatar p, Transform parent, float sizeAcross, Vector3 localPosition, bool spin)
        {
            Transform root = parent;
            PlayerDeathHead dh = DeathHeadField != null ? DeathHeadField.GetValue(p) as PlayerDeathHead : null;
            GameObject src = dh != null ? (dh.meshHeadParent != null ? dh.meshHeadParent : dh.meshParent) : null;
            if (src == null) return null;
            var temp = new GameObject("Head (building)");
            temp.SetActive(false);
            GameObject copy = Object.Instantiate(src, temp.transform);
            for (int pass = 0; pass < 3; pass++)
                foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer) continue;
                    try { Object.DestroyImmediate(c); } catch { }
                }
            int layer = root.gameObject.layer;
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
                if (t.name.ToLowerInvariant().Contains("leg")) t.gameObject.SetActive(false);   // just the head
            }
            // the head's size, to fit the screen
            Bounds b = default;
            bool any = false;
            Matrix4x4 toCopy = copy.transform.worldToLocalMatrix;
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.gameObject.activeSelf) continue;
                r.enabled = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : r is SkinnedMeshRenderer sk ? sk.sharedMesh : null;
                if (mesh == null) continue;
                Matrix4x4 m = toCopy * r.transform.localToWorldMatrix;
                Bounds mb = mesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 c = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                }
            }
            var holder = new GameObject("Head Copy");
            holder.layer = layer;
            holder.transform.SetParent(root, false);
            holder.transform.localPosition = localPosition;
            var pivot = new GameObject("Spin");
            pivot.layer = layer;
            pivot.transform.SetParent(holder.transform, false);
            if (spin) pivot.AddComponent<SightSpin>();
            copy.transform.SetParent(pivot.transform, false);
            float size = any ? Mathf.Max(b.size.x, b.size.y, b.size.z) : 0.3f;
            float scale = sizeAcross / Mathf.Max(0.01f, size);
            copy.transform.localScale = Vector3.one * scale;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localPosition = any ? -b.center * scale : Vector3.zero;
            copy.SetActive(true);
            Object.Destroy(temp);
            return holder;
        }

        internal void Cleanup()
        {
            if (head != null) Object.Destroy(head);
            if (screenMat != null) Object.Destroy(screenMat);
        }
    }

    /// <summary>Turns the head on the Friendship Cannon's screen slowly, so it reads as a little 3D portrait.</summary>
    internal class SightSpin : MonoBehaviour
    {
        private void Update() => transform.localRotation = Quaternion.Euler(0f, 180f + Mathf.Sin(Time.time * 0.8f) * 35f, 0f);
    }
}
