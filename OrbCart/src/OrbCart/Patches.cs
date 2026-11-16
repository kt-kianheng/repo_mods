using System;
using System.Collections.Generic;
using HarmonyLib;

namespace OrbCart
{
    [HarmonyPatch]
    internal static class Patches
    {
        /// <summary>
        /// The inside-box checker caches the component types it looks for in Start.
        /// For the Orb Box, replace that list with EnemyValuable so only enemy orbs are detected and absorbed,
        /// and drop the size filter (orbs come in small, medium and big).
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PhysGrabObjectComponentInsideBoxChecker), "Start")]
        private static void InsideBoxCheckerStart(PhysGrabObjectComponentInsideBoxChecker __instance, List<Type> ___cachedTypes)
        {
            if (__instance.GetComponent<OrbCartChecker>() == null || ___cachedTypes == null) return;
            ___cachedTypes.Clear();
            ___cachedTypes.Add(typeof(EnemyValuable));
            __instance.checkForValuableTypes = false;
        }
    }
}
