using HarmonyLib;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>Diagnostics: logs every hit on an Alter Mimic (damage, health before, dead) on the host.</summary>
    [HarmonyPatch(typeof(EnemyHealth), nameof(EnemyHealth.Hurt))]
    internal static class MimicHealthProbe
    {
        private static void Prefix(EnemyHealth __instance, int _damage)
        {
            if (__instance == null || __instance.GetComponent<AlterMimic>() == null) return;
            R.Info($"Alter Mimic: hit for {_damage} ({__instance.health} max, dead {R.EnemyDead(__instance)}).");
        }
    }

    /// <summary>
    /// Every game: the game's hurt flash only tints the enemy's own model (the Alter Mimic's is the hidden Hidden), so
    /// HurtRPC and DeathRPC (which run on every game) also flash the semibot's body.
    /// </summary>
    [HarmonyPatch]
    internal static class MimicHurtVisuals
    {
        [HarmonyPostfix, HarmonyPatch(typeof(EnemyHealth), nameof(EnemyHealth.HurtRPC))]
        private static void Hurt(EnemyHealth __instance)
        {
            AlterMimic m = __instance != null ? __instance.GetComponent<AlterMimic>() : null;
            if (m != null) m.HurtVisual();
        }

        [HarmonyPostfix, HarmonyPatch(typeof(EnemyHealth), nameof(EnemyHealth.DeathRPC))]
        private static void Death(EnemyHealth __instance)
        {
            AlterMimic m = __instance != null ? __instance.GetComponent<AlterMimic>() : null;
            if (m != null) m.HurtVisual();
        }
    }
}
