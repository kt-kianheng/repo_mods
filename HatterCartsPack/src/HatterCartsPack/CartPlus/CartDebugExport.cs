using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace HatterCartsPack
{
    /// <summary>
    /// Development aid: writes a cart's visible meshes, in cart space, as an OBJ file to
    /// BepInEx/HatterCartsPack debug/, so the model can be inspected outside the game. Switch off before publishing.
    /// </summary>
    internal static class CartDebugExport
    {
        internal static readonly bool Enabled = true;

        internal static void Export(Transform cart, string fileName)
        {
            if (!Enabled) return;
            try
            {
                string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "HatterCartsPack debug");
                Directory.CreateDirectory(dir);
                var sb = new StringBuilder("# " + cart.name + " in cart space (x width, y up, z length)\n");
                int offset = 1;
                foreach (MeshFilter mf in cart.GetComponentsInChildren<MeshFilter>(true))
                {
                    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || !mr.enabled || mf.sharedMesh == null || InItemVolume(mf.transform, cart)) continue;
                    Mesh mesh = mf.sharedMesh.isReadable ? mf.sharedMesh : CartStretch.ReadableCopy(mf.sharedMesh);
                    if (mesh == null) continue;
                    Matrix4x4 toCart = cart.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    sb.Append("o ").Append(PathOf(cart, mf.transform).Replace(' ', '_')).Append('\n');
                    foreach (Vector3 v in mesh.vertices)
                    {
                        Vector3 p = toCart.MultiplyPoint3x4(v);
                        sb.Append("v ").Append(F(p.x)).Append(' ').Append(F(p.y)).Append(' ').Append(F(p.z)).Append('\n');
                    }
                    int[] tris = mesh.triangles;
                    for (int i = 0; i + 2 < tris.Length; i += 3)
                        sb.Append("f ").Append(tris[i] + offset).Append(' ').Append(tris[i + 1] + offset).Append(' ').Append(tris[i + 2] + offset).Append('\n');
                    offset += mesh.vertexCount;
                }
                string path = Path.Combine(dir, fileName + ".obj");
                File.WriteAllText(path, sb.ToString());
                Plugin.Log.LogInfo($"Debug: wrote {path}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Debug: couldn't export {fileName}: {e.Message}");
            }
        }

        /// <summary>
        /// Writes the cart's solid colliders (boxes; capsules and others as their bounding boxes) in cart space, one
        /// object each, to compare with the visible model.
        /// </summary>
        internal static void ExportColliders(Transform cart, string fileName)
        {
            if (!Enabled) return;
            try
            {
                string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "HatterCartsPack debug");
                Directory.CreateDirectory(dir);
                var sb = new StringBuilder("# " + cart.name + " colliders in cart space\n");
                int offset = 1;
                foreach (Collider c in cart.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger || InItemVolume(c.transform, cart) || !CartStretch.TryLocalBounds(c, out Bounds local)) continue;
                    sb.Append("o ").Append(PathOf(cart, c.transform).Replace(' ', '_')).Append('_').Append(c.GetType().Name).Append('\n');
                    Vector3 e = local.extents;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = local.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                        Vector3 p = cart.InverseTransformPoint(c.transform.TransformPoint(corner));
                        sb.Append("v ").Append(F(p.x)).Append(' ').Append(F(p.y)).Append(' ').Append(F(p.z)).Append('\n');
                    }
                    int[,] faces = { { 0, 1, 3, 2 }, { 4, 6, 7, 5 }, { 0, 4, 5, 1 }, { 2, 3, 7, 6 }, { 0, 2, 6, 4 }, { 1, 5, 7, 3 } };
                    for (int f = 0; f < 6; f++)
                        sb.Append("f ").Append(faces[f, 0] + offset).Append(' ').Append(faces[f, 1] + offset).Append(' ').Append(faces[f, 2] + offset).Append(' ').Append(faces[f, 3] + offset).Append('\n');
                    offset += 8;
                }
                string path = Path.Combine(dir, fileName + ".obj");
                File.WriteAllText(path, sb.ToString());
                Plugin.Log.LogInfo($"Debug: wrote {path}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Debug: couldn't export {fileName}: {e.Message}");
            }
        }

        private static bool InItemVolume(Transform t, Transform cart)
        {
            for (Transform x = t; x != null && x != cart; x = x.parent) if (x.name.StartsWith("Item Volume")) return true;
            return false;
        }

        private static string PathOf(Transform root, Transform t)
        {
            string s = t.name;
            for (Transform x = t.parent; x != null && x != root; x = x.parent) s = x.name + "/" + s;
            return s;
        }

        private static string F(float f) => f.ToString("0.#####", CultureInfo.InvariantCulture);
    }
}
