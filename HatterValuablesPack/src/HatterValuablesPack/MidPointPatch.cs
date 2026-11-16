using HarmonyLib;
using UnityEngine;

namespace HatterValuablesPack
{
    /// <summary>
    /// The value tag you see when grabbing a valuable (WorldSpaceUIValue) sits at PhysGrabObject.centerPoint, which
    /// follows midPointOffset, measured once in PhysGrabObject.Start from the colliders' bounds. On the compasses that
    /// measurement came out far from the compass (the tag floated away from it), so for them it's set to the model's
    /// middle instead, right after Start. The first time, the game's figure and the new one are logged.
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabObject), "Start")]
    internal static class MidPointPatch
    {
        private static bool logged;

        private static void Postfix(PhysGrabObject __instance)
        {
            if (__instance == null) return;
            Compass c = __instance.GetComponent<Compass>();
            if (c == null) return;
            if (!logged)
            {
                logged = true;
                Plugin.Log.LogInfo($"Compass value tag: the game measured its middle at {__instance.midPointOffset} (local), " +
                                   $"set to the model's middle {c.modelCentre}.");
            }
            __instance.midPointOffset = c.modelCentre;
            __instance.midPoint = __instance.transform.TransformPoint(c.modelCentre);
            __instance.centerPoint = __instance.midPoint;
            // no grab-collider entry may point at something without a collider (the game reads each one's material)
            int removed = __instance.colliders != null ? __instance.colliders.RemoveAll(t => t == null || t.GetComponent<Collider>() == null) : 0;
            if (removed > 0) Plugin.Log.LogWarning($"Compass: {removed} grab-collider entr{(removed == 1 ? "y" : "ies")} without a collider removed.");
        }
    }
}
