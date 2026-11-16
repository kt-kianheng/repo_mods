using HarmonyLib;
using UnityEngine;

namespace HatterValuablesPack
{
    /// <summary>
    /// Harmony patches for the A-Bomb:
    ///  - spawning: rare (ChancePerLevel, at most one per level, in a wide spot) and outside the level's value budget;
    ///  - heavy impacts arm it (host);
    ///  - it never loses value (it doesn't break, it blows up).
    /// </summary>
    [HarmonyPatch]
    internal static class NukePatches
    {
        // ---- per level (host) ----
        private static bool wantNuke;
        private static bool nukePlaced;

        /// <summary>A level's valuables are about to be placed: roll whether this level gets an A-Bomb.</summary>
        [HarmonyPatch(typeof(ValuableDirector), nameof(ValuableDirector.SetupHost))]
        [HarmonyPrefix]
        private static void SetupHostPrefix()
        {
            nukePlaced = false;
            wantNuke = NukeBuilder.Registered != null && Random.value < Settings.NukeChancePerLevel.Value;
            if (Settings.NukeDebugLog.Value)
                Plugin.Log.LogInfo($"A-Bomb: this level {(wantNuke ? "gets" : "doesn't get")} an A-Bomb (chance {Settings.NukeChancePerLevel.Value:P0}).");
        }

        private struct SpawnState
        {
            public bool isNuke;
            public int totalMaxAmount;
        }

        /// <summary>
        /// Each valuable the director places: the first wide spot of a lucky level gets the A-Bomb; any other time the
        /// director picks the A-Bomb (it's in the generic list, so it can be picked) a different wide valuable goes there.
        /// </summary>
        [HarmonyPatch(typeof(ValuableDirector), "SpawnValuable")]
        [HarmonyPrefix]
        private static bool SpawnValuablePrefix(ValuableDirector __instance, ref PrefabRef _valuable, ValuableVolume _volume,
                                                ref ValuableObject __result, out SpawnState __state)
        {
            __state = new SpawnState { totalMaxAmount = Traverse.Create(__instance).Field<int>("totalMaxAmount").Value };
            if (NukeBuilder.Registered == null) return true;
            bool wide = _volume != null && _volume.VolumeType == ValuableVolume.Type.Wide;
            if (wide && wantNuke && !nukePlaced)
            {
                nukePlaced = true;
                _valuable = NukeBuilder.Registered;
                __state.isNuke = true;
                Plugin.Log.LogInfo($"A-Bomb: placed in {(_volume.Module != null ? _volume.Module.name : "?")}.");
                return true;
            }
            if (!NukeBuilder.Is(_valuable)) return true;

            // the director picked the A-Bomb itself: swap it for another wide valuable
            var wideList = Traverse.Create(__instance).Field<System.Collections.Generic.List<PrefabRef>>("wideValuables").Value;
            var others = wideList?.FindAll(p => p != null && !NukeBuilder.Is(p));
            if (others == null || others.Count == 0) { __result = null; return false; }
            _valuable = others[Random.Range(0, others.Count)];
            return true;
        }

        /// <summary>The A-Bomb's value doesn't count towards the level's budget (and doesn't end the placing early).</summary>
        [HarmonyPatch(typeof(ValuableDirector), "SpawnValuable")]
        [HarmonyPostfix]
        private static void SpawnValuablePostfix(ValuableDirector __instance, ValuableObject __result, SpawnState __state)
        {
            if (!__state.isNuke || __result == null) return;
            var t = Traverse.Create(__instance);
            float dollars = Traverse.Create(__result).Field<float>("dollarValueCurrent").Value;
            t.Field<float>("totalCurrentValue").Value -= dollars * 0.001f;   // the director counts in thousands
            t.Field<int>("totalMaxAmount").Value = __state.totalMaxAmount;
            Plugin.Log.LogInfo($"A-Bomb: worth ${dollars:0}, kept out of the level's budget.");
        }

        /// <summary>Host: every heavy impact (the game: 150 and up) goes to the bomb's hit counter.</summary>
        [HarmonyPatch(typeof(PhysGrabObjectImpactDetector), nameof(PhysGrabObjectImpactDetector.ImpactHeavy))]
        [HarmonyPostfix]
        private static void ImpactHeavyPostfix(PhysGrabObjectImpactDetector __instance, float force)
        {
            Nuke nuke = __instance.GetComponentInParent<Nuke>();
            if (nuke != null) nuke.HostHeavyImpact(force);
        }

        /// <summary>The bomb never loses value from hits.</summary>
        [HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "Break")]
        [HarmonyPrefix]
        private static bool BreakPrefix(PhysGrabObjectImpactDetector __instance) => __instance.GetComponentInParent<Nuke>() == null;
    }
}
