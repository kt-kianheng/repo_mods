using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HatterValuablesPack
{
    /// <summary>
    /// Finding out why the compasses don't break / vanish / show their value: every error the game's grab, impact and
    /// value code throws on a compass is logged (once per method, with where it came from), and while a compass is held
    /// its state is logged every 2 s (value, dead, indestructible, fragility, durability, what the grabber sees).
    /// </summary>
    [HarmonyPatch]
    internal static class CompassDiagnostics
    {
        private static readonly HashSet<string> seen = new HashSet<string>();
        private static float nextState;

        private static bool IsCompass(Component c) => c != null && c.GetComponentInParent<Compass>() != null;

        private static void Report(string where, Exception e)
        {
            if (e == null || !seen.Add(where + e.GetType().Name)) return;
            Plugin.Log.LogError($"Compass diagnostics: {where} threw on a compass: {e}");
        }

        [HarmonyFinalizer, HarmonyPatch(typeof(PhysGrabObject), "Update")]
        private static Exception PgoUpdate(PhysGrabObject __instance, Exception __exception)
        { if (__exception != null && IsCompass(__instance)) Report("PhysGrabObject.Update", __exception); return __exception; }

        [HarmonyFinalizer, HarmonyPatch(typeof(PhysGrabObject), "FixedUpdate")]
        private static Exception PgoFixed(PhysGrabObject __instance, Exception __exception)
        { if (__exception != null && IsCompass(__instance)) Report("PhysGrabObject.FixedUpdate", __exception); return __exception; }

        [HarmonyFinalizer, HarmonyPatch(typeof(PhysGrabObject), "Start")]
        private static Exception PgoStart(PhysGrabObject __instance, Exception __exception)
        { if (__exception != null && IsCompass(__instance)) Report("PhysGrabObject.Start", __exception); return __exception; }

        [HarmonyFinalizer, HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "Start")]
        private static Exception IdStart(PhysGrabObjectImpactDetector __instance, Exception __exception)
        { if (__exception != null && IsCompass(__instance)) Report("ImpactDetector.Start", __exception); return __exception; }

        [HarmonyFinalizer, HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "Update")]
        private static Exception IdUpdate(PhysGrabObjectImpactDetector __instance, Exception __exception)
        { if (__exception != null && IsCompass(__instance)) Report("ImpactDetector.Update", __exception); return __exception; }

        [HarmonyFinalizer, HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "FixedUpdate")]
        private static Exception IdFixed(PhysGrabObjectImpactDetector __instance, Exception __exception)
        { if (__exception != null && IsCompass(__instance)) Report("ImpactDetector.FixedUpdate", __exception); return __exception; }

        [HarmonyFinalizer, HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "OnCollisionStay")]
        private static Exception IdCollision(PhysGrabObjectImpactDetector __instance, Exception __exception)
        { if (__exception != null && IsCompass(__instance)) Report("ImpactDetector.OnCollisionStay", __exception); return __exception; }

        [HarmonyFinalizer, HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "Break")]
        private static Exception IdBreak(PhysGrabObjectImpactDetector __instance, Exception __exception)
        { if (__exception != null && IsCompass(__instance)) Report("ImpactDetector.Break", __exception); return __exception; }

        [HarmonyFinalizer, HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "DestroyObject")]
        private static Exception IdDestroy(PhysGrabObjectImpactDetector __instance, Exception __exception)
        { if (__exception != null && IsCompass(__instance)) Report("ImpactDetector.DestroyObject", __exception); return __exception; }

        [HarmonyFinalizer, HarmonyPatch(typeof(ValuableObject), "Start")]
        private static Exception VoStart(ValuableObject __instance, Exception __exception)
        { if (__exception != null && IsCompass(__instance)) Report("ValuableObject.Start", __exception); return __exception; }

        [HarmonyFinalizer, HarmonyPatch(typeof(ValuableObject), "DollarValueSetLogic")]
        private static Exception VoValue(ValuableObject __instance, Exception __exception)
        { if (__exception != null && IsCompass(__instance)) Report("ValuableObject.DollarValueSetLogic", __exception); return __exception; }

        [HarmonyFinalizer, HarmonyPatch(typeof(PhysGrabber), "ShowValue")]
        private static Exception ShowValue(PhysGrabber __instance, Exception __exception)
        {
            PhysGrabObject held = __instance != null ? Traverse.Create(__instance).Field("grabbedPhysGrabObject").GetValue<PhysGrabObject>() : null;
            if (__exception != null && IsCompass(held)) Report("PhysGrabber.ShowValue", __exception);
            return __exception;
        }

        [HarmonyFinalizer, HarmonyPatch(typeof(PhysGrabber), "Update")]
        private static Exception GrabberUpdate(PhysGrabber __instance, Exception __exception)
        {
            PhysGrabObject held = __instance != null ? Traverse.Create(__instance).Field("grabbedPhysGrabObject").GetValue<PhysGrabObject>() : null;
            if (__exception != null && IsCompass(held)) Report("PhysGrabber.Update", __exception);
            if (held != null && IsCompass(held) && Time.unscaledTime >= nextState
                && Traverse.Create(__instance).Field("isLocal").GetValue<bool>())
            {
                nextState = Time.unscaledTime + 2f;
                try { LogState(__instance, held); } catch (Exception e) { Plugin.Log.LogWarning($"Compass diagnostics: state log failed: {e.Message}"); }
            }
            return __exception;
        }

        private static void LogState(PhysGrabber grabber, PhysGrabObject pgo)
        {
            var v = pgo.GetComponent<ValuableObject>();
            var d = pgo.GetComponent<PhysGrabObjectImpactDetector>();
            var tv = Traverse.Create(v); var td = Traverse.Create(d); var tp = Traverse.Create(pgo);
            var cols = tp.Field("colliders").GetValue<List<Transform>>();
            int bad = 0;
            if (cols != null) foreach (Transform t in cols) if (t == null || t.GetComponent<Collider>() == null) bad++;
            Plugin.Log.LogInfo($"Compass diagnostics: held '{pgo.name}' value {tv.Field("dollarValueCurrent").GetValue<float>():0} " +
                $"(set {tv.Field("dollarValueSet").GetValue<bool>()}, original {tv.Field("dollarValueOriginal").GetValue<float>():0}), " +
                $"dead {pgo.dead}, active {tp.Field("isActive").GetValue<bool>()}, isValuable {tp.Field("isValuable").GetValue<bool>()}, " +
                $"grabbed {Traverse.Create(grabber).Field("grabbed").GetValue<bool>()}, " +
                (d == null ? "NO impact detector" :
                $"indestructible {td.Field("isIndestructible").GetValue<bool>()}, spawn-safe {td.Field("indestructibleSpawnTimer").GetValue<float>():0.0}, " +
                $"breakLogic {td.Field("breakLogic").GetValue<bool>()}, fragility {d.fragility:0.#}, durability {d.durability:0.#}, " +
                $"destroyDisable {d.destroyDisable}, impactDisabled {td.Field("impactDisabledTimer").GetValue<float>():0.0}") +
                $", colliders listed {(cols != null ? cols.Count : -1)} ({bad} broken), centre {pgo.centerPoint} vs item {pgo.transform.position}, " +
                $"WorldSpaceUIValue {(WorldSpaceUIValue.instance != null ? "present" : "MISSING")}.");
        }
    }
}
