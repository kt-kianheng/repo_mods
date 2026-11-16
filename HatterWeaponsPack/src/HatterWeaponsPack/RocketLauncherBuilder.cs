using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Builds the Rocket Launcher prefab at startup from the game's shotgun (see WeaponBuilder), swaps in the
    /// Blender model, and fires rockets (RocketLauncher) instead of pellets.
    /// </summary>
    internal static class RocketLauncherBuilder
    {
        internal const string PrefabName = "Item Gun Rocket Launcher";
        internal const string DisplayName = "Rocket Launcher";
        private const string ModelFile = "rocket_launcher.mesh";

        // ---- Tuning (HatterWeaponsPack.cfg, see Settings) ----
        internal static int RocketsPerCharge => Settings.RocketsPerCharge.Value;
        internal static float ReloadSeconds => Settings.RocketReloadSeconds.Value;   // from the shot until the next rocket is ready
        private const float ShootStateSeconds = 0.25f;     // part of the reload: the gun's short "shooting" state
        internal static float PriceMultiplier => Settings.RocketPriceMultiplier.Value;   // relative to the vanilla shotgun

        // Rocket centre in the loaded position (launcher space), from the Blender export.
        internal static readonly Vector3 LoadedRocketCenter = new Vector3(0f, 0f, 0.1264f);
        private static readonly Vector3 MuzzlePosition = new Vector3(0f, 0f, 0.42f);

        internal static GameObject Build()
        {
            MeshLibrary.Model launcherModel = MeshLibrary.Get(ModelFile, "RPG_Launcher");
            MeshLibrary.Model rocketModel = MeshLibrary.Get(ModelFile, "RPG_Rocket");
            if (launcherModel == null || rocketModel == null) { Plugin.Log.LogError("Rocket launcher model missing from the DLL."); return null; }

            GameObject go = WeaponBuilder.CloneShotgun(PrefabName);
            if (go == null) return null;
            Transform root = go.transform;

            WeaponBuilder.SetupItem(go, PrefabName, DisplayName,
                $"{RocketsPerCharge} rockets per charge. Direct hits deal {RocketProjectile.ContactDamage} plus the blast: " +
                $"{RocketExplosion.CloseDamage} up close, {RocketExplosion.EdgeDamage} at the edge. " +
                "Direct hits and the blast hurt teammates too (and you). Kicks back hard.",
                PriceMultiplier);
            SetupGun(go);

            // Visuals: the launcher and the loaded rocket head.
            Material template = WeaponBuilder.HideShotgunMesh(root);
            GameObject visuals = new GameObject("Rocket Launcher Visuals");
            visuals.transform.SetParent(root, false);
            visuals.layer = go.layer;
            MeshLibrary.Attach(launcherModel, visuals.transform, "Launcher", template);
            GameObject head = MeshLibrary.Attach(rocketModel, visuals.transform, "Rocket Head", template);

            WeaponBuilder.FitColliders(root, new (Vector3, Vector3)[]
            {
                (new Vector3(0f, 0f, -0.206f), new Vector3(0.10f, 0.10f, 0.69f)),   // tube + flare
                (new Vector3(0f, 0f, 0.27f), new Vector3(0.10f, 0.10f, 0.26f)),     // warhead
                (new Vector3(0f, -0.106f, -0.014f), new Vector3(0.04f, 0.115f, 0.05f)), // pistol grip
                (new Vector3(0f, -0.081f, -0.128f), new Vector3(0.04f, 0.085f, 0.046f)), // rear grip
            });
            WeaponBuilder.SetForceGrabPoint(root, new Vector3(0f, -0.09f, -0.02f));

            // Shared by every spawned launcher. (Only serialized fields survive when the game instantiates the
            // prefab, so non-serializable data like the model must live here rather than on the component.)
            RocketModel = rocketModel;
            MaterialTemplate = template;

            var launcher = go.AddComponent<RocketLauncher>();
            launcher.rocketHead = head;   // a public GameObject reference: Unity serializes and remaps it per instance

            return go;
        }

        internal static MeshLibrary.Model RocketModel { get; private set; }
        internal static Material MaterialTemplate { get; private set; }

        private static void SetupGun(GameObject go)
        {
            ItemGun gun = go.GetComponent<ItemGun>();
            gun.numberOfBullets = 0;                         // no pellets: RocketLauncher fires a rocket instead
            gun.gunRandomSpread = 0f;
            gun.shootTime = ShootStateSeconds;
            gun.shootCooldown = Mathf.Max(0.05f, ReloadSeconds - ShootStateSeconds);
            gun.batteryDrainFullBar = true;                  // one battery bar per rocket...
            gun.batteryDrainFullBars = 1;
            gun.misfirePercentageChange = 0f;                // never fire by accident when knocked around
            gun.gunRecoilForce = 30f;                        // kick on the launcher itself
            gun.cameraShakeMultiplier = 4f;
            if (gun.gunMuzzle != null) gun.gunMuzzle.localPosition = MuzzlePosition;

            ItemBattery battery = go.GetComponent<ItemBattery>();
            if (battery != null) battery.batteryBars = RocketsPerCharge;   // ...and that many bars in a full charge
        }
    }
}
