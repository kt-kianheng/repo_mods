using System.Collections.Generic;
using UnityEngine;

namespace HatterMiscellaneousPack
{
    /// <summary>
    /// A solid 3D arrowhead (a wide notched dart with a raised ridge, no shaft), drawn by its own little camera into a RenderTexture that the HUD shows.
    /// The rig sits far below the level on a layer no other camera draws. Its camera looks at the arrow from slightly
    /// above, the way you'd see a sat-nav arrow, so "straight ahead" points up the screen and "behind you" down it, while
    /// up, down and sideways read in depth. Unlit (vertex colours), shaded each frame from a fixed light so it reads as 3D.
    /// </summary>
    internal class CompassArrow3D : MonoBehaviour
    {
        private const int TextureSize = 160;
        private const float ViewPitch = 35f;   // degrees the arrow camera looks down at the arrow

        private static int layer = -2;
        private static int count;
        private static float nextMaskFix;
        private static readonly List<Camera> ours = new List<Camera>();
        private static Material material;
        private static readonly Vector3 LightDir = new Vector3(-0.45f, 0.8f, -0.4f).normalized;   // from the upper left, towards the viewer

        internal RenderTexture texture;
        private Camera cam;
        private Transform arrow;
        private Mesh mesh;
        private Vector3[] normals;
        private Color[] colors;

        internal static CompassArrow3D Create()
        {
            int l = ArrowLayer();
            var root = new GameObject("HatterMiscellaneousPack Compass Arrow Rig");
            root.transform.position = new Vector3(0f, -5000f - 25f * (count++ % 40), 0f);
            var rig = root.AddComponent<CompassArrow3D>();

            if (material == null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                material = new Material(sh != null ? sh : Shader.Find("Unlit/Color")) { name = "HMP Compass Arrow" };
            }
            var a = new GameObject("Arrow") { layer = l };
            a.transform.SetParent(root.transform, false);
            rig.mesh = ArrowMesh();
            rig.normals = rig.mesh.normals;
            rig.colors = new Color[rig.normals.Length];
            a.AddComponent<MeshFilter>().sharedMesh = rig.mesh;
            var mr = a.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            rig.arrow = a.transform;

            var c = new GameObject("Arrow Camera");
            c.transform.SetParent(root.transform, false);
            c.transform.localRotation = Quaternion.Euler(ViewPitch, 0f, 0f);
            c.transform.localPosition = c.transform.localRotation * new Vector3(0f, 0f, -3f);
            rig.cam = c.AddComponent<Camera>();
            rig.cam.orthographic = true;
            rig.cam.orthographicSize = 0.62f;
            rig.cam.nearClipPlane = 0.5f;
            rig.cam.farClipPlane = 6f;
            rig.cam.clearFlags = CameraClearFlags.SolidColor;
            rig.cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            rig.cam.cullingMask = 1 << l;
            rig.cam.depth = -100f;
            rig.cam.allowHDR = false;
            rig.cam.allowMSAA = true;
            rig.cam.useOcclusionCulling = false;
            rig.texture = new RenderTexture(TextureSize, TextureSize, 16, RenderTextureFormat.ARGB32) { name = "HMP Compass Arrow", antiAliasing = 4 };
            rig.texture.Create();
            rig.cam.targetTexture = rig.texture;
            ours.Add(rig.cam);
            rig.Point(Vector3.forward, Color.white);
            return rig;
        }

        /// <summary>Turns the arrow to <paramref name="dirInView"/> (the player camera's space: x right, y up, z ahead) and shades it.</summary>
        internal void Point(Vector3 dirInView, Color colour)
        {
            if (dirInView.sqrMagnitude < 1e-6f) dirInView = Vector3.forward;
            Vector3 up = Mathf.Abs(Vector3.Dot(dirInView.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
            arrow.localRotation = Quaternion.LookRotation(dirInView, up);
            for (int i = 0; i < normals.Length; i++)
            {
                float k = 0.38f + 0.62f * Mathf.Max(0f, Vector3.Dot(arrow.localRotation * normals[i], LightDir));
                colors[i] = new Color(colour.r * k, colour.g * k, colour.b * k, 1f);
            }
            mesh.colors = colors;
        }

        private void LateUpdate()
        {
            // keep every other camera from drawing our layer (cameras can appear later, e.g. on a new level)
            if (Time.unscaledTime < nextMaskFix) return;
            nextMaskFix = Time.unscaledTime + 0.5f;
            ours.RemoveAll(x => x == null);
            int bit = 1 << ArrowLayer();
            foreach (Camera c in Camera.allCameras)
                if (c != null && !ours.Contains(c) && (c.cullingMask & bit) != 0) c.cullingMask &= ~bit;
        }

        private void OnDestroy()
        {
            ours.Remove(cam);
            if (cam != null) cam.targetTexture = null;
            if (texture != null) { texture.Release(); Destroy(texture); }
            if (mesh != null) Destroy(mesh);
        }

        /// <summary>An unnamed layer (nothing in the game uses it), or the last one if every layer is named.</summary>
        private static int ArrowLayer()
        {
            if (layer >= 0) return layer;
            layer = 31;
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) { layer = i; break; }
            Plugin.Log.LogInfo($"Pocket compass: the 3D arrows use layer {layer}.");
            return layer;
        }

        /// <summary>
        /// A 1-unit arrowhead along +Z, no shaft: a wide dart with a notched back, raised along its middle into a ridge
        /// (top and bottom) so each half catches the light differently, flat-shaded (each face its own vertices).
        /// </summary>
        private static Mesh ArrowMesh()
        {
            Vector3 tip = new Vector3(0f, 0f, 0.55f), right = new Vector3(0.46f, 0f, -0.42f), notch = new Vector3(0f, 0f, -0.16f),
                    left = new Vector3(-0.46f, 0f, -0.42f);
            const float ridge = 0.13f;   // the middle line rises this much above (and below) the wings
            Vector3 up = Vector3.up * ridge;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tris = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                Vector3 nn = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(nn, outward) < 0f) { Vector3 t = b; b = c; c = t; nn = -nn; }
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); n.Add(nn); n.Add(nn); n.Add(nn);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }
            // top: two facets per side, meeting on the raised ridge from the tip to the notch
            Tri(tip + up * 0.35f, right, notch + up, Vector3.up);
            Tri(tip + up * 0.35f, notch + up, left, Vector3.up);
            // bottom, mirrored
            Tri(tip - up * 0.35f, right, notch - up, Vector3.down);
            Tri(tip - up * 0.35f, notch - up, left, Vector3.down);
            // the tip's edge (a thin sliver joining top and bottom at the point)
            Tri(tip + up * 0.35f, tip - up * 0.35f, right, new Vector3(1f, 0f, 1f));
            Tri(tip + up * 0.35f, left, tip - up * 0.35f, new Vector3(-1f, 0f, 1f));
            // the notched back: from each wing in to the ridge's back end
            Tri(right, notch - up, notch + up, new Vector3(0.3f, 0f, -1f));
            Tri(left, notch + up, notch - up, new Vector3(-0.3f, 0f, -1f));
            var m = new Mesh { name = "HMP Compass Arrow" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tris, 0); m.RecalculateBounds();
            return m;
        }
    }
}
