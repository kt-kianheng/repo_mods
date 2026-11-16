using System;
using System.Collections.Generic;
using HarmonyLib;

namespace HatterEnemiesPack
{
    /// <summary>
    /// Diagnostics: the game's PhysGrabObject.OverrideTimersTick threw a NullReferenceException every frame during
    /// the first Alter Mimic test. This logs which object it is (once per object, with its parts) so it can be fixed.
    /// The error is passed on unchanged.
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabObject), "OverrideTimersTick")]
    internal static class GrabErrorProbe
    {
        private static readonly HashSet<int> reported = new HashSet<int>();

        private static Exception Finalizer(Exception __exception, PhysGrabObject __instance)
        {
            if (__exception == null || __instance == null) return __exception;
            if (reported.Add(__instance.GetInstanceID()))
            {
                var parts = new List<string>();
                foreach (UnityEngine.Component c in __instance.GetComponents<UnityEngine.Component>()) if (c != null) parts.Add(c.GetType().Name);
                bool ours = __instance.GetComponentInParent<EnemyParent>() != null && __instance.GetComponentInParent<EnemyParent>().enemyName == MimicBuilder.DisplayName;
                Plugin.Log.LogWarning($"Alter Mimic (diagnostics): OverrideTimersTick failed on '{__instance.transform.root.name}/{__instance.name}' ({(ours ? "the Alter Mimic" : "not the mimic")}): {string.Join(", ", parts)}. {__exception.GetType().Name}: {__exception.StackTrace?.Split('\n')[0]}");
            }
            return __exception;
        }
    }
}
