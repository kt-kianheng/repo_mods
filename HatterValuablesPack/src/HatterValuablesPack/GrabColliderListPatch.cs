using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HatterValuablesPack
{
    /// <summary>
    /// PhysGrabObject.OverrideTimersTick sets the grab material on every entry of its colliders list
    /// (GetComponent&lt;Collider&gt;().material). An entry with no Collider on it threw a NullReferenceException there every
    /// frame, which cut PhysGrabObject.Update short before impacts, breaking and removal (the compasses couldn't be
    /// damaged, stayed behind broken, and so on). Before that step runs, entries without a collider are dropped; the
    /// first time per object it's logged which ones they were.
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabObject), "OverrideTimersTick")]
    internal static class GrabColliderListPatch
    {
        private static readonly AccessTools.FieldRef<PhysGrabObject, float> TimerAlterMaterial =
            AccessTools.FieldRefAccess<PhysGrabObject, float>("timerAlterMaterial");
        private static readonly HashSet<int> logged = new HashSet<int>();

        private static void Prefix(PhysGrabObject __instance)
        {
            if (__instance == null || __instance.colliders == null) return;
            float t = TimerAlterMaterial(__instance);
            if (t > 0f || t == -123f) return;     // the material step only runs then
            List<Transform> list = __instance.colliders;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Transform c = list[i];
                if (c != null && c.GetComponent<Collider>() != null) continue;
                if (logged.Add(__instance.GetInstanceID()))
                {
                    string what = "a destroyed object";
                    if (c != null)
                    {
                        var names = new List<string>();
                        foreach (Component k in c.GetComponents<Component>()) if (k != null) names.Add(k.GetType().Name);
                        what = $"'{c.name}' ({string.Join(", ", names)}) under '{(c.parent != null ? c.parent.name : "-")}'";
                    }
                    Plugin.Log.LogWarning($"Grab colliders: '{__instance.name}' listed {what} as a grab collider without a collider; dropped.");
                }
                list.RemoveAt(i);
            }
        }
    }
}
