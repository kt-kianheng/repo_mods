using System.Collections.Generic;
using UnityEngine;

namespace HatterMeleeWeaponsPack
{
    /// <summary>
    /// Builds the Scythe at startup from the game's baseball bat, like the Nunchuck: the bat's melee script holds a
    /// weapon at its grip point and points it where you look, so the scythe's shaft starts at your hand and reaches
    /// out towards the crosshair. The bat's mesh is hidden, its own hurt zone and durability hooks switched off, and
    /// its grab colliders refitted to the shaft and the blade. Cutting, and turning the blade to the side of the last
    /// swing, is done by <see cref="Scythe"/>.
    ///
    /// Model space (scythe.mesh, from tools/build_scythe.py): the shaft runs along +Y from 0 (grip end) to 2.0 m;
    /// the blade leaves the head along +Z and curls back towards the grip, edge on the grip side.
    /// </summary>
    internal static class ScytheBuilder
    {
        private const string BatPath = "Items/Item Melee Baseball Bat";
        internal const string PrefabName = "Item Melee Scythe";
        internal const string DisplayName = "Scythe";
        internal const string ModelFile = "scythe.mesh";

        internal const float SnathLength = 2.00f;
        private const float GripInset = 0.05f;   // the bat's grip point sits this far up from the shaft's end
        private const float ShaftGrabWidth = 0.07f;   // the shaft's grab box is a bit fatter than the shaft, so it's easy to aim at

        // in model space (see build_scythe.py)
        internal static readonly Vector3 BladeBoxCentre = new Vector3(0f, 1.84f, 0.51f);
        private static readonly Vector3 BladeBoxSize = new Vector3(0.014f, 0.13f, 1.05f);
        internal static readonly Quaternion BladeBoxTilt = Quaternion.Euler(16f, 0f, 0f);   // the blade droops 0.30 m over 1.02 m
        // the cutting zone: between the blade's edge and the holder (grip side of the blade, below the shaft)
        // (only a broad search: a cut needs the target right at the blade's edge, see Scythe)
        private static readonly Vector3 ZoneCentre = new Vector3(0f, 1.44f, 0.56f);
        private static readonly Vector3 ZoneHalf = new Vector3(0.50f, 0.46f, 0.52f);

        private static GameObject holder;

        internal static GameObject Build()
        {
            GameObject bat = Resources.Load<GameObject>(BatPath);
            if (bat == null) { Plugin.Log.LogError($"'{BatPath}' not found."); return null; }
            var model = MeshLibrary.Get(ModelFile, "SC_Scythe");
            if (model == null) return null;

            if (holder == null)
            {
                holder = new GameObject("HatterMeleeWeaponsPack.ScytheHolder");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
            }
            GameObject go = Object.Instantiate(bat, holder.transform);
            go.name = PrefabName;
            Transform root = go.transform;
            SetupItem(go);
            // Uses cuts per full charge: one battery bar each (Scythe removes a bar per cut)
            ItemBattery battery = go.GetComponent<ItemBattery>();
            if (battery != null) battery.batteryBars = Settings.ScytheUses.Value;
            else Plugin.Log.LogWarning("The bat has no battery; the scythe's uses can't be counted.");

            Transform grab = root.Find("Force Grab Point");
            Transform swing = root.Find("Swing Point");
            Bounds batBounds = NunchuckBuilder.LocalRendererBounds(root);
            Material template = NunchuckBuilder.PickTemplate(root);
            Vector3 grip = grab != null ? grab.localPosition : batBounds.center - NunchuckBuilder.LongAxis(batBounds) * batBounds.extents.magnitude * 0.8f;
            Vector3 axis = swing != null && (swing.localPosition - grip).sqrMagnitude > 1e-4f
                ? (swing.localPosition - grip).normalized
                : NunchuckBuilder.LongAxis(batBounds);

            // model -> item: shaft along the bat's grip-to-tip axis, turned round it by BladeRoll
            Vector3 bottom = grip - axis * GripInset;
            Quaternion rotation = Quaternion.AngleAxis(Settings.ScytheBladeRoll.Value, axis) * Quaternion.FromToRotation(Vector3.up, axis);
            Vector3 ToItem(Vector3 modelPoint) => bottom + rotation * modelPoint;

            // the bat's melee script normally rolls the whole weapon round its own axis so its hitting side faces the
            // way you drag it (ItemMelee.TurnWeapon). On the scythe that fought Scythe's own blade turning (the two
            // added up or cancelled out, so the blade flipped the wrong way): switch it off, Scythe does the turning
            ItemMelee melee = go.GetComponent<ItemMelee>();
            if (melee != null) melee.turnWeapon = false;

            // the game's on/off switch for items: having one shows "[E]" after the item's name while it's held, reads the
            // key and tells every player. Scythe follows its state (off = blade on one side, on = the other side)
            ItemToggle toggle = go.GetComponent<ItemToggle>();
            if (toggle == null) toggle = go.AddComponent<ItemToggle>();
            toggle.toggleState = false;
            toggle.playSound = false;                 // no device click; the blade turning is the feedback
            toggle.autoTurnOffWhenEquipped = false;   // keep the side through inventory slots
            if (battery != null) battery.onlyShowWhenItemToggleIsOn = false;

            NunchuckBuilder.HideBatMesh(root);
            NunchuckBuilder.DisableBatHurtZones(root);
            NunchuckBuilder.MuteBatDurabilityHooks(root);
            Transform[] hosts = FitColliders(root, new[]
            {
                (ToItem(new Vector3(0f, SnathLength / 2f, 0f)), rotation, new Vector3(ShaftGrabWidth, SnathLength, ShaftGrabWidth)),   // the shaft
                (ToItem(BladeBoxCentre), rotation * BladeBoxTilt, BladeBoxSize),                                     // the blade
            });
            if (swing != null) swing.localPosition = ToItem(new Vector3(0f, 1.665f, 1.02f));   // swing sounds follow the blade tip

            GameObject mesh = MeshLibrary.Attach(model, root, "Scythe Mesh", template);
            mesh.transform.localPosition = bottom;
            mesh.transform.localRotation = rotation;

            // the blade turns round the shaft at runtime (to the side of the last swing): the scythe needs the parts and
            // the model-space layout to do that
            var scythe = go.AddComponent<Scythe>();
            scythe.model = mesh.transform;
            scythe.bladeCollider = hosts != null && hosts.Length > 1 ? hosts[1] : null;
            scythe.modelBottom = bottom;
            scythe.modelRotation = rotation;
            scythe.bladeBoxCentre = BladeBoxCentre;
            scythe.bladeBoxTilt = BladeBoxTilt;
            scythe.zoneCentre = ZoneCentre;
            scythe.zoneHalf = ZoneHalf;
            scythe.gripPoint = grip;
            scythe.shaftAxis = axis;
            // the bat's swing sound, re-voiced with the sword slice, for the pull-back jerk
            scythe.sliceTemplate = go.GetComponent<ItemMelee>();
            Plugin.Log.LogInfo($"Scythe from the bat: grip {grip}, shaft axis {axis}, blade roll {Settings.ScytheBladeRoll.Value:0}°.");
            return go;
        }

        private static void SetupItem(GameObject go)
        {
            ItemAttributes attributes = go.GetComponent<ItemAttributes>();
            Item source = attributes.item;
            Item item = Object.Instantiate(source);
            item.name = PrefabName;
            item.itemName = DisplayName;
            item.itemNameLocalized = null;
            item.description = "A long, crooked scythe. Reach past an enemy so the blade is behind them, then pull it back towards you (or back away) to cut. Press E to flip the blade to the other side.";
            if (source.value != null)
            {
                Value price = Object.Instantiate(source.value);
                price.name = PrefabName + " Value";
                price.valueMin *= Settings.ScythePriceMultiplier.Value;
                price.valueMax *= Settings.ScythePriceMultiplier.Value;
                item.value = price;
            }
            attributes.item = item;
            attributes.icon = null;
        }

        /// <summary>Fresh grab boxes for the shaft and the blade (item-space centre, rotation, size).</summary>
        private static Transform[] FitColliders(Transform root, (Vector3 centre, Quaternion rotation, Vector3 size)[] boxes)
            => NunchuckBuilder.ReplaceGrabColliders(root, "Scythe", boxes);
    }
}
