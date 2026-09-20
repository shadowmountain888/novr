using System;
using System.Reflection;
using NOVR.UnityTypesHelper;
using UnityEngine;

namespace NOVR;

[Flags]
public enum CalibrationAxes : byte
{
    None = 0,
    Pitch = 1,
    Yaw = 2,
    Roll = 4,
    All = Pitch | Yaw | Roll,
    X = Pitch,
    Y = Yaw,
    Z = Roll,
}

public class NOVRPoseDriver: NOVRBehaviour
{
    
    

    


    
    protected override void OnBeforeRender()
    {
        base.OnBeforeRender();
        UpdateTransform();
    }

    private void Update()
    {
        UpdateTransform();
    }

    private void LateUpdate()
    {
        UpdateTransform();
    }

    // Set only on the in-world VR cameras. The menu UI also uses a pose driver and must not move
    // with the cockpit seat adjustment.
    public bool ApplySeatOffset;

    private bool _autoTrackingHandled;

    // Unity's XR integration poses any stereo camera itself in OnBeforeRender unless told not to.
    // NOVRHeadsetData is meant to turn that off, but it is created on the NOVR core object, which
    // has no Camera, so its GetComponent<Camera>() is null and the call never runs on any camera.
    // With both Unity and this driver writing the head pose, positional tracking is applied twice
    // and leaning moves the view roughly 2x. Do it here, on the camera this driver actually drives.
    // Deferred to the first UpdateTransform because NOUIManager adds its Camera after the driver.
    private void DisableUnityAutoTrackingOnce()
    {
        if (_autoTrackingHandled) return;
        if (ModConfiguration.Instance?.DisableUnityXrCameraAutoTracking.Value == false)
        {
            _autoTrackingHandled = true;
            return;
        }

        if (!TryGetComponent<Camera>(out var camera)) return;

        var disableMethod = UuvrXrDevice.XrDeviceType?.GetMethod("DisableAutoXRCameraTracking");
        if (disableMethod == null)
        {
            Debug.LogWarning($"[NOVR] XRDevice.DisableAutoXRCameraTracking not found; {name} may be double-tracked.");
            _autoTrackingHandled = true;
            return;
        }

        disableMethod.Invoke(null, new object[] { camera, true });
        Debug.Log($"[NOVR] Disabled Unity XR auto camera tracking on {name}");
        _autoTrackingHandled = true;
    }

    private void UpdateTransform()
    {
        DisableUnityAutoTrackingOnce();

        transform.localRotation = NOVRHeadsetData.Rotation;

        var localPosition = NOVRHeadsetData.Translation;
        if (ApplySeatOffset)
        {
            var seatForwardOffset = ModConfiguration.Instance?.CockpitSeatForwardOffset.Value ?? 0f;

            // Only while actually seated in an aircraft. The main menu camera also carries a
            // VrCamera, so an unconditional offset shifts the menu camera too - which moves the
            // anchor the native menu is placed against and leaves recentering misaligned.
            var seatHeightOffset = ModConfiguration.Instance?.CockpitSeatHeightOffset.Value ?? 0f;
            if ((seatForwardOffset != 0f || seatHeightOffset != 0f) && GameManager.GetLocalAircraft(out _))
            {
                localPosition += new Vector3(0f, seatHeightOffset, seatForwardOffset);
            }
        }

        transform.localPosition = localPosition;
    }

   
}
