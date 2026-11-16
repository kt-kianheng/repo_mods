using System.Collections.Generic;
using UnityEngine;

namespace HatterMeleeWeaponsPack
{
    /// <summary>
    /// A rift torn in the air along the charged katana's stroke: a thin, jagged slit, black inside with glowing red
    /// edges, pointed at both ends, always turned to face the camera. It is drawn as the host sends its points
    /// (<see cref="Katana"/>), then either opens (it gapes, then hangs in the air building up, wider and brighter, until
    /// its wave launches, then vanishes at once) or fizzles (it fades) if the stroke was too short. A chain's later
    /// rifts are centred on a given point (the first rift's middle): the stroke's shape is shown moved so that its
    /// middle, halfway along its length, sits there, so it grows out from that point as it's drawn. Visual only, on every player's game.
    /// </summary>
    internal class KatanaRift : MonoBehaviour
    {
        private enum Phase { Drawing, Opening, Holding, Closing, Fizzling }

        private const float DrawWidth = 0.03f;      // half-width while it's being drawn
        private const float OpenWidth = 0.13f;      // half-width when it gapes as the wave comes out
        private const float OpenTime = 0.1f, CloseTime = 0.25f, FizzleTime = 0.3f;
        private const float LostTime = 3f;          // a rift that gets no points or end for this long fizzles by itself

        // across the slit, from one outer glow edge to the other: offset (in half-widths), colour, alpha
        private static readonly float[] Rows = { -1.9f, -1f, -0.45f, 0f, 0.45f, 1f, 1.9f };
        private static readonly Color Rim = new Color(0.9f, 0.05f, 0.05f), Glow = new Color(0.6f, 0.0f, 0.02f),
                                      Dark = new Color(0.12f, 0f, 0.01f), Black = new Color(0.01f, 0f, 0f);
        private static readonly Color[] RowColour = { Glow, Rim, Dark, Black, Dark, Rim, Glow };
        private static readonly float[] RowAlpha = { 0f, 1f, 1f, 1f, 1f, 1f, 0f };

        private static Material material;

        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<float> jag = new List<float>();     // per point: sideways wobble of the slit, -1..1
        private readonly List<float> girth = new List<float>();   // per point: width wobble, about 1
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Color> colours = new List<Color>();
        private readonly List<int> tris = new List<int>();
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;
        private Light glowLight;
        private Phase phase = Phase.Drawing;
        private float phaseTime, lastAdded, holdTime;
        private System.Action onLaunch;   // called when the rift has hung for its time: the wave launches
        private bool centred;
        private Vector3 centre;
        private Vector3 offset;   // added to every point when drawn (centred rifts)

        internal static KatanaRift Create()
        {
            var go = new GameObject("HMWP Katana Rift");   // at the world origin: its points are world positions
            var rift = go.AddComponent<KatanaRift>();
            rift.mesh = new Mesh { name = "HMWP Katana Rift" };
            rift.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = rift.mesh;
            rift.meshRenderer = go.AddComponent<MeshRenderer>();
            if (Material != null) rift.meshRenderer.sharedMaterial = Material;
            rift.meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rift.meshRenderer.receiveShadows = false;
            rift.block = new MaterialPropertyBlock();
            var lightObject = new GameObject("Rift Light");
            lightObject.transform.SetParent(go.transform, false);
            rift.glowLight = lightObject.AddComponent<Light>();
            rift.glowLight.type = LightType.Point;
            rift.glowLight.color = new Color(0.85f, 0.04f, 0.04f);
            rift.glowLight.range = 2.5f;
            rift.glowLight.intensity = 0f;
            rift.lastAdded = Time.time;
            return rift;
        }

        internal void Add(Vector3 point)
        {
            if (phase != Phase.Drawing) return;
            points.Add(point);
            // smoothed noise: each point's wobble leans on the last one's, so the tear zig-zags gently rather than spiking
            float lastJag = jag.Count > 0 ? jag[jag.Count - 1] : 0f, lastGirth = girth.Count > 0 ? girth[girth.Count - 1] : 1f;
            jag.Add(Mathf.Clamp(lastJag * 0.55f + Random.Range(-1f, 1f) * 0.6f, -1f, 1f));
            girth.Add(Mathf.Clamp(lastGirth * 0.6f + Random.Range(0.75f, 1.25f) * 0.4f, 0.75f, 1.25f));
            lastAdded = Time.time;
        }

        internal void CentreOn(Vector3 point) { centred = true; centre = point; }

        /// <summary>The point halfway along the drawn points' length.</summary>
        private Vector3 Middle()
        {
            float total = 0f;
            for (int i = 1; i < points.Count; i++) total += Vector3.Distance(points[i - 1], points[i]);
            float target = total * 0.5f, run = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                float seg = Vector3.Distance(points[i - 1], points[i]);
                if (run + seg >= target) return seg > 1e-6f ? Vector3.Lerp(points[i - 1], points[i], (target - run) / seg) : points[i];
                run += seg;
            }
            return points.Count > 0 ? points[points.Count - 1] : centre;
        }

        /// <summary>The rift opens, hangs for `delay` seconds in all, then calls `launch` (its wave) and vanishes.</summary>
        internal void Open(float delay, System.Action launch)
        {
            if (phase != Phase.Drawing) return;
            phase = Phase.Opening;
            phaseTime = 0f;
            holdTime = Mathf.Max(0f, delay - OpenTime);
            onLaunch = launch;
        }

        internal void Fizzle() { if (phase == Phase.Drawing || phase == Phase.Opening) { phase = Phase.Fizzling; phaseTime = 0f; } }

        private void Update()
        {
            phaseTime += Time.deltaTime;
            if (phase == Phase.Drawing && Time.time - lastAdded > LostTime) Fizzle();
            if (phase == Phase.Opening && phaseTime >= OpenTime) { phase = Phase.Holding; phaseTime = 0f; }
            if (phase == Phase.Holding && phaseTime >= holdTime)
            {
                System.Action launch = onLaunch;
                onLaunch = null;
                if (launch != null)
                {
                    try { launch(); }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"Katana rift: launching its wave failed: {e}"); }
                }
                // the wave is out: the rift is gone at once (no closing animation)
                Destroy(gameObject);
                return;
            }
            if ((phase == Phase.Closing && phaseTime >= CloseTime) || (phase == Phase.Fizzling && phaseTime >= FizzleTime))
            {
                Destroy(gameObject);
                return;
            }
        }

        private void LateUpdate()
        {
            float width, alpha, light;
            switch (phase)
            {
                case Phase.Opening:
                    float k = Mathf.Clamp01(phaseTime / OpenTime);
                    width = Mathf.Lerp(DrawWidth, OpenWidth * 0.75f, k); alpha = 1f; light = Mathf.Lerp(1.5f, 3f, k);
                    break;
                case Phase.Holding:
                    // open and building up to the launch: widening and brightening, faster towards the end, edges flickering
                    float h = holdTime > 0f ? Mathf.Clamp01(phaseTime / holdTime) : 1f;
                    width = Mathf.Lerp(OpenWidth * 0.75f, OpenWidth * 1.15f, h * h) * (0.94f + 0.06f * Mathf.Sin(Time.time * (25f + 35f * h)));
                    alpha = 1f; light = Mathf.Lerp(3f, 6f, h * h);
                    break;
                case Phase.Closing:
                    float c = Mathf.Clamp01(phaseTime / CloseTime);
                    // the wave has launched: the rift snaps shut with a flash
                    width = Mathf.Lerp(OpenWidth * 1.15f, 0f, Mathf.Sqrt(c)); alpha = 1f - c * c; light = 8f * (1f - c);
                    break;
                case Phase.Fizzling:
                    float f = Mathf.Clamp01(phaseTime / FizzleTime);
                    width = DrawWidth * (1f - f); alpha = 1f - f; light = 1.5f * (1f - f);
                    break;
                default:
                    width = DrawWidth * (0.9f + 0.1f * Mathf.Sin(Time.time * 40f)); alpha = 1f; light = 1.5f;
                    break;
            }
            Rebuild(width);
            block.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
            meshRenderer.SetPropertyBlock(block);
            if (glowLight != null)
            {
                glowLight.intensity = light;
                if (points.Count > 0) glowLight.transform.position = points[points.Count / 2] + offset;
            }
        }

        /// <summary>A ribbon through the points, turned to the camera, tapering to a point at both ends.</summary>
        private void Rebuild(float width)
        {
            verts.Clear(); colours.Clear(); tris.Clear();
            Camera cam = Camera.main;
            int n = points.Count;
            if (cam == null || n < 2) { mesh.Clear(); return; }
            Vector3 eye = cam.transform.position;
            // a centred rift keeps its middle on the centre (while it's drawn, it grows out from there both ways)
            offset = centred ? centre - Middle() : Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                // the line's direction over a few points either side, so small zig-zags don't swing the ribbon about
                Vector3 tangent = points[Mathf.Min(i + 3, n - 1)] - points[Mathf.Max(i - 3, 0)];
                Vector3 side = Vector3.Cross(tangent, eye - (points[i] + offset));
                side = side.sqrMagnitude > 1e-10f ? side.normalized : Vector3.up;
                float t = i / (float)(n - 1);
                // pointed ends (clamped: a negative number to a fractional power is NaN, which hides the whole mesh)
                float taper = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t)), 0.6f);
                float half = width * taper * girth[i];
                Vector3 mid = points[i] + offset + side * (jag[i] * DrawWidth * 0.5f * taper);   // a jagged tear (as drawn; opening doesn't exaggerate it)
                for (int r = 0; r < Rows.Length; r++)
                {
                    verts.Add(mid + side * (Rows[r] * half));
                    Color col = RowColour[r];
                    col.a = RowAlpha[r];
                    colours.Add(col);
                }
            }
            int per = Rows.Length;
            for (int i = 0; i < n - 1; i++)
                for (int r = 0; r < per - 1; r++)
                {
                    int a = i * per + r, b = a + 1, c = a + per, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                Shader s = Shader.Find("Sprites/Default");
                if (s == null) { Plugin.Log.LogWarning("Katana rift: Sprites/Default shader not found; the rift will be invisible."); return null; }
                material = new Material(s) { name = "HMWP Katana Rift" };
                material.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return material;
            }
        }
    }
}
