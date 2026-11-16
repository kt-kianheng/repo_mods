using System.Collections.Generic;
using UnityEngine;

namespace HatterMeleeWeaponsPack
{
    /// <summary>
    /// The red X a katana wave leaves on what it hits: two thin blade strokes, each pointed at both ends, slashing in
    /// one after the other (the first top-left to bottom-right, then the second across it), always facing the camera.
    /// They hold for a moment, then widen slightly and fade. Visual only, on every player's game (the host sends it).
    /// </summary>
    internal class KatanaImpactMark : MonoBehaviour
    {
        private const float StrokeTime = 0.07f;   // each stroke slashes in this fast
        private const float Hold = 0.25f;         // then both stay at full strength this long
        private const float Fade = 0.45f;

        private static Mesh stroke;
        private static Material material;

        private Transform[] strokes;
        private MeshRenderer[] renderers;
        private MaterialPropertyBlock block;
        private Light flash;
        private float age, size;

        internal static void Spawn(Vector3 point, float size)
        {
            var go = new GameObject("HMWP Katana Impact");
            go.transform.position = point;
            var mark = go.AddComponent<KatanaImpactMark>();
            mark.size = size;
            mark.block = new MaterialPropertyBlock();
            mark.strokes = new Transform[2];
            mark.renderers = new MeshRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                // the strokes cross at 90°, each 45° off upright; each is drawn growing from its own start end
                var pivot = new GameObject("Stroke " + (i + 1)).transform;
                pivot.SetParent(go.transform, false);
                pivot.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -45f : 45f);
                var g = new GameObject("Mesh");
                g.transform.SetParent(pivot, false);
                g.AddComponent<MeshFilter>().sharedMesh = Stroke;
                var r = g.AddComponent<MeshRenderer>();
                if (Material != null) r.sharedMaterial = Material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                mark.strokes[i] = pivot;
                mark.renderers[i] = r;
            }
            mark.flash = go.AddComponent<Light>();
            mark.flash.type = LightType.Point;
            mark.flash.color = new Color(0.8f, 0.03f, 0.03f);
            mark.flash.range = 2.5f;
            mark.UpdateLooks();
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age >= StrokeTime * 2f + Hold + Fade) { Destroy(gameObject); return; }
            UpdateLooks();
        }

        private void UpdateLooks()
        {
            float fade = 1f - Mathf.Clamp01((age - StrokeTime * 2f - Hold) / Fade);
            float widen = 1f + 0.25f * Mathf.Clamp01((age - StrokeTime * 2f - Hold) / Fade);
            for (int i = 0; i < 2; i++)
            {
                // stroke i draws in from one end over StrokeTime, starting after the one before it
                float drawn = Mathf.Clamp01((age - i * StrokeTime) / StrokeTime);
                Transform t = strokes[i];
                // the mesh runs 0..1 along local +X from its start end; it's centred on the cross by its parent
                t.localScale = new Vector3(size * widen, size * widen, 1f);
                Transform m = t.GetChild(0);
                m.localPosition = new Vector3(-0.5f, 0f, 0f);
                m.localScale = new Vector3(Mathf.Max(0.001f, drawn), 1f, 1f);
                renderers[i].enabled = drawn > 0f;
                block.SetColor("_Color", new Color(1f, 1f, 1f, fade));
                renderers[i].SetPropertyBlock(block);
            }
            if (flash != null) flash.intensity = 3f * fade * Mathf.Clamp01(age / StrokeTime);
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
        }

        /// <summary>
        /// One stroke, 1 long (local X from 0 to 1) and at most 0.09 wide: pointed at both ends, a bright red core line
        /// fading to clear edges (vertex colours, with Sprites/Default).
        /// </summary>
        private static Mesh Stroke
        {
            get
            {
                if (stroke != null) return stroke;
                const int segments = 16;
                float[] rows = { -1f, -0.35f, 0f, 0.35f, 1f };
                float[] alpha = { 0f, 0.75f, 1f, 0.75f, 0f };
                Color edge = new Color(0.55f, 0.01f, 0.03f), core = new Color(1f, 0.16f, 0.14f);
                Color[] colour = { edge, Color.Lerp(edge, core, 0.5f), core, Color.Lerp(edge, core, 0.5f), edge };
                var verts = new List<Vector3>();
                var colours = new List<Color>();
                var tris = new List<int>();
                for (int s = 0; s <= segments; s++)
                {
                    float x = s / (float)segments;
                    // widest a little past the middle, a sharp point at each end (clamped: no negative to a power)
                    float half = 0.045f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * x)), 0.8f);
                    for (int r = 0; r < rows.Length; r++)
                    {
                        verts.Add(new Vector3(x, rows[r] * half, 0f));
                        Color c = colour[r];
                        c.a = alpha[r];
                        colours.Add(c);
                    }
                }
                int per = rows.Length;
                for (int s = 0; s < segments; s++)
                    for (int r = 0; r < per - 1; r++)
                    {
                        int a = s * per + r, b = a + 1, c = a + per, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                stroke = new Mesh { name = "HMWP Katana Impact Stroke" };
                stroke.SetVertices(verts);
                stroke.SetColors(colours);
                stroke.SetTriangles(tris, 0);
                stroke.RecalculateBounds();
                stroke.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return stroke;
            }
        }

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                Shader s = Shader.Find("Sprites/Default");
                if (s == null) return null;
                material = new Material(s) { name = "HMWP Katana Impact" };
                material.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return material;
            }
        }
    }
}
