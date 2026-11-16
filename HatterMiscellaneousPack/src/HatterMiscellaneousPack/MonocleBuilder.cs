using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HatterMiscellaneousPack
{
    /// <summary>
    /// Builds the All-Seeing Monocle at startup from one of the game's drones (grabbing and physics as a drone: its
    /// colliders, weight and feel in the hand; the [E] use button, a battery, shop slot and networking). The drone's logic
    /// and model are removed; its colliders stay. The look is a gold monocle made in code (a rim, a faint glass lens, a
    /// loop and a short chain with a clip), in the style of the game's monocle cosmetic. (Copying the cosmetic's own
    /// meshes didn't show in game, likely because its materials only draw on a player.)
    /// </summary>
    internal static class MonocleBuilder
    {
        internal const string DisplayName = "All-Seeing Monocle";
        private const string PrefabName = "Item Hatter All-Seeing Monocle";
        // any ordinary (breakable) drone; they share one design
        private static readonly string[] BasePaths =
        {
            "Items/Item Drone Battery", "Items/Item Drone Feather", "Items/Item Drone Torque", "Items/Item Drone Zero Gravity", "Items/Item Drone Heal",
        };

        private static GameObject holder;

        internal static GameObject Build()
        {
            GameObject source = null;
            string sourcePath = null;
            foreach (string p in BasePaths)
            {
                source = Resources.Load<GameObject>(p);
                if (source != null && source.GetComponent<ItemAttributes>() != null) { sourcePath = p; break; }
                source = null;
            }
            if (source == null) { Plugin.Log.LogError($"{DisplayName}: no drone found to build it from."); return null; }
            if (holder == null)
            {
                holder = new GameObject("HatterMiscellaneousPack.MonocleHolder");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
            }
            GameObject go = Object.Instantiate(source, holder.transform);
            go.name = PrefabName;
            Transform root = go.transform;
            string from = $"'{sourcePath}'";
            BuildHelpers.LogComponents(go, DisplayName, from);

            // the drone's logic goes (ItemDrone and its kind's script, sounds); its lights, effects and model are switched
            // off; its colliders, toggle, battery and physics stay
            var removed = new List<string>();
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || !mb.GetType().Name.StartsWith("ItemDrone")) continue;
                removed.Add(mb.GetType().Name);
                Object.DestroyImmediate(mb);
            }
            int muted = BuildHelpers.MuteToggleListeners(go.GetComponent<ItemToggle>());
            Plugin.Log.LogInfo($"{DisplayName}: removed {string.Join(", ", removed)}; {muted} use-button listener(s) muted.");
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            foreach (Light l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            Material template = null;
            int modelLayer = -1;   // the layer the drone's own model is drawn on (the item's root may be on one the camera skips)
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (template == null && r is MeshRenderer && r.sharedMaterial != null && !(r.GetComponent<TMPro.TMP_Text>() != null))
                {
                    template = r.sharedMaterial;
                    modelLayer = r.gameObject.layer;
                }
                r.enabled = false;
            }

            // shop entry: a fixed price
            ItemAttributes attributes = go.GetComponent<ItemAttributes>();
            Item item = Object.Instantiate(attributes.item);
            item.name = PrefabName;
            item.itemName = DisplayName;
            item.itemNameLocalized = null;
            float secs = Settings.MonocleSecondsPerCharge.Value;
            item.description = $"Press [E] while holding it to look through it: every valuable within {Settings.MonocleEspRange.Value:0} m shows through the walls. " +
                               $"Each charge lasts {secs:0.#} s and is used up as soon as you look, even if you stop early. " +
                               $"{Settings.MonocleCharges.Value} charges per battery.";
            item.itemType = SemiFunc.itemType.tracker;
            item.value = ShopPrice.Fixed(PrefabName, Settings.MonoclePriceK.Value);
            attributes.item = item;
            attributes.icon = null;   // rendered by the game from the new model

            // battery: whole charges only, nothing drains it by itself
            ItemBattery battery = go.GetComponent<ItemBattery>();
            if (battery != null)
            {
                battery.batteryBars = Settings.MonocleCharges.Value;
                battery.autoDrain = false;
                battery.batteryDrainRate = 0f;
                battery.onlyShowWhenItemToggleIsOn = false;
            }
            else Plugin.Log.LogWarning($"{DisplayName}: {from} has no battery; it can't count charges.");
            ItemToggle toggle = go.GetComponent<ItemToggle>();
            if (toggle != null) toggle.autoTurnOffWhenEquipped = true;
            else Plugin.Log.LogError($"{DisplayName}: {from} has no use button (ItemToggle).");

            Bounds grip = BuildHelpers.ColliderBounds(root, out int colliderCount);

            // the look
            var visual = new GameObject("Monocle Visual").transform;
            visual.SetParent(root, false);
            visual.gameObject.layer = go.layer;
            MakeModel(visual, go.layer, BuildHelpers.NeutralTemplate(template));
            Bounds b = PiecesBounds(visual);
            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
            // as big as the drone's colliders (SizeVsDrone), centred on them, so you grab what you see
            float droneSize = Mathf.Max(grip.size.x, grip.size.y, grip.size.z);
            float wanted = (colliderCount > 0 && droneSize > 0.02f ? droneSize : 0.14f) * Settings.MonocleSizeVsDrone.Value;
            float scale = longest > 1e-5f ? wanted / longest : 1f;
            visual.localScale = Vector3.one * scale;
            Vector3 middle = colliderCount > 0 ? grip.center : Vector3.zero;
            visual.localPosition = middle - b.center * scale;

            MonocleItem m = go.AddComponent<MonocleItem>();
            m.visual = visual;
            m.modelLayer = modelLayer >= 0 ? modelLayer : 0;
            Plugin.Log.LogInfo($"{DisplayName}: item root on layer '{LayerMask.LayerToName(go.layer)}', the drone's model on '{LayerMask.LayerToName(m.modelLayer)}'.");
            m.lensCentre = Vector3.zero;          // the model's rim: centred on its origin, facing +Z, radius 0.5
            m.lensNormal = Vector3.forward;
            m.lensRadius = 0.5f;
            Plugin.Log.LogInfo($"{DisplayName}: built from {from}, " +
                               $"{wanted * 100f:0} cm (the drone's {colliderCount} collider(s) span {droneSize * 100f:0} cm), ${Settings.MonoclePriceK.Value}K, {Settings.MonocleCharges.Value} charges.");
            return go;
        }

        /// <summary>The pieces' combined mesh box in <paramref name="visual"/>'s own space (before its scale).</summary>
        private static Bounds PiecesBounds(Transform visual)
        {
            bool any = false;
            var b = new Bounds();
            foreach (MeshFilter mf in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = Matrix4x4.TRS(mf.transform.localPosition, mf.transform.localRotation, mf.transform.localScale);
                for (Transform t = mf.transform.parent; t != null && t != visual; t = t.parent)
                    m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * m;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return any ? b : new Bounds(Vector3.zero, Vector3.one * 0.1f);
        }

        /// <summary>A gold monocle facing +Z: a rim, a faint glass lens, a loop on the rim and a short chain ending in a clip.</summary>
        private static void MakeModel(Transform parent, int layer, Material template)
        {
            Material gold = MeshLibrary.MaterialFor(template, "Monocle Gold", new Color(0.86f, 0.64f, 0.22f), 0.75f, 0.9f);
            Material glass = MeshLibrary.MaterialFor(template, "Glass Monocle", new Color(0.82f, 0.92f, 1f, 0.22f), 0.9f, 0f);
            AddPiece(parent, "Rim", Torus(0.5f, 0.06f, 48, 12), gold, layer, Vector3.zero, Quaternion.identity);
            AddPiece(parent, "Lens", Disc(0.46f, 48), glass, layer, Vector3.zero, Quaternion.identity);
            // a loop on the rim's lower right, then the chain hanging from it (links alternate their facing)
            Vector3 loop = new Vector3(0.40f, -0.40f, 0f);
            AddPiece(parent, "Loop", Torus(0.065f, 0.02f, 16, 8), gold, layer, loop, Quaternion.identity);
            Mesh link = Torus(0.05f, 0.014f, 14, 6);
            Vector3 at = loop + new Vector3(0.02f, -0.1f, 0f);
            for (int i = 0; i < 6; i++)
            {
                AddPiece(parent, "Chain Link " + (i + 1), link, gold, layer, at,
                         Quaternion.Euler(0f, i % 2 == 0 ? 90f : 0f, 90f) * Quaternion.Euler(0f, 0f, 0f));
                at += new Vector3(0.025f, -0.075f, 0f);
            }
            // the clip at the chain's end: a little bar
            AddPiece(parent, "Clip", Torus(0.035f, 0.022f, 12, 6), gold, layer, at + new Vector3(0f, -0.03f, 0f), Quaternion.Euler(90f, 0f, 0f));
        }

        private static void AddPiece(Transform parent, string name, Mesh mesh, Material mat, int layer, Vector3 at, Quaternion rot)
        {
            var g = new GameObject(name) { layer = layer };
            g.transform.SetParent(parent, false);
            g.transform.localPosition = at;
            g.transform.localRotation = rot;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Mesh Torus(float R, float r, int seg, int side)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 c = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                for (int j = 0; j <= side; j++)
                {
                    float b = j * Mathf.PI * 2f / side;
                    Vector3 dir = c * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    v.Add(c * R + dir * r); n.Add(dir);
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a0 = i * (side + 1) + j, a1 = a0 + side + 1;
                    tris.AddRange(new[] { a0, a1, a0 + 1, a0 + 1, a1, a1 + 1 });
                }
            var m = new Mesh { name = "HMP Torus" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tris, 0); m.RecalculateBounds();
            return m;
        }

        private static Mesh Disc(float radius, int seg)
        {
            var v = new List<Vector3> { Vector3.zero }; var tris = new List<int>();
            for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2f / seg; v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius); }
            for (int i = 1; i <= seg; i++) tris.AddRange(new[] { 0, i, i + 1, 0, i + 1, i });   // both sides
            var m = new Mesh { name = "HMP Disc" };
            m.SetVertices(v); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
