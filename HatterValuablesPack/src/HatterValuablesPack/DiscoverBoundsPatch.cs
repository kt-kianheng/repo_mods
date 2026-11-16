using HarmonyLib;
using UnityEngine;

namespace HatterValuablesPack
{
    /// <summary>
    /// The game's valuable highlight (the yellow corners when you find a valuable, ValuableDiscoverGraphic) sizes its box
    /// from every enabled renderer under the valuable. On the compasses that includes the borrowed Robe (skinned, with
    /// padded bounds) and the copied extraction screen (its texts and pieces), which made the box far bigger than the
    /// compass. For the compasses the box comes from their own colliders instead, which match the model.
    /// </summary>
    [HarmonyPatch(typeof(ValuableDiscoverGraphic), "RendererBoundsInScreenSpace")]
    internal static class DiscoverBoundsPatch
    {
        private static bool logged;

        private static void Prefix(ValuableDiscoverGraphic __instance, ref Bounds bigBounds)
        {
            PhysGrabObject target = __instance != null ? __instance.target : null;
            if (target == null || target.GetComponent<Compass>() == null) return;
            bool any = false;
            Bounds b = default;
            foreach (Collider c in target.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger || !c.enabled) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (!any) return;
            if (!logged)
            {
                logged = true;
                Plugin.Log.LogInfo($"Compass highlight: sized from the colliders ({b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m) " +
                                   $"instead of the renderers ({bigBounds.size.x:0.00} x {bigBounds.size.y:0.00} x {bigBounds.size.z:0.00} m).");
            }
            bigBounds = b;
        }
    }
}
