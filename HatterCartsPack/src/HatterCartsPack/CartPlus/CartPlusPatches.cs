using HarmonyLib;
using UnityEngine;

namespace HatterCartsPack
{
    /// <summary>
    /// A local player shrunk in a C.A.R.T. Plus sees from lower down: the crouch camera pivot (CameraCrouchPosition,
    /// whose Update sets its localPosition every frame) is lowered by <see cref="CartPlusContents.LocalCameraDrop"/>.
    /// </summary>
    [HarmonyPatch(typeof(CameraCrouchPosition), "Update")]
    internal static class CartPlusCameraPatch
    {
        private static float applied;   // the drop added last frame, in case Update returned early and didn't overwrite it

        [HarmonyPrefix]
        private static void Before(CameraCrouchPosition __instance, out Vector3 __state)
        {
            __state = __instance.transform.localPosition;
        }

        [HarmonyPostfix]
        private static void After(CameraCrouchPosition __instance, Vector3 __state)
        {
            float drop = CartPlusContents.LocalCameraDrop;
            if (drop <= 0f && applied <= 0f) return;
            Transform t = __instance.transform;
            Vector3 lp = t.localPosition;
            if (lp == __state) lp.y += applied;   // Update didn't set it this frame: take last frame's drop back off
            lp.y -= drop;
            t.localPosition = lp;
            applied = drop;
        }
    }
}
