using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>Builds the Plasma Rifle prefab at startup from the game's shotgun (see WeaponBuilder).</summary>
    internal static class PlasmaRifleBuilder
    {
        internal const string PrefabName = "Item Gun Plasma Rifle";
        internal const string DisplayName = "Plasma Rifle";
        private const string ModelFile = "plasma_rifle.mesh";
        private const string ShotSoundFile = "plasma_shot.pcm";   // "Sci-Fi Weapon Laser Shot 04" by DavidDumaisAudio (Pixabay, per the file name)

        // ---- Tuning ----
        // (HatterWeaponsPack.cfg, see Settings)
        internal static int ShotsPerCharge => Settings.PlasmaShotsPerCharge.Value;
        private static float TimeBetweenShots => Settings.PlasmaTimeBetweenShots.Value;   // no reload, just this gap
        private static float PriceMultiplier => Settings.PlasmaPriceMultiplier.Value;

        // Tip of the upper barrel (rifle space), from the Blender export.
        internal static readonly Vector3 MuzzlePosition = new Vector3(0f, 0.036f, 0.53f);

        internal static GameObject Build()
        {
            MeshLibrary.Model model = MeshLibrary.Get(ModelFile, "Plasma_Rifle");
            if (model == null) { Plugin.Log.LogError("Plasma rifle model missing from the DLL."); return null; }

            GameObject go = WeaponBuilder.CloneShotgun(PrefabName);
            if (go == null) return null;
            Transform root = go.transform;

            WeaponBuilder.SetupItem(go, PrefabName, DisplayName,
                $"{ShotsPerCharge} plasma bolts per charge, {PlasmaBolt.Damage} damage each, no reload. " +
                "Bolts fly fast but not instantly. Direct hits hurt teammates too.",
                PriceMultiplier);
            SetupGun(go);

            Material template = WeaponBuilder.HideShotgunMesh(root);
            GameObject visuals = new GameObject("Plasma Rifle Visuals");
            visuals.transform.SetParent(root, false);
            visuals.layer = go.layer;
            MeshLibrary.Attach(model, visuals.transform, "Rifle", template);

            WeaponBuilder.FitColliders(root, new (Vector3, Vector3)[]
            {
                (new Vector3(0f, 0.009f, 0.047f), new Vector3(0.06f, 0.104f, 0.315f)),   // receiver + front block
                (new Vector3(0f, 0.011f, 0.362f), new Vector3(0.045f, 0.086f, 0.316f)),   // twin barrels
                (new Vector3(0f, -0.018f, -0.193f), new Vector3(0.042f, 0.08f, 0.165f)),  // stock
                (new Vector3(0f, -0.077f, -0.03f), new Vector3(0.03f, 0.095f, 0.04f)),    // grip
            });
            WeaponBuilder.SetForceGrabPoint(root, new Vector3(0f, -0.07f, -0.03f));

            go.AddComponent<PlasmaRifle>();
            return go;
        }

        private static void SetupGun(GameObject go)
        {
            ItemGun gun = go.GetComponent<ItemGun>();
            gun.numberOfBullets = 0;                         // no pellets: PlasmaRifle fires a bolt instead
            gun.gunRandomSpread = 0f;
            // the gap between shots is the gun's short "shooting" state plus its cooldown
            float shootState = Mathf.Min(0.1f, TimeBetweenShots * 0.4f);
            gun.shootTime = shootState;
            gun.shootCooldown = Mathf.Max(0.01f, TimeBetweenShots - shootState);
            gun.batteryDrainFullBar = true;                  // one battery bar per shot...
            gun.batteryDrainFullBars = 1;
            gun.misfirePercentageChange = 0f;
            gun.gunRecoilForce = 6f;                         // light kick on the rifle
            gun.cameraShakeMultiplier = 0.6f;
            if (gun.gunMuzzle != null) gun.gunMuzzle.localPosition = MuzzlePosition;

            // laser shot sound instead of the shotgun blast (near and distant versions both use it)
            AudioClip shot = SoundLibrary.Get(ShotSoundFile);
            if (shot != null)
            {
                if (gun.soundShoot != null) gun.soundShoot.Sounds = new[] { shot };
                if (gun.soundShootGlobal != null) { gun.soundShootGlobal.Sounds = new[] { shot }; gun.soundShootGlobal.Volume *= 0.5f; }
            }

            ItemBattery battery = go.GetComponent<ItemBattery>();
            if (battery != null) battery.batteryBars = ShotsPerCharge;   // ...and that many bars in a full charge
        }
    }
}
