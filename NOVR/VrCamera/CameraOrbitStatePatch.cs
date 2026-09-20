using HarmonyLib;
using UnityEngine;

namespace NOVR.VrCamera;

internal static class CameraOrbitStatePatch
{
    [HarmonyPatch(typeof(CameraOrbitState), "CameraMotion")]
    private static class CameraMotionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(CameraStateManager cam)
        {
            if (cam?.cameraPivot == null)
                return true;

            // This prefix replaces the stock orbit motion so the game cannot fight the headset
            // rotation. Snapping straight onto the pivot, however, leaves the external view sitting
            // at the aircraft itself - i.e. still inside the cockpit. Pull back along the pivot's
            // forward axis so the aircraft is actually in front of the camera.
            var pivot = cam.cameraPivot;
            var viewDistance = ModConfiguration.Instance?.ExternalViewDistance.Value ?? 25f;

            cam.transform.SetPositionAndRotation(
                pivot.position - pivot.forward * viewDistance,
                pivot.rotation);
            return false;
        }
    }
}
