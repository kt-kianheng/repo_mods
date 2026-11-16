using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>Builds the wand prefabs (Ember Wand, Frost Wand) at startup from the game's shotgun (see WeaponBuilder).</summary>
    internal static class WandBuilder
    {
        internal const string EmberName = "Ember Wand";
        internal const string FrostName = "Frost Wand";
        internal const string HealName = "Healing Wand";
        internal const string LightningName = "Lightning Wand";
        internal const string EmberStaffName = "Ember Staff";
        internal const string FrostStaffName = "Frost Staff";
        internal const string LightningStaffName = "Lightning Staff";

        /// <summary>Where a weapon's tip is, how its grab boxes sit and where it is held (weapon space, Unity axes).</summary>
        internal class Shape
        {
            public Vector3 Tip;
            public (Vector3 centre, Vector3 size)[] Colliders;
            public Vector3 GrabPoint;
        }

        /// <summary>The wands (all the same body shape).</summary>
        internal static readonly Shape WandShape = new Shape
        {
            Tip = new Vector3(0f, 0f, 0.245f),
            Colliders = new[]
            {
                (new Vector3(0f, 0f, -0.078f), new Vector3(0.036f, 0.036f, 0.16f)),   // handle + pommel
                (new Vector3(0f, 0f, 0.128f), new Vector3(0.026f, 0.026f, 0.255f)),   // shaft + tip
            },
            GrabPoint = new Vector3(0f, 0f, -0.07f),
        };

        /// <summary>
        /// The staves (tools/build_staves.py): about 1.32 m long, foot at z = -0.415, held at the origin, the spiral branch
        /// round the crystal from z = 0.60 to 0.89 (up to 9 cm out from the axis), crystal centre at z = 0.75.
        /// </summary>
        internal static readonly Shape StaffShape = new Shape
        {
            Tip = new Vector3(0f, 0f, 0.75f),
            Colliders = new[]
            {
                (new Vector3(0f, 0f, 0.09f), new Vector3(0.055f, 0.055f, 1.0f)),      // foot to the head
                (new Vector3(0f, 0f, 0.74f), new Vector3(0.18f, 0.18f, 0.32f)),       // the spiral branch round the crystal
            },
            GrabPoint = Vector3.zero,
        };

        /// <summary>The Ember Wand's launch sound, for anything else that throws its fireballs.</summary>
        internal static AudioClip WhooshClip { get; private set; }


        private const float ShootStateSeconds = 0.1f;

        // Tip crystal centre (wand space), from the Blender export. All the wands share the same body shape.
        internal static Vector3 TipPosition => WandShape.Tip;

        // Shared with every spawned Frost Wand (only serialized fields survive the game's Instantiate).
        internal static MeshLibrary.Model IcicleModel { get; private set; }
        internal static Material MaterialTemplate { get; private set; }

        internal static GameObject BuildEmber()
        {
            GameObject go = Build<EmberWand>("Item Gun Ember Wand", EmberName, "ember_wand.mesh", "Ember_Wand", "Ember_Tip",
                $"Charge up and hurl a fireball: {Settings.EmberFireballsPerCharge.Value} per charge, {Fireball.DirectDamage} on a direct hit " +
                $"plus {Settings.EmberSplashDamage.Value} splash. Burns everyone it touches, you included.",
                Settings.EmberPriceMultiplier.Value, Settings.EmberFireballsPerCharge.Value, Settings.EmberReloadSeconds.Value, Whoosh());
            return go;
        }

        internal static GameObject BuildFrost()
        {
            IcicleModel = MeshLibrary.Get("frost_wand.mesh", "Icicle");
            if (IcicleModel == null) { Plugin.Log.LogError("Icicle model missing from the DLL."); return null; }
            int count = Settings.FrostIcicleCount.Value;
            // the reload runs from the trigger, so it covers the whole volley plus ReloadSeconds after the last icicle flies
            float reload = FrostVolleySeconds + Settings.FrostReloadSeconds.Value;
            return Build<FrostWand>("Item Gun Frost Wand", FrostName, "frost_wand.mesh", "Frost_Wand", "Frost_Tip",
                $"Conjure {count} icicle{(count == 1 ? "" : "s")} one after another round the tip, each flying at your crosshair a moment after it forms: " +
                $"{Settings.FrostIcicleDamage.Value} damage each, and every hit freezes the enemy in place for {Settings.FrostStunSeconds.Value:0.#} s. " +
                $"{Settings.FrostCastsPerCharge.Value} casts per charge. They hurt anyone they hit.",
                Settings.FrostPriceMultiplier.Value, Settings.FrostCastsPerCharge.Value, reload,
                MakeClip("HWP Silence", new float[64]));   // FrostWand plays the flying sound with each icicle
        }

        /// <summary>From the trigger until the last icicle flies.</summary>
        internal static float FrostVolleySeconds =>
            (Mathf.Max(1, Settings.FrostIcicleCount.Value) - 1) * Settings.FrostIcicleInterval.Value + Settings.FrostLaunchDelay.Value;

        internal static GameObject BuildHealing()
        {
            return Build<HealingWand>("Item Gun Healing Wand", HealName, "healing_wand.mesh", "Heal_Wand", "Heal_Tip",
                $"Aim at a teammate for a healing beam ({Settings.HealBeamPerSecond.Value} health a second for {Settings.HealBeamSeconds.Value:0} s), " +
                $"or point it at the sky for a healing orb ({Settings.HealAreaPerSecond.Value} a second to everyone within {Settings.HealAreaRadius.Value:0.#} m). " +
                $"{Settings.HealCharges.Value} casts per charge.",
                Settings.HealPriceMultiplier.Value, Settings.HealCharges.Value, Settings.HealReloadSeconds.Value,
                MakeClip("HWP Silence", new float[64]));   // the chimes are played by HealingWand (one per spell)
        }

        internal static GameObject BuildLightning()
        {
            int charges = Mathf.Max(1, Settings.LightningChargesPerBolt.Value);
            int targets = Mathf.Clamp(Settings.LightningMaxTargets.Value, 1, charges);
            return Build<LightningWand>("Item Gun Lightning Wand", LightningName, "lightning_wand.mesh", "Lightning_Wand", "Lightning_Tip",
                $"Call down a bolt of lightning that jumps between up to {targets} enemies. Each one is electrocuted and stunned for " +
                $"{Settings.LightningStunSeconds.Value:0.#} s, then takes {Settings.LightningDamagePerCharge.Value} damage per charge, " +
                $"up to {Settings.LightningDamagePerCharge.Value * Mathf.Max(1, Settings.LightningMaxDamageCharges.Value)} " +
                $"({charges} charges per bolt, shared between them). {Settings.LightningCasts.Value} casts per charge.",
                Settings.LightningPriceMultiplier.Value, Settings.LightningCasts.Value, Settings.LightningReloadSeconds.Value,
                MakeClip("HWP Silence", new float[64]));   // LightningWand times its cast sound to end as the bolt fires
        }

        /// <summary>
        /// Ember Staff, the Ember Wand's upgrade: a fireball volley, or (armed by pointing at the sky) an inferno.
        /// It's not stocked by the shop: it comes from using an Evolution Crystal on an Ember Wand.
        /// </summary>
        internal static GameObject BuildEmberStaff()
        {
            if (WhooshClip == null) WhooshClip = Whoosh();
            int volley = Mathf.Max(1, Settings.StaffFireballs.Value);
            GameObject go = Build<EmberStaff>("Item Gun Ember Staff", EmberStaffName, "ember_staff.mesh", "EmberStaff_Body", "EmberStaff_Tip",
                $"The Ember Wand, upgraded. Hurls {volley} fireballs one after another. Point it at the sky to ready an inferno, " +
                $"then aim at the floor and cast: a pillar of fire reaching the ceiling for {Settings.StaffInfernoSeconds.Value:0} s, " +
                $"{Settings.StaffInfernoEnemyDps.Value} damage a second to enemies and {Settings.StaffInfernoAllyDps.Value} to players.",
                StaffPrice(Settings.EmberPriceMultiplier.Value), Settings.StaffCasts.Value, Settings.StaffReloadSeconds.Value,
                MakeClip("HWP Silence", new float[64]),   // EmberStaff plays the whoosh with each fireball
                StaffShape);
            UpgradeOnly(go, EmberName);
            return go;
        }

        /// <summary>
        /// Frost Staff, the Frost Wand's upgrade: a bigger, faster icicle volley, or (readied by pointing at the sky) an ice
        /// wall. Like the Ember Staff it comes only from evolving its wand.
        /// </summary>
        internal static GameObject BuildFrostStaff()
        {
            if (IcicleModel == null) IcicleModel = MeshLibrary.Get("frost_wand.mesh", "Icicle");
            int count = Mathf.Max(1, Settings.FrostStaffIcicles.Value), together = Mathf.Clamp(Settings.FrostStaffIciclesAtOnce.Value, 1, count);
            int sets = (count + together - 1) / together;
            // like the Frost Wand, the reload runs from the trigger and covers the whole volley
            float volley = (sets - 1) * Settings.FrostStaffInterval.Value + Settings.FrostStaffLaunchDelay.Value;
            GameObject go = Build<FrostStaff>("Item Gun Frost Staff", FrostStaffName, "frost_staff.mesh", "FrostStaff_Body", "FrostStaff_Tip",
                $"The Frost Wand, upgraded. Conjures {count} icicles, {together} at a time, that stream at your crosshair: {Settings.FrostStaffIcicleDamage.Value} damage each, freezing what they hit. " +
                $"Point it at the sky to ready an ice wall, then aim at the floor and cast: a {Settings.FrostStaffWallLength.Value:0.#} m wall of ice, " +
                $"{Settings.FrostStaffWallHeight.Value:0.#} m tall, that nothing gets through for {Settings.FrostStaffWallSeconds.Value:0} s. " +
                $"Enemies it rises under take {Settings.FrostStaffWallImpactDamage.Value} damage and are frozen inside it.",
                StaffPrice(Settings.FrostPriceMultiplier.Value), Settings.FrostStaffBars.Value, volley + Settings.FrostStaffReloadSeconds.Value,
                MakeClip("HWP Silence", new float[64]),   // FrostWand plays the flying sound with each icicle
                StaffShape);
            UpgradeOnly(go, FrostName);
            return go;
        }

        /// <summary>
        /// Lightning Staff, the Lightning Wand's upgrade: three homing chain strikes, or (readied by pointing at the sky) a
        /// bouncing lightning orb. Like the other staves it comes only from evolving its wand.
        /// </summary>
        internal static GameObject BuildLightningStaff()
        {
            int strikes = Mathf.Max(1, Settings.LStaffStrikes.Value);
            GameObject go = Build<LightningStaff>("Item Gun Lightning Staff", LightningStaffName, "lightning_staff.mesh", "LightningStaff_Body", "LightningStaff_Tip",
                $"The Lightning Wand, upgraded. Channel, then {strikes} lightning strikes seek out the enemies on your screen and chain between them, " +
                $"hurting on impact (up to {Settings.LStaffChargeDamage.Value * Mathf.Max(1, Settings.LStaffMaxDamageCharges.Value)} each) and stunning. " +
                $"Point it at the sky to ready a lightning orb: it drifts where you aim, bouncing off walls, striking nearby enemies every " +
                $"{Settings.LStaffOrbStrikeInterval.Value:0.#} s for {Settings.LStaffOrbSeconds.Value:0} s.",
                StaffPrice(Settings.LightningPriceMultiplier.Value), Settings.LStaffBars.Value, Settings.LStaffReloadSeconds.Value,
                MakeClip("HWP Silence", new float[64]),   // LightningWand times its cast sound to end as the channel does
                StaffShape);
            UpgradeOnly(go, LightningName);
            return go;
        }

        /// <summary>A staff's list price (x the handgun): its wand's, times StaffPriceVsWand.</summary>
        private static float StaffPrice(float wandVsGun) => wandVsGun * Settings.CrystalStaffPriceVsWand.Value;

        /// <summary>
        /// Never stocked by the shop (an Evolution Crystal makes one from its wand); owned staves still come back each
        /// level like any bought item. Records which wand evolves into it.
        /// </summary>
        private static void UpgradeOnly(GameObject staff, string wandDisplayName)
        {
            if (staff == null) return;
            Item item = staff.GetComponent<ItemAttributes>().item;
            item.maxAmountInShop = 0;
            EvolutionCrystal.Evolutions["Item Gun " + wandDisplayName] = item.name;
        }

        private static GameObject Build<T>(string prefabName, string displayName, string modelFile, string bodyName, string tipName,
                                           string description, float price, int charges, float reload, AudioClip sound,
                                           Shape shape = null) where T : ChargedWand
        {
            shape = shape ?? WandShape;
            MeshLibrary.Model body = MeshLibrary.Get(modelFile, bodyName);
            MeshLibrary.Model tip = MeshLibrary.Get(modelFile, tipName);
            if (body == null || tip == null) { Plugin.Log.LogError($"{displayName} model missing from the DLL."); return null; }

            GameObject go = WeaponBuilder.CloneShotgun(prefabName);
            if (go == null) return null;
            Transform root = go.transform;

            WeaponBuilder.SetupItem(go, prefabName, displayName, description, price, WeaponBuilder.HandgunPath);   // priced like the gun
            SetupGun(go, charges, reload, sound, shape.Tip);

            Material template = WeaponBuilder.HideShotgunMesh(root);
            if (MaterialTemplate == null) MaterialTemplate = template;
            GameObject visuals = new GameObject(displayName + " Visuals");
            visuals.transform.SetParent(root, false);
            visuals.layer = go.layer;
            MeshLibrary.Attach(body, visuals.transform, "Wand", template);
            GameObject crystal = MeshLibrary.Attach(tip, visuals.transform, "Tip Crystal", template);

            WeaponBuilder.FitColliders(root, shape.Colliders);
            WeaponBuilder.SetForceGrabPoint(root, shape.GrabPoint);

            var wand = go.AddComponent<T>();
            wand.tipCrystal = crystal;   // a public GameObject reference: Unity remaps it per spawned wand
            return go;
        }

        private static void SetupGun(GameObject go, int charges, float reload, AudioClip sound, Vector3 tip)
        {
            ItemGun gun = go.GetComponent<ItemGun>();
            gun.numberOfBullets = 0;                         // no pellets: the wand launches its own projectiles
            gun.gunRandomSpread = 0f;
            gun.hasOneShot = true;                           // one shot per trigger pull (the charge-up is the wand's)
            gun.hasBuildUp = false;
            gun.shootTime = ShootStateSeconds;
            gun.shootCooldown = Mathf.Max(0.05f, reload - ShootStateSeconds);
            gun.batteryDrainFullBar = true;                  // one battery bar per cast
            gun.batteryDrainFullBars = 1;
            gun.misfirePercentageChange = 0f;
            gun.gunRecoilForce = 2f;
            gun.cameraShakeMultiplier = 0.3f;
            if (gun.gunMuzzle != null) gun.gunMuzzle.localPosition = tip;

            if (gun.soundShoot != null) gun.soundShoot.Sounds = new[] { sound };
            if (gun.soundShootGlobal != null) { gun.soundShootGlobal.Sounds = new[] { sound }; gun.soundShootGlobal.Volume *= 0.4f; }

            ItemBattery battery = go.GetComponent<ItemBattery>();
            if (battery != null) battery.batteryBars = charges;
        }

        private static AudioClip MakeClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, 44100, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return clip;
        }

        /// <summary>Ember Wand launch: filtered noise that swells and fades, with a low rumble.</summary>
        private static AudioClip Whoosh()
        {
            int n = (int)(44100 * 0.7f);
            var data = new float[n];
            var rng = new System.Random(1234);
            float low = 0f, rumble = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += Mathf.Lerp(0.25f, 0.05f, t) * (white - low);   // bright at launch, darker as it flies off
                rumble += 0.01f * (white - rumble);
                float env = Mathf.Min(1f, t / 0.06f) * Mathf.Pow(1f - t, 1.6f);
                data[i] = Mathf.Clamp((low * 2.2f + rumble * 6f) * env, -1f, 1f);
            }
            WhooshClip = MakeClip("HWP Fireball Whoosh", data);
            return WhooshClip;
        }
    }
}
