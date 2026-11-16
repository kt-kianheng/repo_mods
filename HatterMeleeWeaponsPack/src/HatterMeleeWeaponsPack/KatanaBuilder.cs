using UnityEngine;

namespace HatterMeleeWeaponsPack
{
    /// <summary>
    /// Builds the Katana at startup from the game's own sword, so it keeps everything the sword does: grabbing, how
    /// it is held and turned, the swing when you scroll it out, its hurt box and damage, its durability, sounds and
    /// networking. Only the looks change (the sword's mesh is hidden and the katana model laid along it; the sword's
    /// colliders are kept, so it handles exactly like the sword), the blade's damage (BladeDamage), its energy (Energy
    /// bars), an E switch (the game's ItemToggle) and the price (twice the Scythe's). Charging with E, the rift and its
    /// wave are <see cref="Katana"/>.
    ///
    /// Model space (katana.mesh, from tools/build_katana.py): along +Y from 0 (the pommel) to 0.977 m (the tip). Grip
    /// 0-0.265, guard 0.265-0.272, blade 0.272-0.977. The edge faces +Z; the blade curves back towards -Z.
    /// </summary>
    internal static class KatanaBuilder
    {
        private const string SwordPath = "Items/Item Melee Sword";
        private const string BatPath = "Items/Item Melee Baseball Bat";   // prices are set against the bat, like the Scythe's
        internal const string PrefabName = "Item Melee Katana";
        internal const string DisplayName = "Katana";
        internal const string ModelFile = "katana.mesh";

        private const float HandFromPommel = 0.17f;    // where the sword's grip point lands on the katana's handle
        internal const float GripLength = 0.265f;
        internal const float BladeRoot = 0.272f;
        internal const float BladeTip = 0.977f;

        private static GameObject holder;

        internal static GameObject Build()
        {
            GameObject sword = Resources.Load<GameObject>(SwordPath);
            if (sword == null) { Plugin.Log.LogError($"'{SwordPath}' not found."); return null; }
            var model = MeshLibrary.Get(ModelFile, "KT_Katana");
            if (model == null) return null;

            if (holder == null)
            {
                holder = new GameObject("HatterMeleeWeaponsPack.KatanaHolder");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
            }
            GameObject go = Object.Instantiate(sword, holder.transform);
            go.name = PrefabName;
            Transform root = go.transform;
            SetupItem(go);

            // where the sword is held and which way it points, and which way its blade is wide (its edges)
            Transform grab = root.Find("Force Grab Point");
            Transform swing = root.Find("Swing Point");
            Bounds swordBounds = NunchuckBuilder.LocalRendererBounds(root);
            Material template = NunchuckBuilder.PickTemplate(root);
            Vector3 grip = grab != null ? grab.localPosition : swordBounds.center - NunchuckBuilder.LongAxis(swordBounds) * swordBounds.extents.magnitude * 0.8f;
            Vector3 axis = swing != null && (swing.localPosition - grip).sqrMagnitude > 1e-4f
                ? (swing.localPosition - grip).normalized
                : NunchuckBuilder.LongAxis(swordBounds);
            Vector3 side = WidthAxis(swordBounds, axis);
            side = Quaternion.AngleAxis(Settings.KatanaEdgeRoll.Value, axis) * side;

            // model -> item: the katana's length along the sword's, its edge along the sword's edge
            Quaternion rotation = Quaternion.LookRotation(side, axis);
            Vector3 bottom = grip - axis * HandFromPommel;
            Vector3 ToItem(Vector3 modelPoint) => bottom + rotation * modelPoint;

            // only the sword's mesh is hidden. Its colliders stay exactly as they are: they set how the held sword turns and
            // takes the scroll-out swing's push, so the katana handles like the vanilla sword (the model sits along them)
            NunchuckBuilder.HideBatMesh(root);
            LogColliders(root);
            HurtCollider swordHurt = root.GetComponentInChildren<HurtCollider>(true);
            if (swordHurt != null) swordHurt.enemyDamage = Settings.KatanaBladeDamage.Value;
            // energy: battery bars per full charge (the first wave of a chain uses WaveEnergy of them)
            ItemBattery battery = go.GetComponent<ItemBattery>();
            if (battery != null) { battery.batteryBars = Settings.KatanaEnergy.Value; battery.onlyShowWhenItemToggleIsOn = false; }
            else Plugin.Log.LogWarning("The sword has no battery; the katana's energy can't be counted.");

            // E charges the katana: the game's on/off switch for items shows "[E]" after the name while held, reads the
            // key and tells every player (Katana follows its state: on = charged, its sharp half glowing)
            ItemToggle toggle = go.GetComponent<ItemToggle>();
            if (toggle == null) toggle = go.AddComponent<ItemToggle>();
            toggle.toggleState = false;
            toggle.playSound = true;                  // the game's device on/off click
            toggle.autoTurnOffWhenEquipped = true;    // putting it away uncharges it

            GameObject mesh = MeshLibrary.Attach(model, root, "Katana Mesh", template);
            mesh.transform.localPosition = bottom;
            mesh.transform.localRotation = rotation;

            var katana = go.AddComponent<Katana>();
            katana.bladeBase = ToItem(new Vector3(0f, BladeRoot, 0f));
            katana.bladeTip = ToItem(new Vector3(0f, BladeTip, 0f));
            katana.soundTemplate = go.GetComponent<ItemMelee>();
            // the sharp half of the blade (Blender materials "Steel Edge" and "Steel Hamon") glows when charged
            katana.bladeRenderer = mesh.GetComponent<MeshRenderer>();
            var slots = new System.Collections.Generic.List<int>();
            for (int i = 0; i < model.Parts.Count; i++)
                if (model.Parts[i].MaterialName == "Steel Edge" || model.Parts[i].MaterialName == "Steel Hamon") slots.Add(i);
            katana.glowSlots = slots.ToArray();
            if (slots.Count < 2) Plugin.Log.LogWarning($"Katana: found {slots.Count} of the blade's 2 edge materials; the glow will be partial.");

            LogHurtBox(root, swordBounds, ToItem(new Vector3(0f, BladeRoot, 0f)), ToItem(new Vector3(0f, BladeTip, 0f)));
            Plugin.Log.LogInfo($"Katana from the sword: grip {grip}, axis {axis}, edge {side} (grip point {(grab != null ? "found" : "guessed")}, " +
                               $"swing point {(swing != null ? "found" : "guessed")}), sword size {swordBounds.size}.");
            return go;
        }

        /// <summary>The sword's wide direction across the blade (the second-longest side of its bounds), square to its length.</summary>
        private static Vector3 WidthAxis(Bounds b, Vector3 axis)
        {
            Vector3 s = b.size;
            Vector3[] dirs = { Vector3.right, Vector3.up, Vector3.forward };
            float[] len = { s.x, s.y, s.z };
            Vector3 best = Vector3.zero;
            float bestLen = -1f;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(Vector3.Dot(dirs[i], axis)) > 0.7f) continue;   // that's the length
                if (len[i] > bestLen) { bestLen = len[i]; best = dirs[i]; }
            }
            Vector3 w = Vector3.ProjectOnPlane(best, axis);
            if (w.sqrMagnitude < 1e-6f) w = Vector3.ProjectOnPlane(Vector3.forward, axis);
            if (w.sqrMagnitude < 1e-6f) w = Vector3.ProjectOnPlane(Vector3.right, axis);
            return w.normalized;
        }

        /// <summary>Logs the sword's grab colliders (kept as they are), in case the model needs lining up with them.</summary>
        private static void LogColliders(Transform root)
        {
            var log = new System.Text.StringBuilder("Katana: the sword's colliders (kept):");
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || c.GetComponent<HurtCollider>() != null) continue;
                string shape = c is BoxCollider b ? $"box centre {root.InverseTransformPoint(b.transform.TransformPoint(b.center))} size {Vector3.Scale(b.size, b.transform.lossyScale)}"
                             : c is CapsuleCollider k ? $"capsule r {k.radius} h {k.height}"
                             : c.GetType().Name;
                log.Append($"\n  '{c.name}' {shape}, enabled {c.enabled}");
            }
            Plugin.Log.LogInfo(log.ToString());
        }

        /// <summary>Logs where the sword's hurt box is, next to the katana's blade, in case the two need lining up.</summary>
        private static void LogHurtBox(Transform root, Bounds swordBounds, Vector3 bladeBase, Vector3 bladeTip)
        {
            HurtCollider hurt = root.GetComponentInChildren<HurtCollider>(true);
            if (hurt == null) { Plugin.Log.LogWarning("Katana: the sword has no hurt box; it won't do melee damage."); return; }
            // (the prefab is inactive, so collider bounds read zero: work it out from the box itself)
            BoxCollider c = hurt.GetComponent<BoxCollider>();
            string box = c != null
                ? $"centre {root.InverseTransformPoint(c.transform.TransformPoint(c.center))}, size {Vector3.Scale(c.size, c.transform.lossyScale)}"
                : "not a box";
            Plugin.Log.LogInfo($"Katana: the sword's hurt box ({box}) now does {hurt.enemyDamage} damage; " +
                               $"the katana's blade runs {bladeBase} -> {bladeTip}.");
        }

        private static void SetupItem(GameObject go)
        {
            ItemAttributes attributes = go.GetComponent<ItemAttributes>();
            Item source = attributes.item;
            Item item = Object.Instantiate(source);
            item.name = PrefabName;
            item.itemName = DisplayName;
            item.itemNameLocalized = null;
            item.description = "A razor-sharp katana. Press E to charge it, then slash through the air to tear open a rift: 2 seconds later it launches a cutting wave. Up to 3 rifts per charge.";

            // twice the Scythe's price; the Scythe is priced against the bat
            GameObject bat = Resources.Load<GameObject>(BatPath);
            ItemAttributes batAttributes = bat != null ? bat.GetComponent<ItemAttributes>() : null;
            Value basis = batAttributes != null && batAttributes.item != null && batAttributes.item.value != null ? batAttributes.item.value : source.value;
            if (basis != null)
            {
                float factor = Settings.ScythePriceMultiplier.Value * Settings.KatanaPriceVsScythe.Value;
                Value price = Object.Instantiate(basis);
                price.name = PrefabName + " Value";
                price.valueMin = basis.valueMin * factor;
                price.valueMax = basis.valueMax * factor;
                item.value = price;
            }
            attributes.item = item;
            attributes.icon = null;   // rendered by the game from the new model
        }
    }
}
