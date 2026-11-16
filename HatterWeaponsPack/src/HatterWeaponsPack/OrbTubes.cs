using System.Collections.Generic;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// The Orb Cannon's liquid tubes: round glass tubes laid over the model's flat circuit traces (the same paths as
    /// tools/build_orb_cannon.py: four main runs from the rear flare to the muzzle collar with 45-degree jogs, four
    /// short branches ending in round caps), held down by metal clamps. Inside each is a core with UVs running along
    /// its length, so a scrolling texture makes the pink glow look like liquid flowing towards the muzzle.
    /// Built in the model's own (nominal) space and parented to the tube object, so it scales with it.
    /// </summary>
    internal static class OrbTubes
    {
        // the model's numbers (tools/build_orb_cannon.py)
        private const float R = 0.12f, RS = R + 0.012f, SleeveTop = 0.13f, SleeveBottom = -0.20f, Step = 0.006f;
        private const float MainCore = 0.0030f, MainGlass = 0.0046f, BranchCore = 0.0018f, BranchGlass = 0.0030f;
        private const int Sides = 10;
        private const float TileLength = 0.05f;   // the flow texture repeats every 5 cm along a tube

        private static readonly float[][] Traces =
        {
            new float[] { 28, 0.30f, 14, -0.02f, -14, -0.40f, 10 },
            new float[] { 152, 0.36f, -12, 0.05f, 12, -0.35f, -14 },
            new float[] { 212, 0.25f, 12, -0.10f, -12 },
            new float[] { 332, 0.38f, -10, 0.00f, 12, -0.45f, -12 },
        };
        // (trace, y where it leaves the trace, turn in degrees, straight run in metres)
        private static readonly float[][] Branches =
        {
            new float[] { 0, 0.20f, 12, 0.05f },
            new float[] { 3, -0.25f, -12, 0.04f },
            new float[] { 1, -0.15f, 12, 0.05f },
            new float[] { 2, 0.35f, -10, 0.04f },
        };

        private static Mesh coreMesh, glassMesh, clampMesh;

        // the liquid's colours: deeper and more saturated than the cannon's other pinks
        internal static readonly Color LiquidPink = new Color(1f, 0.05f, 0.55f, 1f);
        internal static readonly Color LiquidPinkLight = new Color(1f, 0.28f, 0.72f, 1f);
        internal static readonly Color GlassColor = new Color(0.55f, 0.6f, 0.65f, 0.22f);
        /// <summary>How bright the lit liquid is: its colour is pushed above 1 (HDR), so it reads as a glow (and blooms where the game's bloom is on).</summary>
        internal const float Brightness = 2.2f;

        /// <summary>The lit liquid's colour for a breathing value 0-1, and the glass taking on its tint like a glow round it.</summary>
        internal static void SetLit(Parts p, float breathe)
        {
            if (p == null) return;
            Color c = Color.Lerp(p.ColorA, p.ColorB, breathe);
            if (p.Glow != null) p.Glow.color = new Color(c.r * Brightness, c.g * Brightness, c.b * Brightness, 1f);
            if (p.Glass != null) p.Glass.color = new Color(c.r, c.g, c.b, 0.34f + 0.08f * breathe);
        }

        /// <summary>Unlit: the glass goes back to plain grey.</summary>
        internal static void SetUnlit(Parts p)
        {
            if (p != null && p.Glass != null) p.Glass.color = GlassColor;
        }

        // the Friendship Cannon's light blue
        internal static readonly Color LiquidBlue = new Color(0.25f, 0.72f, 1f, 1f);
        internal static readonly Color LiquidBlueLight = new Color(0.6f, 0.9f, 1f, 1f);
        private static Texture2D flowTex;

        internal class Parts
        {
            internal MeshRenderer Core;
            internal Material Glow, Dark, Glass;
            internal Color ColorA, ColorB;   // the liquid breathes between these
        }

        /// <summary>Adds the tubes under the cannon's tube object. 'metal' (the model's OC Metal) is used for the clamps.</summary>
        internal static Parts Attach(Transform tube, Material metal) => Attach(tube, metal, LiquidPink, LiquidPinkLight);

        internal static Parts Attach(Transform tube, Material metal, Color colorA, Color colorB)
        {
            Build();
            var parts = new Parts { ColorA = colorA, ColorB = colorB };
            Shader sprites = Shader.Find("Sprites/Default");
            if (sprites == null) return null;

            parts.Glow = new Material(sprites) { name = "HWP Orb Cannon Liquid", mainTexture = FlowTexture(), color = colorA };
            parts.Dark = new Material(sprites) { name = "HWP Orb Cannon Liquid (empty)", color = new Color(0.05f, 0.045f, 0.05f, 1f) };
            parts.Glass = new Material(sprites) { name = "HWP Orb Cannon Glass", color = GlassColor, renderQueue = 3100 };

            parts.Core = Child(tube, "Liquid Tubes (core)", coreMesh, parts.Dark);
            Child(tube, "Liquid Tubes (glass)", glassMesh, parts.Glass);
            if (clampMesh != null) Child(tube, "Liquid Tube Clamps", clampMesh, metal != null ? metal : parts.Dark);
            return parts;
        }

        private static MeshRenderer Child(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        /// <summary>A soft pink stream with brighter blobs and darker gaps (tinted by the material colour).</summary>
        private static Texture2D FlowTexture()
        {
            if (flowTex != null) return flowTex;
            const int w = 128, h = 8;
            flowTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "HWP Orb Liquid Flow" };
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w * Mathf.PI * 2f;
                // a few travelling blobs of different sizes, a gentle base level in between
                float blob = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * 2f)), 6f) * 0.9f + Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * 3f + 1.3f)), 10f) * 0.6f;
                float level = 0.9f + 0.08f * Mathf.Sin(u * 5f + 0.4f) + blob * 0.5f;
                for (int y = 0; y < h; y++)
                {
                    // brighter down the middle of the tube's front, like light in a liquid
                    float across = 0.85f + 0.15f * Mathf.Cos((y + 0.5f) / h * Mathf.PI * 2f);
                    float v = Mathf.Clamp01(level * across);
                    float white = Mathf.Clamp01(blob - 0.6f) * 0.35f;   // the blob centres lighten only a little (keeps it saturated)
                    flowTex.SetPixel(x, y, new Color(Mathf.Lerp(v, 1f, white), Mathf.Lerp(v, 1f, white), Mathf.Lerp(v, 1f, white), 1f));
                }
            }
            flowTex.Apply();
            flowTex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return flowTex;
        }

        // ---------------- geometry ----------------

        private static void Build()
        {
            if (coreMesh != null) return;
            var core = new MeshData();
            var glass = new MeshData();
            var clamps = new MeshData();
            var paths = new List<List<Vector2>>();
            foreach (float[] t in Traces)
            {
                List<Vector2> p = TracePath(t);
                paths.Add(p);
                List<Vector2> s = Resample(p);
                Tube(core, s, MainCore, true);
                Tube(glass, s, MainGlass, false);
                // clamps every ~15 cm along the run
                float run = 0f;
                for (int i = 1; i < s.Count; i++)
                {
                    run += Dist(s[i - 1], s[i]);
                    if (run >= 0.15f && i < s.Count - 3) { run = 0f; Clamp(clamps, s, i, MainGlass); }
                }
            }
            foreach (float[] b in Branches)
            {
                List<Vector2> trace = paths[(int)b[0]];
                float yb = b[1], dth = b[2] * Mathf.Deg2Rad, run = b[3];
                float th0 = ThetaAt(trace, yb);
                float y1 = yb - R * Mathf.Abs(dth), th1 = th0 + dth;
                var bp = new List<Vector2> { new Vector2(yb, th0), new Vector2(y1, th1), new Vector2(y1 - run, th1) };
                List<Vector2> s = Resample(bp);
                Tube(core, s, BranchCore, true);
                Tube(glass, s, BranchGlass, false);
                Cap(clamps, s[s.Count - 1], BranchGlass * 1.8f);   // a round connector where the branch ends
            }
            coreMesh = core.ToMesh("HWP Orb Liquid Core");
            glassMesh = glass.ToMesh("HWP Orb Liquid Glass");
            clampMesh = clamps.ToMesh("HWP Orb Liquid Clamps");
        }

        /// <summary>(y, theta) control points: straight runs, a 45-degree diagonal at each jog (as the Python).</summary>
        private static List<Vector2> TracePath(float[] t)
        {
            float th = t[0] * Mathf.Deg2Rad;
            var pts = new List<Vector2> { new Vector2(0.44f, th) };
            for (int i = 1; i + 1 < t.Length; i += 2)
            {
                float y = t[i], dth = t[i + 1] * Mathf.Deg2Rad;
                pts.Add(new Vector2(y, th));
                th += dth;
                pts.Add(new Vector2(y - R * Mathf.Abs(dth), th));
            }
            pts.Add(new Vector2(-0.61f, th));
            return pts;
        }

        private static float ThetaAt(List<Vector2> pts, float y)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                if (a.x >= y && y >= b.x) return Mathf.Abs(a.x - b.x) < 1e-6f ? a.y : a.y + (b.y - a.y) * (a.x - y) / (a.x - b.x);
            }
            return pts[pts.Count - 1].y;
        }

        private static List<Vector2> Resample(List<Vector2> pts)
        {
            var o = new List<Vector2> { pts[0] };
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float len = Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + R * R * (b.y - a.y) * (b.y - a.y));
                int n = Mathf.Max(1, Mathf.CeilToInt(len / Step));
                for (int k = 1; k <= n; k++) o.Add(Vector2.Lerp(a, b, k / (float)n));
            }
            return o;
        }

        private static float SurfaceRadius(float y)
        {
            const float ramp = 0.004f;
            if (y <= SleeveTop && y >= SleeveBottom) return RS;
            if (y > SleeveTop && y < SleeveTop + ramp) return RS - (RS - R) * (y - SleeveTop) / ramp;
            if (y < SleeveBottom && y > SleeveBottom - ramp) return RS - (RS - R) * (SleeveBottom - y) / ramp;
            return R;
        }

        /// <summary>A point on (or 'lift' above) the tube's surface, in Unity model space (Blender (x, y, z) → (-x, z, -y)).</summary>
        private static Vector3 Surface(Vector2 yt, float lift)
        {
            float r = SurfaceRadius(yt.x) + lift;
            return new Vector3(-r * Mathf.Cos(yt.y), r * Mathf.Sin(yt.y), -yt.x);
        }

        private static Vector3 Outward(Vector2 yt) => new Vector3(-Mathf.Cos(yt.y), Mathf.Sin(yt.y), 0f);

        private static float Dist(Vector2 a, Vector2 b) => Vector3.Distance(Surface(a, 0f), Surface(b, 0f));

        /// <summary>A round tube of radius r lying on the surface along the samples; UV u = length / TileLength.</summary>
        private static void Tube(MeshData m, List<Vector2> s, float r, bool caps)
        {
            int start = m.V.Count;
            float along = 0f;
            for (int i = 0; i < s.Count; i++)
            {
                if (i > 0) along += Dist(s[i - 1], s[i]);
                Vector3 n = Outward(s[i]);
                Vector3 c = Surface(s[i], r * 1.05f);   // sits on the surface
                Vector3 t = (Surface(s[Mathf.Min(s.Count - 1, i + 1)], 0f) - Surface(s[Mathf.Max(0, i - 1)], 0f)).normalized;
                Vector3 b = Vector3.Cross(t, n).normalized;
                n = Vector3.Cross(b, t).normalized;
                for (int j = 0; j <= Sides; j++)
                {
                    float a = j / (float)Sides * Mathf.PI * 2f;
                    Vector3 d = n * Mathf.Cos(a) + b * Mathf.Sin(a);
                    m.V.Add(c + d * r);
                    m.N.Add(d);
                    m.UV.Add(new Vector2(along / TileLength, j / (float)Sides));
                }
            }
            int ring = Sides + 1;
            for (int i = 0; i + 1 < s.Count; i++)
                for (int j = 0; j < Sides; j++)
                {
                    int a = start + i * ring + j, b2 = a + ring;
                    m.T.Add(a); m.T.Add(a + 1); m.T.Add(b2);
                    m.T.Add(a + 1); m.T.Add(b2 + 1); m.T.Add(b2);
                }
            if (!caps) return;
            // rounded ends so the liquid doesn't look cut off
            Cap(m, s[0], r);
            Cap(m, s[s.Count - 1], r);
        }

        /// <summary>A small ball (an end cap or connector) on the surface.</summary>
        private static void Cap(MeshData m, Vector2 at, float r)
        {
            Vector3 c = Surface(at, r * 1.05f);
            int start = m.V.Count, lat = 6, lon = 10;
            for (int i = 0; i <= lat; i++)
            {
                float phi = i / (float)lat * Mathf.PI;
                for (int j = 0; j <= lon; j++)
                {
                    float th = j / (float)lon * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    m.V.Add(c + d * r); m.N.Add(d); m.UV.Add(new Vector2(j / (float)lon, i / (float)lat));
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = start + i * (lon + 1) + j, b = a + lon + 1;
                    m.T.Add(a); m.T.Add(b); m.T.Add(a + 1);
                    m.T.Add(a + 1); m.T.Add(b); m.T.Add(b + 1);
                    m.T.Add(a); m.T.Add(a + 1); m.T.Add(b);          // both sides (lit metal culls back faces)
                    m.T.Add(a + 1); m.T.Add(b + 1); m.T.Add(b);
                }
        }

        /// <summary>A metal clamp: a short band wrapped over the tube at sample i, its ends flat on the surface.</summary>
        private static void Clamp(MeshData m, List<Vector2> s, int i, float glass)
        {
            Vector3 n = Outward(s[i]);
            Vector3 t = (Surface(s[i + 1], 0f) - Surface(s[i - 1], 0f)).normalized;
            Vector3 b = Vector3.Cross(t, n).normalized;
            Vector3 c = Surface(s[i], glass * 1.05f);
            float r = glass * 1.3f, half = 0.0035f;
            int start = m.V.Count, steps = 8;
            // a half ring over the top of the tube, from one side down to the other
            for (int k = 0; k <= steps; k++)
            {
                float a = Mathf.PI * k / steps;
                Vector3 d = b * Mathf.Cos(a) + n * Mathf.Sin(a);
                Vector3 p = c + d * r;
                if (k == 0 || k == steps) p -= n * glass * 1.05f;   // the feet reach down to the surface
                m.V.Add(p - t * half); m.N.Add(d);
                m.V.Add(p + t * half); m.N.Add(d);
                m.UV.Add(Vector2.zero); m.UV.Add(Vector2.zero);
            }
            for (int k = 0; k < steps; k++)
            {
                int a = start + k * 2;
                m.T.Add(a); m.T.Add(a + 2); m.T.Add(a + 1);
                m.T.Add(a + 1); m.T.Add(a + 2); m.T.Add(a + 3);
                // inside face too (thin band, seen from both sides)
                m.T.Add(a); m.T.Add(a + 1); m.T.Add(a + 2);
                m.T.Add(a + 1); m.T.Add(a + 3); m.T.Add(a + 2);
            }
        }

        private class MeshData
        {
            internal readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
            internal readonly List<Vector2> UV = new List<Vector2>();
            internal readonly List<int> T = new List<int>();

            internal Mesh ToMesh(string name)
            {
                if (V.Count == 0) return null;
                var mesh = new Mesh { name = name };
                if (V.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(V);
                mesh.SetNormals(N);
                mesh.SetUVs(0, UV);
                mesh.SetTriangles(T, 0);
                mesh.RecalculateBounds();
                mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return mesh;
            }
        }
    }
}
