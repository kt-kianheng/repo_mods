using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HatterCartsPack
{
    /// <summary>
    /// Builds the C.A.R.T. Plus prefab at startup:
    ///   1. clones the vanilla C.A.R.T. (keeps handling, haul screen, physics, networking),
    ///   2. adds the stasis button: a copy of the handle bar (mesh and grabbable collider) at the far end of the cart,
    ///   3. makes it 1.4x as wide and long and 1.1x as tall without making it thicker (<see cref="CartStretch"/>),
    ///   4. gives it its own Item definition and price.
    /// The prefab lives under an inactive, DontDestroyOnLoad holder so it never wakes up itself.
    /// </summary>
    internal static class CartPlusBuilder
    {
        internal const string PrefabName = "Item Cart Plus";
        internal const string DisplayName = "C.A.R.T. Plus";
        private const string CartPath = "Items/Item Cart Medium";

        // ---- Fixed tuning (not configurable) ------------------------------------------------------
        internal static readonly Vector3 SizeMultiplier = new Vector3(1.4f, 1.1f, 1.4f);   // width, height, length compared with the vanilla C.A.R.T.
        internal const float PriceMultiplier = 1.5f;     // shop price relative to the vanilla C.A.R.T.

        /// <summary>The stretch recipe, captured from the unstretched cart (step 2 shrinks carts back with it).</summary>
        internal static CartStretch Stretch { get; private set; }
        internal static Vector3 FullDelta { get; private set; }
        /// <summary>The basket ("In Cart" box), cart space, at C.A.R.T. size and at full size (contents are mapped between them).</summary>
        internal static Bounds BasketSmall { get; private set; }
        internal static Bounds BasketFull { get; private set; }
        /// <summary>Cart space, full size: the box around the cart's solid box colliders (walls, floor, handle and button
        /// bars; not the capsules, which reach the ground). Before growing back, the cart checks this space is clear.</summary>
        internal static Bounds RoomFull { get; private set; }

        private static GameObject holder;

        internal static GameObject Build()
        {
            GameObject source = Resources.Load<GameObject>(CartPath);
            if (source == null)
            {
                Plugin.Log.LogError($"Vanilla C.A.R.T. not found at '{CartPath}'. Has the game changed its resource paths?");
                return null;
            }
            ItemAttributes srcAttributes = source.GetComponent<ItemAttributes>();
            if (srcAttributes == null || srcAttributes.item == null || source.GetComponent<PhysGrabCart>() == null)
            {
                Plugin.Log.LogError("Vanilla C.A.R.T. is missing expected components (ItemAttributes / PhysGrabCart).");
                return null;
            }

            holder = new GameObject("HatterCartsPack.CartPlus.PrefabHolder");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            holder.hideFlags = HideFlags.HideAndDontSave;

            GameObject cart = Object.Instantiate(source, holder.transform);
            cart.name = PrefabName;
            Transform cartT = cart.transform;
            LogParts(cart);

            SetupItem(cart, srcAttributes.item);

            if (!TryGetBodyBounds(cartT, out Bounds body))
            {
                Plugin.Log.LogError("Couldn't measure the C.A.R.T.'s body; the C.A.R.T. Plus will not be available.");
                return null;
            }
            // Centre: the basket's middle across and along the cart (the handle and screen sit on its centre line;
            // the body mesh is a little off it), and the body's middle in height.
            Vector3 center = body.center;
            PhysGrabInCart inCart = cart.GetComponentInChildren<PhysGrabInCart>(true);
            BoxCollider basket = inCart != null ? inCart.GetComponent<BoxCollider>() : null;
            if (basket != null)
            {
                Bounds b = CartStretch.CartBounds(cartT, basket);
                center.x = b.center.x;
                center.z = b.center.z;
            }
            CartDebugExport.Export(cartT, "cart_vanilla");
            AddButton(cartT, center);

            // The button's bar, knobs and collider keep their size (the rest of the cart stretches).
            Stretch = CartStretch.Capture(cartT, center, body.size,
                t => t.name == ButtonBarName || t.name == "Cart Handle (Stasis Button)" || t.GetComponent<CartPlusButton>() != null);
            FullDelta = Stretch.DeltaForFactor(SizeMultiplier);
            if (basket != null) BasketSmall = CartStretch.CartBounds(cartT, basket);
            Stretch.Apply(cartT, FullDelta, new HashSet<Mesh>());
            if (basket != null) BasketFull = CartStretch.CartBounds(cartT, basket);
            bool anyBox = false;
            Bounds room = default;
            foreach (BoxCollider bc in cartT.GetComponentsInChildren<BoxCollider>(true))
            {
                if (bc.isTrigger) continue;
                Bounds b = CartStretch.CartBounds(cartT, bc);
                if (!anyBox) { room = b; anyBox = true; } else room.Encapsulate(b);
            }
            RoomFull = room;
            Plugin.Log.LogInfo($"Room needed to grow back: {RoomFull.size} around {RoomFull.center} (cart space).");
            Plugin.Log.LogInfo($"Basket: {BasketSmall.size} at C.A.R.T. size, {BasketFull.size} at full size.");

            CartDebugExport.Export(cartT, "cart_plus");
            CartDebugExport.ExportColliders(cartT, "cart_plus_colliders");
            cart.AddComponent<CartPlus>();
            Plugin.Log.LogInfo($"Built {DisplayName} prefab ({SizeMultiplier} x the C.A.R.T.'s width, height, length; walls {FullDelta} further out).");
            return cart;
        }

        private static void SetupItem(GameObject cart, Item source)
        {
            Item item = Object.Instantiate(source);
            item.name = PrefabName;               // StatsManager / save key: must be unique
            item.itemName = DisplayName;
            item.itemNameLocalized = null;        // force the game to fall back to itemName
            item.description = "A wider, longer C.A.R.T. The button on its far end shrinks it, and everything in it, to fit through tight doors.";

            if (source.value != null)
            {
                Value price = Object.Instantiate(source.value);
                price.name = PrefabName + " Value";
                price.valueMin *= PriceMultiplier;
                price.valueMax *= PriceMultiplier;
                item.value = price;
            }
            cart.GetComponent<ItemAttributes>().item = item;
        }

        /// <summary>The cart body ("Cart Mesh"), in cart space: the stretch is centred on it.</summary>
        private static bool TryGetBodyBounds(Transform cartT, out Bounds body)
        {
            body = default;
            Transform mesh = cartT.Find("Cart Mesh");
            MeshFilter mf = mesh != null ? mesh.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return false;
            body = CartStretch.TransformBounds(mf.sharedMesh.bounds, mf.transform, cartT);
            return body.size.x > 0.1f && body.size.y > 0.1f && body.size.z > 0.1f;
        }

        internal const string MeterName = "Stasis Meter";
        internal const string ButtonBarName = "Cart Handle Bar (Stasis Button)";
        internal const string IconMaterialName = "Stasis Button Shrink Icon";
        private const float ButtonBarScale = 0.7f;   // the button's bar is this wide compared with the C.A.R.T.'s handle, and doesn't stretch
        private const float BarMinY = 0.28f;         // body triangles above this (at the handle end) are the bar and its plate
        internal const float MeterWidth = 0.6f;

        /// <summary>
        /// Builds the stasis button at the opposite end of the cart, turned round to face outwards: a copy of the whole
        /// handle (the bar "Cart Handle" with its knobs, the bar's grabbable collider, the frame that holds it, which
        /// is part of the body mesh, and any separate pieces of the body at the handle end), plus a recharge meter on
        /// the far wall under it. The copy isn't a grab area, so grabbing it holds the cart like a wall;
        /// CartPlus watches for grabs on it.
        /// </summary>
        private static void AddButton(Transform cartT, Vector3 center)
        {
            Transform handleMesh = cartT.Find("Cart Handle");
            Transform grabPoint = cartT.Find("Cart Grab Point");
            Transform handleCollider = null;
            float best = float.MaxValue;
            if (grabPoint != null)
            {
                foreach (Transform child in cartT)
                {
                    if (child.name != "Cart Wall Collider") continue;
                    BoxCollider box = child.GetComponentInChildren<BoxCollider>(true);
                    if (box == null) continue;
                    float d = Vector3.Distance(CartStretch.CartBounds(cartT, box).center, grabPoint.localPosition);
                    if (d < best) { best = d; handleCollider = child; }
                }
            }
            if (handleMesh == null || handleCollider == null)
            {
                Plugin.Log.LogError($"The C.A.R.T.'s handle wasn't found (mesh: {handleMesh != null}, collider: {handleCollider != null}); the C.A.R.T. Plus will have no stasis button.");
                return;
            }

            // The body's far end; the handle end's wall face is its mirror image (the handle and its frame stick out
            // behind that). The cart's wall pieces have no meshes of their own, so the body mesh is measured.
            Transform body = cartT.Find("Cart Mesh");
            MeshFilter bodyMesh = body != null ? body.GetComponent<MeshFilter>() : null;
            float frontOuter = bodyMesh != null && bodyMesh.sharedMesh != null
                ? CartStretch.TransformBounds(bodyMesh.sharedMesh.bounds, body, cartT).max.z : center.z + 0.97f;
            float backCut = 2f * center.z - frontOuter - 0.005f;

            var copied = new System.Collections.Generic.List<string>();
            TurnedCopy(handleMesh, cartT, center).name = "Cart Handle (Stasis Button)";
            copied.Add(handleMesh.name);
            // Same name as the original: PhysGrabCart and the game set up the cart's wall colliders by name.
            // Its mesh only marks the collider's shape (hidden on the original); the copy showed it, so it goes.
            GameObject collider = TurnedCopy(handleCollider, cartT, center);
            collider.name = handleCollider.name;
            Object.DestroyImmediate(collider.GetComponent<MeshRenderer>());
            collider.AddComponent<CartPlusButton>();
            copied.Add("handle collider");
            // A shorter bar: its collider comes in with it. The knobs (two arrow lights and the icon) already fit on it
            // where they are; pulled in, the arrows would overlap the icon.
            Vector3 cs = collider.transform.localScale;
            collider.transform.localScale = new Vector3(cs.x * ButtonBarScale, cs.y, cs.z);
            Transform knobs = cartT.Find("Cart Handle (Stasis Button)");
            if (knobs != null) SetShrinkIcon(knobs);

            // The handle end's other collision boxes (around the screen holder and bumper) have no partner at the far
            // end, so the copied frame there would pass through walls: copy each one that has no mirror image.
            var handleEnd = new System.Collections.Generic.List<Transform>();
            var allBounds = new System.Collections.Generic.List<Bounds>();
            foreach (Transform child in cartT)
            {
                if (child.name != "Cart Wall Collider" || child == handleCollider || child == collider.transform) continue;
                BoxCollider box = child.GetComponentInChildren<BoxCollider>(true);
                if (box == null) continue;
                Bounds b = CartStretch.CartBounds(cartT, box);
                allBounds.Add(b);
                if (b.center.z < center.z - 0.3f) handleEnd.Add(child);
            }
            foreach (Transform piece in handleEnd)
            {
                Bounds b = CartStretch.CartBounds(cartT, piece.GetComponentInChildren<BoxCollider>(true));
                Vector3 mirror = new Vector3(2f * center.x - b.center.x, b.center.y, 2f * center.z - b.center.z);
                bool partnered = allBounds.Exists(o => Vector3.Distance(o.center, mirror) < 0.08f && Vector3.Distance(o.size, b.size) < 0.08f);
                if (partnered) continue;
                GameObject copy = TurnedCopy(piece, cartT, center);
                copy.name = piece.name;
                Object.DestroyImmediate(copy.GetComponent<MeshRenderer>());
                copied.Add($"collider {b.size.x:F2}x{b.size.y:F2}x{b.size.z:F2} at z {b.center.z:F2}");
            }

            if (body != null)
            {
                if (CopyHandleFrame(body, cartT, center, backCut)) copied.Add("handle frame");
                // Separate pieces of the body that lie wholly at the handle end.
                foreach (Transform piece in body)
                {
                    MeshFilter mf = piece.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    if (CartStretch.TransformBounds(mf.sharedMesh.bounds, piece, cartT).max.z >= backCut) continue;
                    TurnedCopy(piece, cartT, center).name = piece.name + " (Stasis Button)";
                    copied.Add(piece.name);
                }
            }

            // The meter fills the copied frame's screen holder: where the haul screen sits at the handle end, turned round.
            if (TryGetScreenBounds(cartT, out Bounds screen))
            {
                Vector3 c = screen.center;
                AddMeter(cartT, new Vector3(2f * center.x - c.x, c.y, 2f * center.z - c.z), screen.size);
            }
            else Plugin.Log.LogWarning("Stasis button: the haul screen wasn't found; no recharge meter.");
            Plugin.Log.LogInfo($"Stasis button: copied {string.Join(", ", copied)} to the far end (frame cut at z={backCut:F2}, far end z={frontOuter:F2}).");
        }

        /// <summary>Cart-space bounds of the haul screen's display ("Screen/Display Wide", else everything under "Screen").</summary>
        private static bool TryGetScreenBounds(Transform cartT, out Bounds bounds)
        {
            bounds = default;
            Transform screen = cartT.Find("Screen/Display Wide") ?? cartT.Find("Screen");
            if (screen == null) return false;
            MeshFilter mf = screen.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                bounds = CartStretch.TransformBounds(mf.sharedMesh.bounds, screen, cartT);
                return true;
            }
            bool any = false;
            foreach (MeshFilter m in screen.GetComponentsInChildren<MeshFilter>(true))
            {
                if (m.sharedMesh == null) continue;
                Bounds b = CartStretch.TransformBounds(m.sharedMesh.bounds, m.transform, cartT);
                if (!any) { bounds = b; any = true; } else bounds.Encapsulate(b);
            }
            return any;
        }

        /// <summary>
        /// The handle's frame is part of the cart body mesh. Copies, into a new mesh turned round to the far end:
        /// the body's separate pieces (connected groups of triangles) that stick out behind the handle-end wall (past
        /// <paramref name="zCut"/>) and lie wholly at that end (the slanted plate behind the knobs, with the bar), and
        /// the body shell's triangles wholly behind the wall (the struts, the screen holder and the bumper).
        /// The wall itself (wide and tall) is left out. Checked against the exported model: 414 triangles.
        /// </summary>
        private static bool CopyHandleFrame(Transform body, Transform cartT, Vector3 c, float zCut)
        {
            MeshFilter mf = body.GetComponent<MeshFilter>();
            MeshRenderer mr = body.GetComponent<MeshRenderer>();
            Mesh src = mf != null && mf.sharedMesh != null ? CartStretch.ReadableCopy(mf.sharedMesh) : null;
            if (src == null || mr == null) { Plugin.Log.LogWarning("Stasis button: couldn't read the body mesh; the far handle has no frame."); return false; }

            Matrix4x4 toCart = cartT.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Vector3[] v = src.vertices;
            Vector3[] n = src.normals;
            Vector2[] uv = src.uv;
            var cartV = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) cartV[i] = toCart.MultiplyPoint3x4(v[i]);

            // Pieces: vertices joined by triangles, and vertices at the same spot (meshes are split at UV seams).
            var parent = new int[v.Length];
            for (int i = 0; i < v.Length; i++) parent[i] = i;
            int Root(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Join(int a, int b) { a = Root(a); b = Root(b); if (a != b) parent[a] = b; }
            var spot = new System.Collections.Generic.Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(cartV[i].x * 1000f), Mathf.RoundToInt(cartV[i].y * 1000f), Mathf.RoundToInt(cartV[i].z * 1000f));
                if (spot.TryGetValue(key, out int other)) Join(i, other); else spot[key] = i;
            }
            var allTris = new int[src.subMeshCount][];
            for (int s = 0; s < src.subMeshCount; s++)
            {
                allTris[s] = src.GetTriangles(s);
                for (int t = 0; t + 2 < allTris[s].Length; t += 3) { Join(allTris[s][t], allTris[s][t + 1]); Join(allTris[s][t], allTris[s][t + 2]); }
            }
            var pieceBounds = new System.Collections.Generic.Dictionary<int, Bounds>();
            for (int i = 0; i < v.Length; i++)
            {
                int r = Root(i);
                if (pieceBounds.TryGetValue(r, out Bounds b)) { b.Encapsulate(cartV[i]); pieceBounds[r] = b; }
                else pieceBounds[r] = new Bounds(cartV[i], Vector3.zero);
            }
            const float endDepth = 0.17f;   // a piece must lie wholly within this far in from the wall face
            var keep = new System.Collections.Generic.HashSet<int>();
            foreach (var kv in pieceBounds)
            {
                Bounds b = kv.Value;
                bool sticksOut = b.min.z < zCut - 0.01f;
                bool atEnd = b.max.z < zCut + endDepth;
                bool isWall = b.size.x > 1.0f && b.size.y > 0.5f;
                if (sticksOut && atEnd && !isWall) keep.Add(kv.Key);
            }

            // Split: the bar with its plate (above the struts) is narrowed and kept rigid; the rest is the frame.
            var barTris = new System.Collections.Generic.List<int>[src.subMeshCount];
            var frameTris = new System.Collections.Generic.List<int>[src.subMeshCount];
            int triCount = 0;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                barTris[s] = new System.Collections.Generic.List<int>();
                frameTris[s] = new System.Collections.Generic.List<int>();
                int[] tris = allTris[s];
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    // Whole pieces at the handle end (the plate behind the knobs, with the bar), plus the triangles of the
                    // body shell that lie wholly behind the wall (the struts, the screen holder and the bumper are joined to it).
                    bool behind = cartV[tris[t]].z < zCut && cartV[tris[t + 1]].z < zCut && cartV[tris[t + 2]].z < zCut;
                    if (!behind && !keep.Contains(Root(tris[t]))) continue;
                    bool high = cartV[tris[t]].y > BarMinY && cartV[tris[t + 1]].y > BarMinY && cartV[tris[t + 2]].y > BarMinY;
                    var target = high ? barTris[s] : frameTris[s];
                    target.Add(tris[t]); target.Add(tris[t + 1]); target.Add(tris[t + 2]);
                    triCount++;
                }
            }
            if (triCount == 0) { Plugin.Log.LogInfo("Stasis button: no body pieces stick out behind the handle-end wall; no frame copied."); return false; }

            MakeTurnedMesh(ButtonBarName, barTris, src, cartV, n, uv, toCart, c, ButtonBarScale, body, mr, cartT);
            MakeTurnedMesh("Cart Handle Frame (Stasis Button)", frameTris, src, cartV, n, uv, toCart, c, 1f, body, mr, cartT);
            Plugin.Log.LogInfo($"Stasis button: handle frame = {keep.Count} of the body's {pieceBounds.Count} pieces, {triCount} triangles (sticking out past z={zCut:F2}); bar above y={BarMinY:F2} narrowed to {ButtonBarScale:P0}.");
            return true;
        }

        /// <summary>
        /// A new mesh object from some of the body's triangles, turned 180 degrees about the centre line and squeezed
        /// across by <paramref name="widthScale"/> (about the centre line); same materials as the body.
        /// </summary>
        private static void MakeTurnedMesh(string name, System.Collections.Generic.List<int>[] subTris, Mesh src, Vector3[] cartV,
            Vector3[] n, Vector2[] uv, Matrix4x4 toCart, Vector3 c, float widthScale, Transform body, MeshRenderer mr, Transform cartT)
        {
            var remap = new System.Collections.Generic.Dictionary<int, int>();
            var verts = new System.Collections.Generic.List<Vector3>();
            var norms = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var outTris = new System.Collections.Generic.List<int>[subTris.Length];
            int count = 0;
            for (int s = 0; s < subTris.Length; s++)
            {
                outTris[s] = new System.Collections.Generic.List<int>();
                foreach (int old in subTris[s])
                {
                    if (!remap.TryGetValue(old, out int idx))
                    {
                        idx = verts.Count;
                        remap[old] = idx;
                        Vector3 p = cartV[old];
                        float x = c.x + (p.x - c.x) * widthScale;
                        verts.Add(new Vector3(2f * c.x - x, p.y, 2f * c.z - p.z));   // turned 180 degrees about the centre line
                        if (n != null && n.Length == cartV.Length)
                        {
                            Vector3 nc = toCart.MultiplyVector(n[old]);
                            nc.x /= Mathf.Max(widthScale, 0.01f);   // keep normals right on the squeezed shape
                            norms.Add(new Vector3(-nc.x, nc.y, -nc.z).normalized);
                        }
                        if (uv != null && uv.Length == cartV.Length) uvs.Add(uv[old]);
                    }
                    outTris[s].Add(idx);
                    count++;
                }
            }
            if (count == 0) return;

            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            if (norms.Count == verts.Count) mesh.SetNormals(norms);
            if (uvs.Count == verts.Count) mesh.SetUVs(0, uvs);
            mesh.subMeshCount = outTris.Length;
            for (int s = 0; s < outTris.Length; s++) mesh.SetTriangles(outTris[s], s);
            if (norms.Count != verts.Count) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;

            var go = new GameObject(name);
            go.layer = body.gameObject.layer;
            go.transform.SetParent(cartT, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = mr.sharedMaterials;
            renderer.shadowCastingMode = mr.shadowCastingMode;
        }

        /// <summary>The icon material's texture offset that shows the first frame; the second frame is +0.5 in x.</summary>
        internal static Vector2 IconBaseOffset { get; private set; }

        /// <summary>
        /// The middle knob (a hand on the C.A.R.T.'s handle) shows the shrink icon instead: four arrows pointing in from
        /// the corners (Assets/cart_plus_shrink_icon.png: two frames side by side, idle and pushed in). The texture
        /// scale and offset are set from the knob's own UVs, measured here, so exactly one frame (half the texture's
        /// width) fills the knob whatever its UVs are. The middle knob is the one nearest the bar's centre line.
        /// </summary>
        private static void SetShrinkIcon(Transform knobs)
        {
            Renderer middle = null;
            float best = float.MaxValue;
            foreach (Renderer r in knobs.GetComponentsInChildren<Renderer>(true))
            {
                float d = Mathf.Abs(knobs.parent.InverseTransformPoint(r.bounds.center).x - knobs.parent.InverseTransformPoint(knobs.position).x);
                if (r.transform != knobs && d < best) { best = d; middle = r; }
            }
            Texture2D icon = CartPlusAssets.ShrinkIcon;
            if (middle == null || icon == null || middle.sharedMaterial == null)
            {
                Plugin.Log.LogWarning("Stasis button: couldn't set the shrink icon; the button keeps the hand.");
                return;
            }
            var m = new Material(middle.sharedMaterial) { name = IconMaterialName };
            m.mainTexture = icon;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", icon);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", icon);
            if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", icon);

            // Fit one frame to the knob's UVs: visible u = offset + uv.u * scale must run over 0..0.5, v over 0..1.
            Vector2 vanillaScale = m.mainTextureScale, vanillaOffset = m.mainTextureOffset;
            string uvInfo = "UVs unreadable; kept the hand's tiling";
            MeshFilter mf = middle.GetComponent<MeshFilter>();
            Mesh readable = mf != null && mf.sharedMesh != null ? CartStretch.ReadableCopy(mf.sharedMesh) : null;
            Vector2[] uv = readable != null ? readable.uv : null;
            IconBaseOffset = vanillaOffset;
            if (uv != null && uv.Length > 0)
            {
                float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                foreach (Vector2 p in uv) { u0 = Mathf.Min(u0, p.x); u1 = Mathf.Max(u1, p.x); v0 = Mathf.Min(v0, p.y); v1 = Mathf.Max(v1, p.y); }
                if (u1 - u0 > 1e-4f && v1 - v0 > 1e-4f)
                {
                    var scale = new Vector2(0.5f / (u1 - u0), 1f / (v1 - v0));
                    IconBaseOffset = new Vector2(-u0 * scale.x, -v0 * scale.y);
                    m.mainTextureScale = scale;
                    uvInfo = $"knob UVs u {u0:F3}..{u1:F3}, v {v0:F3}..{v1:F3} -> tiling {scale}, offset {IconBaseOffset}";
                }
            }
            m.mainTextureOffset = IconBaseOffset;
            middle.sharedMaterial = m;
            Plugin.Log.LogInfo($"Stasis button: '{middle.name}' shows the shrink icon (shader {m.shader.name}; the hand's tiling was {vanillaScale}, offset {vanillaOffset}; {uvInfo}).");
        }

        /// <summary>A copy of a part of the cart, turned 180 degrees about the vertical line through <paramref name="c"/>.</summary>
        private static GameObject TurnedCopy(Transform original, Transform cartT, Vector3 c)
        {
            GameObject copy = Object.Instantiate(original.gameObject, original.parent, false);
            Transform t = copy.transform;
            Vector3 p = cartT.InverseTransformPoint(original.position);
            t.position = cartT.TransformPoint(new Vector3(2f * c.x - p.x, p.y, 2f * c.z - p.z));
            t.rotation = cartT.rotation * Quaternion.Euler(0f, 180f, 0f) * (Quaternion.Inverse(cartT.rotation) * original.rotation);
            return copy;
        }

        /// <summary>
        /// The recharge meter on the far wall's outer face: a dark backing and a fill bar (unlit, so it reads in the dark).
        /// Turned to face outwards, so its local +x is the viewer's right; CartPlus sizes and colours the fill.
        /// </summary>
        private static void AddMeter(Transform cartT, Vector3 position, Vector3 screenSize)
        {
            var meter = new GameObject(MeterName);
            meter.transform.SetParent(cartT, false);
            meter.transform.localPosition = position;
            meter.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Shader unlit = Shader.Find("Sprites/Default");
            // A dark panel the size of the screen, with the bar across its middle.
            float panelW = Mathf.Max(screenSize.x * 0.92f, MeterWidth + 0.04f);
            float panelH = Mathf.Clamp(screenSize.y * 0.8f, 0.08f, 0.4f);
            MeterPart(meter.transform, "Back", Vector3.zero, new Vector3(panelW, panelH, 0.01f), unlit, new Color(0.05f, 0.05f, 0.07f), 3000);
            // The fill sits clear in front of the panel (no overlap, so it can't flicker behind it at an angle).
            // Both are see-through-capable (unlit sprite shader), and the game sorts those by their centres, which can put
            // the panel last at some angles and hide the bar: a later render queue makes the bar always draw after it.
            MeterPart(meter.transform, "Fill", new Vector3(0f, 0f, -0.012f), new Vector3(MeterWidth, Mathf.Min(0.07f, panelH * 0.4f), 0.004f), unlit, new Color(0.3f, 1f, 0.4f), 3001);
        }

        private static void MeterPart(Transform parent, string name, Vector3 position, Vector3 size, Shader shader, Color color, int renderQueue)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = size;
            MeshRenderer r = cube.GetComponent<MeshRenderer>();
            if (shader != null) r.sharedMaterial = new Material(shader) { name = "Stasis Meter " + name, color = color, renderQueue = renderQueue };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Writes the C.A.R.T.'s parts (two levels deep) to LogOutput.log.</summary>
        private static void LogParts(GameObject cart)
        {
            var sb = new StringBuilder("C.A.R.T. parts:");
            foreach (Transform child in cart.transform) Describe(sb, child, cart.transform, 1);
            var rootComps = new List<string>();
            foreach (Component c in cart.GetComponents<Component>()) if (c != null) rootComps.Add(c.GetType().Name);
            sb.Append("\n  (root) ").Append(string.Join(", ", rootComps));
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static void Describe(StringBuilder sb, Transform t, Transform cartT, int depth)
        {
            var comps = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
                if (c != null && !(c is Transform)) comps.Add(c.GetType().Name);
            Vector3 p = cartT.InverseTransformPoint(t.position);
            sb.Append('\n').Append(' ', depth * 2).Append(t.name)
              .Append($" @({p.x:F2},{p.y:F2},{p.z:F2}) s{t.localScale} [{string.Join(", ", comps)}]");
            if (depth < 2) foreach (Transform child in t) Describe(sb, child, cartT, depth + 1);
        }
    }
}
