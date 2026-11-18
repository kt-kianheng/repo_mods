using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HatterCartsPack
{
    /// <summary>
    /// Makes a cart bigger without making it thicker: every point of the cart moves away from the body's centre by a
    /// fixed amount per axis (<c>delta</c>), instead of being scaled. Walls, floor and bars keep their thickness while
    /// the cart gets longer, wider and taller.
    ///
    /// Each part is handled per axis (in cart space):
    /// - a part that spans the body on that axis (crosses the centre and is at least half the body's size) is
    ///   stretched: its points on each side of the centre move outwards on that side;
    /// - any other part just moves outwards as a whole, without changing size.
    /// A transform whose whole subtree spans nothing (the screen, the grab point, a handle knob) is moved as one piece.
    ///
    /// The recipe is captured once from the unstretched cart and stores the original values, so <see cref="Apply"/>
    /// can be called with any delta (zero gives the vanilla cart back) on that cart or on any copy of it.
    /// </summary>
    internal class CartStretch
    {
        private const float SpanFraction = 0.5f;   // a part spans an axis when it's at least this much of the body's size
        private const float DeadZone = 0.05f;      // metres: parts this close to the centre plane don't move on that axis

        internal Vector3 Center { get; private set; }  // body centre, cart space
        internal Vector3 Size { get; private set; }    // body size, cart space

        private readonly List<Entry> entries = new List<Entry>();
        private readonly HashSet<string> skipNames;
        private Func<Transform, bool> rigid;           // parts that only move, never stretch

        private CartStretch(HashSet<string> skipNames) { this.skipNames = skipNames; }

        /// <summary>
        /// Captures the recipe from <paramref name="cart"/> (unstretched). <paramref name="body"/> is measured for the
        /// centre and size. Children named in <paramref name="skip"/> (with their subtrees) are left alone.
        /// </summary>
        internal static CartStretch Capture(Transform cart, Vector3 center, Vector3 size, Func<Transform, bool> rigid = null)
        {
            var recipe = new CartStretch(new HashSet<string>()) { Center = center, Size = size, rigid = rigid };
            foreach (Transform child in cart) recipe.Visit(cart, child);
            Plugin.Log.LogInfo($"Cart stretch: centre {center}, body size {size}, {recipe.entries.Count} parts.");
            return recipe;
        }

        /// <summary>
        /// Stretches <paramref name="cart"/> (the captured cart or a copy of it) by <paramref name="delta"/> metres per
        /// side on each axis. The first call on a cart gives its meshes their own copies.
        /// </summary>
        internal void Apply(Transform cart, Vector3 delta, HashSet<Mesh> ownMeshes)
        {
            foreach (Entry e in entries)
            {
                Transform t = Find(cart, e.Path);
                if (t == null) continue;
                try { e.Apply(cart, t, delta, ownMeshes); }
                catch (Exception ex) { Plugin.Log.LogWarning($"Cart stretch: '{e.Path}' failed: {ex.Message}"); }
            }
        }

        /// <summary>The delta that makes the body <paramref name="factor"/> times its size on each axis (x width, y height, z length).</summary>
        internal Vector3 DeltaForFactor(Vector3 factor) => Vector3.Scale(Size, factor - Vector3.one) * 0.5f;

        // ---- Capture ------------------------------------------------------------------------------------

        private void Visit(Transform cart, Transform t)
        {
            if (skipNames.Contains(t.name) || t.name.StartsWith("Item Volume")) return;
            string path = PathOf(cart, t);

            if (t.name == "In Cart" && t.GetComponent<BoxCollider>() != null)
            {
                // The game measures the haul from this transform's own position and scale, so stretch the transform.
                entries.Add(new TransformBoxEntry(path, t, CartBounds(cart, t.GetComponent<BoxCollider>()), this));
                return;
            }

            Bounds subtree;
            if (!TrySubtreeBounds(cart, t, out subtree)) subtree = new Bounds(cart.InverseTransformPoint(t.position), Vector3.zero);
            if (!SpansAny(subtree) || (rigid != null && rigid(t)))
            {
                entries.Add(new MoveEntry(path, t, Shift(subtree.center)));
                return;
            }

            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) entries.Add(new MeshEntry(path, cart, mf, this));
            foreach (BoxCollider box in t.GetComponents<BoxCollider>()) entries.Add(new BoxEntry(path, cart, box, this));
            foreach (CapsuleCollider cap in t.GetComponents<CapsuleCollider>()) entries.Add(new CapsuleEntry(path, cart, cap, this));
            foreach (Transform child in t) Visit(cart, child);
        }

        private bool Spans(Bounds b, int axis)
        {
            return b.min[axis] < Center[axis] - DeadZone && b.max[axis] > Center[axis] + DeadZone
                   && b.size[axis] >= Size[axis] * SpanFraction;
        }

        private bool SpansAny(Bounds b) => Spans(b, 0) || Spans(b, 1) || Spans(b, 2);

        /// <summary>Direction (-1/0/+1 per axis) a non-spanning part at <paramref name="p"/> moves in.</summary>
        private Vector3 Shift(Vector3 p)
        {
            Vector3 s = Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                float d = p[i] - Center[i];
                s[i] = Mathf.Abs(d) < DeadZone ? 0f : Mathf.Sign(d);
            }
            return s;
        }

        /// <summary>Per axis: 1 where the part spans (stretches), 0 where it moves as a whole; plus that move direction.</summary>
        private void Classify(Bounds b, out Vector3 spans, out Vector3 shift)
        {
            spans = Vector3.zero;
            shift = Shift(b.center);
            for (int i = 0; i < 3; i++)
                if (Spans(b, i)) { spans[i] = 1f; shift[i] = 0f; }
        }

        /// <summary>The cart-space offset of a point: stretched axes push it out on its own side, the others use the part's shift.</summary>
        private Vector3 PointOffset(Vector3 p, Vector3 spans, Vector3 shift, Vector3 delta)
        {
            Vector3 o = Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                float dir = shift[i];
                if (spans[i] > 0f)
                {
                    float d = p[i] - Center[i];
                    dir = Mathf.Abs(d) < 0.001f ? 0f : Mathf.Sign(d);
                }
                o[i] = dir * delta[i];
            }
            return o;
        }

        // ---- Measuring ----------------------------------------------------------------------------------

        private static bool TrySubtreeBounds(Transform cart, Transform root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Encapsulate(ref bounds, ref any, TransformBounds(mf.sharedMesh.bounds, mf.transform, cart));
            }
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
            {
                if (TryLocalBounds(c, out Bounds local)) Encapsulate(ref bounds, ref any, TransformBounds(local, c.transform, cart));
            }
            return any;
        }

        private static void Encapsulate(ref Bounds b, ref bool any, Bounds add)
        {
            if (!any) { b = add; any = true; } else b.Encapsulate(add);
        }

        internal static bool TryLocalBounds(Collider c, out Bounds local)
        {
            switch (c)
            {
                case BoxCollider box: local = new Bounds(box.center, box.size); return true;
                case CapsuleCollider cap:
                    Vector3 size = Vector3.one * (cap.radius * 2f);
                    size[cap.direction] = Mathf.Max(cap.height, cap.radius * 2f);
                    local = new Bounds(cap.center, size); return true;
                case SphereCollider sph: local = new Bounds(sph.center, Vector3.one * sph.radius * 2f); return true;
                case MeshCollider mc when mc.sharedMesh != null: local = mc.sharedMesh.bounds; return true;
            }
            local = default;
            return false;
        }

        internal static Bounds CartBounds(Transform cart, BoxCollider box) => TransformBounds(new Bounds(box.center, box.size), box.transform, cart);

        internal static Bounds TransformBounds(Bounds local, Transform from, Transform to)
        {
            Vector3 c = local.center, e = local.extents;
            Bounds result = default;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = to.InverseTransformPoint(from.TransformPoint(corner));
                if (i == 0) result = new Bounds(p, Vector3.zero);
                else result.Encapsulate(p);
            }
            return result;
        }

        /// <summary>For each local axis of <paramref name="t"/>: the cart axis it lies along, and cart metres per local unit.</summary>
        private static void AxisMap(Transform cart, Transform t, out int[] cartAxis, out float[] unit)
        {
            cartAxis = new int[3];
            unit = new float[3];
            for (int i = 0; i < 3; i++)
            {
                Vector3 local = Vector3.zero; local[i] = 1f;
                Vector3 v = cart.InverseTransformVector(t.TransformVector(local));
                Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
                cartAxis[i] = a.x >= a.y && a.x >= a.z ? 0 : (a.y >= a.z ? 1 : 2);
                unit[i] = Mathf.Max(v.magnitude, 1e-6f);
            }
        }

        // ---- Paths --------------------------------------------------------------------------------------

        // Paths are names, with "#k" for the k-th sibling of the same name: the game removes some of a cart's parts
        // once it spawns (e.g. its shop-placement helper), so sibling indices shift and can't be used.
        private static string PathOf(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (Transform x = t; x != null && x != root; x = x.parent)
            {
                int k = 0;
                if (x.parent != null)
                    for (int i = 0; i < x.GetSiblingIndex(); i++)
                        if (x.parent.GetChild(i).name == x.name) k++;
                parts.Add(x.name + "#" + k);
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static Transform Find(Transform root, string path)
        {
            Transform t = root;
            foreach (string segment in path.Split('/'))
            {
                int hash = segment.LastIndexOf('#');
                string name = segment.Substring(0, hash);
                int k = int.Parse(segment.Substring(hash + 1));
                Transform next = null;
                foreach (Transform child in t)
                {
                    if (child.name != name) continue;
                    if (k-- == 0) { next = child; break; }
                }
                if (next == null) return null;
                t = next;
            }
            return t;
        }

        // ---- Entries ------------------------------------------------------------------------------------

        private abstract class Entry
        {
            internal readonly string Path;
            protected Entry(string path) { Path = path; }
            internal abstract void Apply(Transform cart, Transform t, Vector3 delta, HashSet<Mesh> ownMeshes);
        }

        /// <summary>A part moved as a whole (keeps its size).</summary>
        private class MoveEntry : Entry
        {
            private readonly Vector3 localPosition, shift;
            internal MoveEntry(string path, Transform t, Vector3 shift) : base(path) { localPosition = t.localPosition; this.shift = shift; }

            internal override void Apply(Transform cart, Transform t, Vector3 delta, HashSet<Mesh> ownMeshes)
            {
                Vector3 offsetCart = Vector3.Scale(shift, delta);
                Vector3 offsetParent = t.parent != null ? t.parent.InverseTransformVector(cart.TransformVector(offsetCart)) : offsetCart;
                t.localPosition = localPosition + offsetParent;
            }
        }

        /// <summary>A box measured by its transform (the "In Cart" haul volume): its position and scale are stretched.</summary>
        private class TransformBoxEntry : Entry
        {
            private readonly Vector3 localPosition, localScale, spans, shift;
            private readonly Bounds bounds;
            private readonly CartStretch recipe;

            internal TransformBoxEntry(string path, Transform t, Bounds cartBounds, CartStretch recipe) : base(path)
            {
                localPosition = t.localPosition; localScale = t.localScale; bounds = cartBounds; this.recipe = recipe;
                Vector3 sp, sh; recipe.Classify(cartBounds, out sp, out sh); spans = sp; shift = sh;
            }

            internal override void Apply(Transform cart, Transform t, Vector3 delta, HashSet<Mesh> ownMeshes)
            {
                t.localPosition = localPosition;
                t.localScale = localScale;
                AxisMap(cart, t, out int[] cartAxis, out float[] _);
                Vector3 scale = localScale;
                for (int i = 0; i < 3; i++)
                {
                    int j = cartAxis[i];
                    if (spans[j] > 0f && bounds.size[j] > 1e-4f) scale[i] *= (bounds.size[j] + 2f * delta[j]) / bounds.size[j];
                }
                t.localScale = scale;
                Vector3 offsetCart = Vector3.Scale(shift, delta);
                Vector3 offsetParent = t.parent != null ? t.parent.InverseTransformVector(cart.TransformVector(offsetCart)) : offsetCart;
                t.localPosition = localPosition + offsetParent;
            }
        }

        private class BoxEntry : Entry
        {
            private readonly int index;
            private readonly Vector3 center, size, spans, shift;
            internal BoxEntry(string path, Transform cart, BoxCollider box, CartStretch recipe) : base(path)
            {
                index = Array.IndexOf(box.GetComponents<BoxCollider>(), box);
                center = box.center; size = box.size;
                Vector3 sp, sh; recipe.Classify(CartBounds(cart, box), out sp, out sh); spans = sp; shift = sh;
            }

            internal override void Apply(Transform cart, Transform t, Vector3 delta, HashSet<Mesh> ownMeshes)
            {
                BoxCollider[] boxes = t.GetComponents<BoxCollider>();
                if (index < 0 || index >= boxes.Length) return;
                BoxCollider box = boxes[index];
                AxisMap(cart, t, out int[] cartAxis, out float[] unit);
                Vector3 s = size;
                for (int i = 0; i < 3; i++)
                {
                    int j = cartAxis[i];
                    if (spans[j] > 0f) s[i] = Mathf.Abs(size[i]) + 2f * delta[j] / unit[i];
                }
                box.size = s;
                box.center = center + t.InverseTransformVector(cart.TransformVector(Vector3.Scale(shift, delta)));
            }
        }

        private class CapsuleEntry : Entry
        {
            private readonly int index;
            private readonly Vector3 center, spans, shift;
            private readonly float height;
            internal CapsuleEntry(string path, Transform cart, CapsuleCollider cap, CartStretch recipe) : base(path)
            {
                index = Array.IndexOf(cap.GetComponents<CapsuleCollider>(), cap);
                center = cap.center; height = cap.height;
                TryLocalBounds(cap, out Bounds local);
                Bounds b = TransformBounds(local, cap.transform, cart);
                Vector3 sp, sh; recipe.Classify(b, out sp, out sh);
                // A capsule can only stretch along its length. Across it, it moves out as a whole, by where its centre
                // is: the cart's side capsules (radius 0.5) are what stop it at walls, so they must reach the new sides.
                AxisMap(cart, cap.transform, out int[] cartAxis, out float[] _);
                int along = cartAxis[cap.direction];
                Vector3 centreShift = recipe.Shift(b.center);
                for (int j = 0; j < 3; j++)
                    if (j != along) { sp[j] = 0f; sh[j] = centreShift[j]; }
                spans = sp; shift = sh;
            }

            internal override void Apply(Transform cart, Transform t, Vector3 delta, HashSet<Mesh> ownMeshes)
            {
                CapsuleCollider[] caps = t.GetComponents<CapsuleCollider>();
                if (index < 0 || index >= caps.Length) return;
                CapsuleCollider cap = caps[index];
                AxisMap(cart, t, out int[] cartAxis, out float[] unit);
                int j = cartAxis[cap.direction];
                cap.height = spans[j] > 0f ? height + 2f * delta[j] / unit[cap.direction] : height;
                cap.center = center + t.InverseTransformVector(cart.TransformVector(Vector3.Scale(shift, delta)));
            }
        }

        /// <summary>A mesh whose vertices are stretched. Each cart gets its own copy of the mesh on the first Apply.</summary>
        private class MeshEntry : Entry
        {
            private readonly Vector3[] vertices;       // original, mesh space
            private readonly Vector3[] cartPoints;     // original, cart space
            private readonly Matrix4x4 fromCart;       // cart space -> mesh space, in the part's original pose
            private readonly Vector3 spans, shift;
            private readonly Mesh template;            // readable copy of the original mesh
            private readonly CartStretch recipe;

            internal MeshEntry(string path, Transform cart, MeshFilter mf, CartStretch recipe) : base(path)
            {
                this.recipe = recipe;
                template = ReadableCopy(mf.sharedMesh);
                Bounds b = TransformBounds(mf.sharedMesh.bounds, mf.transform, cart);
                Vector3 sp, sh; recipe.Classify(b, out sp, out sh); spans = sp; shift = sh;
                if (template == null) return;
                vertices = template.vertices;
                cartPoints = new Vector3[vertices.Length];
                Matrix4x4 toCart = cart.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                fromCart = toCart.inverse;
                for (int i = 0; i < vertices.Length; i++) cartPoints[i] = toCart.MultiplyPoint3x4(vertices[i]);
            }

            internal override void Apply(Transform cart, Transform t, Vector3 delta, HashSet<Mesh> ownMeshes)
            {
                MeshFilter mf = t.GetComponent<MeshFilter>();
                if (mf == null) return;
                if (template == null)
                {
                    // Unreadable mesh: fall back to moving it as a whole (no stretch).
                    return;
                }
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || !ownMeshes.Contains(mesh))
                {
                    // This cart's own copy, so stretching one cart never changes another (or the prefab).
                    mesh = Object.Instantiate(template);
                    mesh.name = template.name.Replace(" (Readable)", "") + " (Stretched)";
                    mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    mf.sharedMesh = mesh;
                    ownMeshes.Add(mesh);
                }
                // Converted with the part's original pose, not its current one: the game animates some parts
                // (the hover effect under the cart is rescaled as it moves), and the stretch must not follow that.
                var moved = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                    moved[i] = vertices[i] + fromCart.MultiplyVector(recipe.PointOffset(cartPoints[i], spans, shift, delta));
                mesh.vertices = moved;
                mesh.RecalculateBounds();
            }
        }

        // ---- Mesh copies ----------------------------------------------------------------------------------

        private static readonly Dictionary<Mesh, Mesh> readableCopies = new Dictionary<Mesh, Mesh>();

        /// <summary>
        /// A CPU-readable copy of <paramref name="src"/>. Game meshes are usually not readable, so their vertex and index
        /// buffers are read back from the GPU. Returns null (and logs) if that isn't possible.
        /// </summary>
        internal static Mesh ReadableCopy(Mesh src)
        {
            if (readableCopies.TryGetValue(src, out Mesh cached)) return cached;
            Mesh copy = null;
            try
            {
                if (src.isReadable)
                {
                    copy = Object.Instantiate(src);
                }
                else
                {
                    copy = new Mesh();
                    copy.indexFormat = src.indexFormat;
                    copy.SetVertexBufferParams(src.vertexCount, src.GetVertexAttributes());
                    const MeshUpdateFlags quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
                    for (int s = 0; s < src.vertexBufferCount; s++)
                    {
                        using (GraphicsBuffer vb = src.GetVertexBuffer(s))
                        {
                            byte[] data = new byte[vb.count * vb.stride];
                            vb.GetData(data);
                            copy.SetVertexBufferData(data, 0, 0, data.Length, s, quiet);
                        }
                    }
                    using (GraphicsBuffer ib = src.GetIndexBuffer())
                    {
                        byte[] data = new byte[ib.count * ib.stride];
                        ib.GetData(data);
                        copy.SetIndexBufferParams(ib.count, src.indexFormat);
                        copy.SetIndexBufferData(data, 0, 0, data.Length, quiet);
                    }
                    copy.subMeshCount = src.subMeshCount;
                    for (int i = 0; i < src.subMeshCount; i++) copy.SetSubMesh(i, src.GetSubMesh(i), quiet);
                    copy.bounds = src.bounds;
                }
                copy.name = src.name + " (Readable)";
                copy.hideFlags = HideFlags.DontUnloadUnusedAsset;
                if (copy.vertexCount != src.vertexCount || copy.vertices.Length != src.vertexCount)
                    throw new InvalidOperationException($"read {copy.vertices.Length} of {src.vertexCount} vertices");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Cart stretch: couldn't read mesh '{src.name}' ({e.Message}); it keeps its shape.");
                copy = null;
            }
            readableCopies[src] = copy;
            return copy;
        }
    }
}
