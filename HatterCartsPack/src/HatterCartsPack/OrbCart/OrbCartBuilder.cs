using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace HatterCartsPack
{
    /// <summary>
    /// Builds the Orb C.A.R.T. prefab at startup:
    ///   1. clones the vanilla Pocket C.A.R.T. (keeps pocketing, handling, physics),
    ///   2. grafts the Hauler's self-contained "Item Valuable Box" (hatch, absorb logic, extraction) into the basket,
    ///   3. restricts the box to enemy orbs and sets its capacity,
    ///   4. the handle screen shows the stored orbs' total value; a second screen on the far end shows how full it is,
    ///   5. gives it its own Item definition, icon, price, tint and decals.
    /// The prefab lives under an inactive, DontDestroyOnLoad holder so it never wakes up itself;
    /// REPOLib/Photon instantiate copies of it.
    /// </summary>
    internal static class OrbCartBuilder
    {
        internal const string PrefabName = "Item Cart Orb";
        internal const string DisplayName = "Orb C.A.R.T.";
        private const string PocketCartPath = "Items/Item Cart Small";
        private const string HaulerPath = "Items/Item Vehicle Semiscooter";

        // Pocket C.A.R.T. basket centre is offset slightly on X in the vanilla model.
        private const float BasketCenterX = 0.02f;

        // ---- Fixed tuning (not configurable) ------------------------------------------------------
        internal const float PriceMultiplier = 3f;     // shop price relative to the vanilla Pocket C.A.R.T.
        internal static readonly bool Decals = true;             // orb emblem on the sides and front
        internal const float HatchFill = 1f;           // hatch covers the basket opening edge to edge
        internal const float HatchOffset = -0.15f;     // metres from the measured rim; flush with the top of the cart
        internal static readonly bool HatchStretch = true;       // stretch the hatch to the basket's exact width and depth

        private static GameObject holder;

        internal static GameObject Build()
        {
            GameObject pocket = Resources.Load<GameObject>(PocketCartPath);
            GameObject hauler = Resources.Load<GameObject>(HaulerPath);
            if (pocket == null || hauler == null)
            {
                Plugin.Log.LogError($"Vanilla prefabs not found (pocket cart: {pocket != null}, hauler: {hauler != null}). Has the game changed its resource paths?");
                return null;
            }

            ItemAttributes srcAttributes = pocket.GetComponent<ItemAttributes>();
            ItemValuableBox srcBox = hauler.GetComponentInChildren<ItemValuableBox>(true);
            if (srcAttributes == null || srcAttributes.item == null || srcBox == null)
            {
                Plugin.Log.LogError("Vanilla prefabs are missing expected components (ItemAttributes / ItemValuableBox).");
                return null;
            }

            holder = new GameObject("HatterCartsPack.OrbCart.PrefabHolder");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            holder.hideFlags = HideFlags.HideAndDontSave;

            GameObject cart = Object.Instantiate(pocket, holder.transform);
            cart.name = PrefabName;

            SetupItem(cart, srcAttributes.item, pocket);
            ItemValuableBox box = GraftBox(cart, srcBox);
            SetupScreen(cart);
            if (Settings.TryGetOrbCartTint(out Color tint)) TintBody(cart, tint);
            if (Decals) AddDecals(cart);

            Plugin.Log.LogInfo($"Built {DisplayName} prefab (box limit ${box.valueLimit:N0}).");
            return cart;
        }

        private static void SetupItem(GameObject cart, Item source, GameObject pocketPrefab)
        {
            Item item = Object.Instantiate(source);
            item.name = PrefabName;               // StatsManager / save key — must be unique
            item.itemName = DisplayName;
            item.itemNameLocalized = null;        // force the game to fall back to itemName
            item.description = "A pocketable C.A.R.T. with a hatch that only swallows enemy orbs. The handle screen shows the orbs' total value, the far-end screen how full it is.";

            if (source.value != null)
            {
                Value price = Object.Instantiate(source.value);
                price.name = PrefabName + " Value";
                price.valueMin *= PriceMultiplier;
                price.valueMax *= PriceMultiplier;
                item.value = price;
            }

            ItemAttributes attributes = cart.GetComponent<ItemAttributes>();
            attributes.item = item;

            Sprite icon = OrbCartIcon.Build(source, pocketPrefab) ?? OrbCartAssets.Icon;
            if (icon != null)
                attributes.icon = icon;           // a preset icon skips the game's runtime icon render
        }

        private static ItemValuableBox GraftBox(GameObject cart, ItemValuableBox srcBox)
        {
            // The Hauler's box is a child with its own PhotonView, so it networks correctly as a nested view.
            GameObject boxObject = Object.Instantiate(srcBox.gameObject, cart.transform, false);
            boxObject.name = "Orb Box";

            Transform t = boxObject.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            ItemValuableBox box = boxObject.GetComponent<ItemValuableBox>();
            box.valueLimit = Settings.OrbCartCapacity.Value;
            // Orbs come in several sizes; the component filter (EnemyValuable only) does the gatekeeping instead.
            box.typeLimit = ValuableVolume.Type.VeryTall;

            // The checker's component filter is swapped to EnemyValuable in Patches.InsideBoxCheckerStart.
            // Its size filter stays on until then, so if that patch ever fails the box behaves like a vanilla Hauler bed
            // rather than accepting everything.
            if (box.boxChecker != null)
                box.boxChecker.gameObject.AddComponent<OrbCartChecker>();

            // Nothing in the box should physically block orbs dropping into the basket.
            foreach (Collider c in boxObject.GetComponentsInChildren<Collider>(true))
                c.isTrigger = true;

            // The box must stay fixed to the cart. Anything that lets it move on its own (a body, a joint, or a
            // transform/rigidbody sync on its PhotonView) only acts on the host, where physics and ownership live,
            // and dropped the hatch to the bottom of the basket there while clients looked fine.
            StripBoxMotion(boxObject);

            // The Hauler's pop-up screen is replaced by the cart's handle screen (see OrbCartScreen).
            if (box.textScreenAnimatorTransform != null) HideVisuals(box.textScreenAnimatorTransform);
            else if (box.textScreen != null) HideVisuals(box.textScreen.transform);

            // The Hauler's bed railing doesn't belong in a basket.
            Transform railing = t.Find("Railing");
            if (railing != null) HideVisuals(railing);

            FitBoxToBasket(cart, box);

            boxObject.AddComponent<OrbCartBox>();
            return box;
        }

        /// <summary>
        /// Removes joints, rigidbodies and Photon transform/rigidbody sync components from the grafted box,
        /// and logs what the box is made of, so a host-only drift can be traced in LogOutput.log.
        /// </summary>
        private static void StripBoxMotion(GameObject boxObject)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (Component c in boxObject.GetComponentsInChildren<Component>(true))
                if (c != null && !(c is Transform)) parts.Add(c.transform.name + ":" + c.GetType().Name);
            Plugin.Log.LogInfo("Orb Box parts: " + string.Join(", ", parts));

            var removed = new System.Collections.Generic.List<string>();
            foreach (Joint j in boxObject.GetComponentsInChildren<Joint>(true))
            {
                removed.Add(j.name + ":" + j.GetType().Name);
                Object.DestroyImmediate(j);
            }

            foreach (MonoBehaviour mb in boxObject.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb is Photon.Pun.PhotonTransformView || mb is Photon.Pun.PhotonTransformViewClassic || mb is Photon.Pun.PhotonRigidbodyView)
                {
                    removed.Add(mb.name + ":" + mb.GetType().Name);
                    Object.DestroyImmediate(mb);
                }
            }

            foreach (Rigidbody rb in boxObject.GetComponentsInChildren<Rigidbody>(true))
            {
                removed.Add(rb.name + ":Rigidbody");
                Object.DestroyImmediate(rb);
            }

            // Drop the removed (now null) entries from every PhotonView's observed list.
            foreach (Photon.Pun.PhotonView view in boxObject.GetComponentsInChildren<Photon.Pun.PhotonView>(true))
                view.ObservedComponents?.RemoveAll(c => c == null);

            if (removed.Count > 0)
                Plugin.Log.LogInfo("Orb Box: removed " + string.Join(", ", removed) + " so the box stays fixed to the cart.");
        }

        /// <summary>
        /// Sizes and places the box so it fills the basket and its hatch is the cart's top face,
        /// adds a solid lid for orbs to rest on, and moves the detection volume above the lid.
        /// Everything is measured from the actual meshes/colliders, so it adapts if the models change.
        /// </summary>
        private static void FitBoxToBasket(GameObject cart, ItemValuableBox box)
        {
            Transform cartT = cart.transform;
            Transform boxT = box.transform;

            if (!TryGetBasketBounds(cart, out Bounds basket))
            {
                Plugin.Log.LogWarning("Could not measure the basket; using fallback hatch placement.");
                basket = new Bounds(new Vector3(BasketCenterX, 0.10f, 0f), new Vector3(0.72f, 0.56f, 0.72f));
            }

            // The "In Cart" volume reaches well above the walls (it catches items piled high), so take the rim
            // height from the cart body mesh itself: the hatch's top must sit at the top of the walls.
            string rimSource = "in-cart volume";
            if (TryGetRimHeight(cart, out float rim) && rim > basket.min.y + 0.1f && rim < basket.max.y)
            {
                Vector3 min = basket.min, max = basket.max;
                max.y = rim;
                basket.SetMinMax(min, max);
                rimSource = "cart walls";
            }

            // Box measured at unit scale, identity rotation, origin at the cart origin (set by the caller).
            if (!TryGetVisibleMeshBounds(boxT, cartT, out Bounds boxBounds) || boxBounds.size.x < 0.01f || boxBounds.size.y < 0.01f || boxBounds.size.z < 0.01f)
            {
                Plugin.Log.LogWarning("Could not measure the hatch box; using fallback hatch placement.");
                boxBounds = new Bounds(new Vector3(0f, 0.17f, 0f), new Vector3(0.55f, 0.34f, 0.55f));
            }

            float fill = HatchFill;
            float sx = basket.size.x * fill / boxBounds.size.x;
            float sz = basket.size.z * fill / boxBounds.size.z;
            float footprint = Mathf.Min(sx, sz);
            // Keep the box inside the basket vertically; it may be squashed a little but never pokes out of the bottom.
            float sy = Mathf.Min(footprint, basket.size.y * 0.98f / boxBounds.size.y);
            Vector3 scale = new Vector3(sx, sy, sz);
            if (!HatchStretch) scale = new Vector3(footprint, sy, footprint);
            boxT.localScale = scale;

            float rimY = basket.max.y + HatchOffset;
            boxT.localPosition = new Vector3(
                basket.center.x - boxBounds.center.x * scale.x,
                rimY - boxBounds.max.y * scale.y,
                basket.center.z - boxBounds.center.z * scale.z);

            // Solid lid flush with the rim: orbs sit on the closed hatch, then the hatch opens and pulls them in.
            // It's part of the cart's own rigidbody, so it also stops other items falling into the basket.
            // It's a copy of one of the cart's own grabbable wall colliders, so it carries the "Phys Grab Object"
            // tag, layer and PhysGrabObjectCollider the grab beam needs: grabbing the hatch grabs the cart.
            const float lidThickness = 0.03f;
            GameObject lid = CreateGrabbableCollider(cart, "Orb Hatch Lid");
            lid.transform.SetParent(cartT, false);
            lid.transform.localPosition = new Vector3(basket.center.x, rimY - lidThickness * 0.5f, basket.center.z);
            lid.transform.localRotation = Quaternion.identity;
            lid.transform.localScale = Vector3.one;
            BoxCollider lidCollider = lid.GetComponent<BoxCollider>();
            lidCollider.isTrigger = false;
            lidCollider.center = Vector3.zero;
            lidCollider.size = new Vector3(basket.size.x, lidThickness, basket.size.z);

            // Detection volume: from just under the lid surface to a hand's height above it.
            if (box.boxChecker != null)
            {
                const float above = 0.45f;
                Transform checker = box.boxChecker.transform;
                Vector3 centerCart = new Vector3(basket.center.x, rimY + above * 0.5f - 0.02f, basket.center.z);
                Vector3 sizeCart = new Vector3(basket.size.x * 0.98f, above + 0.04f, basket.size.z * 0.98f);
                checker.localRotation = Quaternion.identity;
                checker.position = cartT.TransformPoint(centerCart);
                Vector3 parentScale = checker.parent != null ? checker.parent.lossyScale : Vector3.one;
                Vector3 cartScale = cartT.lossyScale;
                checker.localScale = new Vector3(
                    sizeCart.x * cartScale.x / parentScale.x,
                    sizeCart.y * cartScale.y / parentScale.y,
                    sizeCart.z * cartScale.z / parentScale.z);
            }

            Plugin.Log.LogInfo($"Hatch fitted: basket {basket.size}, box scale {scale}, rim at y={rimY:F3} (from {rimSource}).");
        }

        /// <summary>
        /// Top of the cart's walls (cart-local Y). Measured from the "Cart Wall Collider" pieces, taking the height
        /// most of them share, so the handle bar (part of the body mesh) and small trim pieces don't count.
        /// </summary>
        private static bool TryGetRimHeight(GameObject cart, out float rim)
        {
            rim = 0f;
            var tops = new System.Collections.Generic.List<float>();
            foreach (Transform child in cart.transform)
            {
                if (child.name != "Cart Wall Collider") continue;
                MeshFilter mf = child.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                tops.Add(TransformBounds(mf.sharedMesh.bounds, child, cart.transform).max.y);
            }

            if (tops.Count >= 2)
            {
                // Largest group of wall tops within 2 cm of each other.
                int bestCount = 0;
                float bestSum = 0f;
                foreach (float t in tops)
                {
                    int count = 0; float sum = 0f;
                    foreach (float u in tops)
                        if (Mathf.Abs(u - t) <= 0.02f) { count++; sum += u; }
                    if (count > bestCount) { bestCount = count; bestSum = sum; }
                }
                if (bestCount >= 2)
                {
                    rim = bestSum / bestCount;
                    return true;
                }
            }

            // Fallback: the visible body mesh (includes the handle, so this sits a little high).
            Transform body = cart.transform.Find("Cart Mesh");
            if (body == null || !TryGetVisibleMeshBounds(body, cart.transform, out Bounds b)) return false;
            rim = b.max.y;
            return true;
        }

        /// <summary>Cart-local bounds of the basket interior, from the cart's "In Cart" trigger volume.</summary>
        private static bool TryGetBasketBounds(GameObject cart, out Bounds bounds)
        {
            bounds = default;
            PhysGrabInCart inCart = cart.GetComponentInChildren<PhysGrabInCart>(true);
            BoxCollider volume = inCart != null ? inCart.GetComponent<BoxCollider>() : null;
            if (volume == null) return false;
            bounds = TransformBounds(new Bounds(volume.center, volume.size), volume.transform, cart.transform);
            return bounds.size.x > 0.05f && bounds.size.y > 0.05f && bounds.size.z > 0.05f;
        }

        /// <summary>Bounds (in <paramref name="space"/>) of every mesh under root that has an enabled renderer.</summary>
        private static bool TryGetVisibleMeshBounds(Transform root, Transform space, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled || mf.sharedMesh == null) continue;
                Bounds b = TransformBounds(mf.sharedMesh.bounds, mf.transform, space);
                if (!any) { bounds = b; any = true; }
                else bounds.Encapsulate(b);
            }
            return any;
        }

        private static Bounds TransformBounds(Bounds local, Transform from, Transform to)
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

        /// <summary>
        /// A new collider object set up exactly like the cart's own grabbable colliders (tag, layer, grab components),
        /// made by copying one of them. Falls back to a plain collider with the same tag/layer if none is found.
        /// </summary>
        private static GameObject CreateGrabbableCollider(GameObject cart, string name)
        {
            PhysGrabObjectCollider reference = null;
            foreach (PhysGrabObjectCollider c in cart.GetComponentsInChildren<PhysGrabObjectCollider>(true))
            {
                BoxCollider box = c.GetComponent<BoxCollider>();
                if (box != null && !box.isTrigger && c.transform.childCount == 0) { reference = c; break; }
            }

            GameObject go;
            if (reference != null)
            {
                // Instantiate inside the (inactive) cart template so the copy's scripts never start on their own.
                go = Object.Instantiate(reference.gameObject, cart.transform, false);
                // Keep only what's needed: the collider and the grab components.
                foreach (Component comp in go.GetComponents<Component>())
                    if (!(comp is Transform) && !(comp is BoxCollider) && !(comp is PhysGrabObjectCollider) && !(comp is PhysGrabObjectBoxCollider))
                        Object.DestroyImmediate(comp);
            }
            else
            {
                Plugin.Log.LogWarning("No grabbable collider found to copy; the hatch may not be grabbable.");
                go = new GameObject();
                go.layer = ReferenceColliderLayer(cart);
                go.tag = "Phys Grab Object";
                go.AddComponent<BoxCollider>();
                go.AddComponent<PhysGrabObjectCollider>();
            }
            go.name = name;
            return go;
        }

        private static int ReferenceColliderLayer(GameObject cart)
        {
            foreach (BoxCollider c in cart.GetComponentsInChildren<BoxCollider>(true))
                if (!c.isTrigger && c.GetComponent<PhysGrabObjectBoxCollider>() != null)
                    return c.gameObject.layer;
            return cart.layer;
        }

        private static void SetupScreen(GameObject cart)
        {
            ValueScreen valueScreen = cart.GetComponentInChildren<ValueScreen>(true);
            if (valueScreen == null || valueScreen.displayText == null)
            {
                Plugin.Log.LogWarning("Pocket C.A.R.T. value screen not found; the Orb C.A.R.T. will have no fill readout.");
                return;
            }

            // Stop the vanilla $-value display from writing to the text, then take it over: the handle screen shows the
            // total value of the stored orbs (user, 18 Nov; it used to show the fill percentage).
            valueScreen.enabled = false;
            OrbCartScreen screen = cart.AddComponent<OrbCartScreen>();
            screen.text = valueScreen.displayText;
            screen.showValue = true;

            // The far end gets a screen of its own with the fill percentage, where the front emblem used to be:
            // a dark panel flat on the outside of the far wall, with a copy of the handle screen's text on it.
            TextMeshPro percentText = AddFarScreen(cart.transform, valueScreen.displayText);
            if (percentText != null)
            {
                OrbCartScreen far = cart.AddComponent<OrbCartScreen>();
                far.text = percentText;
                far.showValue = false;
            }
        }

        private const float FarScreenZ = 0.405f;            // outside face of the far wall (where the front emblem sat)
        private const float FarScreenY = 0.07f;
        private static readonly Vector2 FarScreenSize = new Vector2(0.26f, 0.13f);

        private static TextMeshPro AddFarScreen(Transform cart, TextMeshPro source)
        {
            var root = new GameObject("Orb Fill Screen");
            root.transform.SetParent(cart, false);
            root.transform.localPosition = new Vector3(BasketCenterX, FarScreenY, FarScreenZ);
            root.transform.localEulerAngles = new Vector3(0f, 180f, 0f);   // faces outwards (+z), like the front emblem did

            // Panel: an unlit dark quad 1 mm off the wall.
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.name = "Panel";
            panel.transform.SetParent(root.transform, false);
            panel.transform.localPosition = new Vector3(0f, 0f, -0.001f);
            panel.transform.localScale = new Vector3(FarScreenSize.x, FarScreenSize.y, 1f);
            Shader unlit = Shader.Find("Sprites/Default");
            MeshRenderer pr = panel.GetComponent<MeshRenderer>();
            if (unlit != null) pr.sharedMaterial = new Material(unlit) { name = "Orb Fill Screen Panel", color = new Color(0.03f, 0.03f, 0.04f, 1f) };
            pr.shadowCastingMode = ShadowCastingMode.Off;

            // Text: a copy of the handle screen's text object, without anything but the text itself.
            GameObject copy = Object.Instantiate(source.gameObject, root.transform, false);
            copy.name = "Text";
            for (int i = copy.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(copy.transform.GetChild(i).gameObject);
            foreach (MonoBehaviour mb in copy.GetComponents<MonoBehaviour>())
                if (!(mb is TextMeshPro)) Object.DestroyImmediate(mb);
            copy.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            copy.transform.localRotation = Quaternion.identity;
            // Same world size as the handle screen's text.
            Vector3 s = source.transform.lossyScale, c = cart.lossyScale;
            copy.transform.localScale = new Vector3(s.x / c.x, s.y / c.y, s.z / c.z);
            TextMeshPro text = copy.GetComponent<TextMeshPro>();
            if (text == null) { Plugin.Log.LogWarning("Orb C.A.R.T.: couldn't copy the screen text; no fill screen on the far end."); return null; }
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = new Vector2(FarScreenSize.x / Mathf.Max(Mathf.Abs(copy.transform.localScale.x), 1e-4f),
                                                       FarScreenSize.y / Mathf.Max(Mathf.Abs(copy.transform.localScale.y), 1e-4f));
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.1f;
            text.fontSizeMax = Mathf.Max(text.fontSize, 0.1f);
            Plugin.Log.LogInfo($"Orb C.A.R.T. fill screen: handle text '{source.name}' world scale {source.transform.lossyScale}, size {source.rectTransform.sizeDelta}, font {source.fontSize}; at {root.transform.localPosition}.");
            return text;
        }

        private static void TintBody(GameObject cart, Color tint)
        {
            Transform mesh = cart.transform.Find("Cart Mesh");
            if (mesh == null) return;
            foreach (Renderer r in mesh.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    Material m = new Material(materials[i]) { name = materials[i].name + " (Orb Tint)" };
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * tint);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", m.GetColor("_Color") * tint);
                    materials[i] = m;
                }
                r.sharedMaterials = materials;
            }
        }

        private static void AddDecals(GameObject cart)
        {
            Texture2D emblem = OrbCartAssets.Emblem;
            Transform mesh = cart.transform.Find("Cart Mesh");
            Renderer reference = mesh != null ? mesh.GetComponentInChildren<Renderer>(true) : null;
            if (emblem == null || reference == null || reference.sharedMaterial == null) return;

            Material decal = new Material(reference.sharedMaterial) { name = "Orb Emblem", mainTexture = emblem };
            if (decal.HasProperty("_BaseColor")) decal.SetColor("_BaseColor", Color.white);
            if (decal.HasProperty("_Color")) decal.SetColor("_Color", Color.white);
            if (decal.HasProperty("_MainTex")) decal.SetTexture("_MainTex", emblem);
            if (decal.HasProperty("_BaseMap")) decal.SetTexture("_BaseMap", emblem);

            const float size = 0.26f;
            const float y = 0.07f;
            // Quad faces -Z by default; rotate so each decal faces outward from its wall.
            AddDecal(cart.transform, decal, "Orb Decal Left", new Vector3(-0.375f, y, 0f), new Vector3(0f, 90f, 0f), size);
            AddDecal(cart.transform, decal, "Orb Decal Right", new Vector3(0.415f, y, 0f), new Vector3(0f, -90f, 0f), size);
            // (The far end has the fill screen where the front emblem used to be.)
        }

        private static void AddDecal(Transform parent, Material material, string name, Vector3 position, Vector3 euler, float size)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = position;
            quad.transform.localEulerAngles = euler;
            quad.transform.localScale = new Vector3(size, size, 1f);
            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void HideVisuals(Transform root)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (Light l in root.GetComponentsInChildren<Light>(true)) l.enabled = false;
        }
    }
}
