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

    private void UpdateTransform()
    {
        transform.localRotation = NOVRHeadsetData.Rotation;

        var localPosition = NOVRHeadsetData.Translation;
        if (ApplySeatOffset)
        {
            var seatForwardOffset = ModConfiguration.Instance?.CockpitSeatForwardOffset.Value ?? 0f;

            // Only while actually seated in an aircraft. The main menu camera also carries a
            // VrCamera, so an unconditional offset shifts the menu camera too - which moves the
            // anchor the native menu is placed against and leaves recentering misaligned.
            if (seatForwardOffset != 0f && GameManager.GetLocalAircraft(out _))
            {
                localPosition += new Vector3(0f, 0f, seatForwardOffset);
            }
        }

        transform.localPosition = localPosition;
    }

   
}
