using HarmonyLib;
using Photon.Pun;

namespace HatterWeaponsPack
{
    [HarmonyPatch]
    internal static class Patches
    {
        /// <summary>
        /// ItemGun.ShootRPC runs on every client when the host fires a gun. For our weapons, launch the projectile
        /// there (the guns have no bullets, so the pellet logic does nothing).
        /// </summary>
        /// <summary>The Plasma Rifle shows its own blue glow instead of the shotgun's muzzle blast.</summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ItemGun), "MuzzleFlash")]
        private static bool MuzzleFlashPrefix(ItemGun __instance)
        {
            if (__instance.GetComponent<ChargedWand>() != null) return false;   // wands have their own effects
            if (__instance.GetComponent<OrbCannon>() != null) return false;     // so does the Orb Cannon
            if (__instance.GetComponent<PlasmaRifle>() == null) return true;
            try { PlasmaMuzzleGlow.Spawn(__instance.gunMuzzle != null ? __instance.gunMuzzle : __instance.transform); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Plasma muzzle glow failed: {e}"); }
            return false;
        }

        /// <summary>
        /// Wands: a trigger pull starts the charge instead of firing. When the charge is done the wand calls
        /// Shoot again with Releasing set, and the game's normal shot runs.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ItemGun), nameof(ItemGun.Shoot))]
        private static bool ShootPrefix(ItemGun __instance, ref bool __result)
        {
            // Orb Cannon: the trigger loads an orb when empty, fires when loaded, does nothing while cooling
            OrbCannon cannon = __instance.GetComponent<OrbCannon>();
            if (cannon != null)
            {
                if (cannon.HostTrigger()) return true;   // firing: the game's shot runs (sound, kick)
                __result = true;
                return false;
            }
            ChargedWand wand = __instance.GetComponent<ChargedWand>();
            if (wand == null || wand.Releasing) return true;
            if (!wand.HostTriggerPulled()) return true;   // empty battery: let the game dry-fire
            __result = true;
            return false;
        }

        private static readonly System.Reflection.FieldInfo SpotBatteryField = AccessTools.Field(typeof(InventorySpot), "batteryVisualLogic");

        /// <summary>
        /// The Orb and Friendship Cannons use no battery: their inventory slot shows none. (InventorySpot.StateOccupied turns
        /// the slot's battery on every frame for any item with an ItemBattery; this turns it straight back off.)
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventorySpot), "StateOccupied")]
        private static void InventorySpotBatteryPostfix(InventorySpot __instance)
        {
            try
            {
                ItemEquippable item = __instance.CurrentItem;
                if (item == null || item.GetComponent<OrbCannon>() == null) return;
                var logic = SpotBatteryField?.GetValue(__instance) as BatteryVisualLogic;
                if (logic != null && logic.gameObject.activeSelf) logic.gameObject.SetActive(false);
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ItemGun), nameof(ItemGun.ShootRPC))]
        private static void ShootRPCPostfix(ItemGun __instance, PhotonMessageInfo _info)
        {
            RocketLauncher launcher = __instance.GetComponent<RocketLauncher>();
            PlasmaRifle rifle = __instance.GetComponent<PlasmaRifle>();
            ChargedWand wand = __instance.GetComponent<ChargedWand>();
            if (launcher == null && rifle == null && wand == null) return;
            if (!SemiFunc.MasterOnlyRPC(_info)) return;   // same guard as the original method
            if (launcher != null) launcher.OnShot();
            else if (rifle != null) rifle.OnShot();
            else wand.OnShot();
        }
    }
}
