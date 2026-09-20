using System.ComponentModel;
using BepInEx.Configuration;
using UnityEngine;

namespace NOVR;

public class ModConfiguration
{
    public static ModConfiguration Instance;
    

    public readonly ConfigFile Config;
    public readonly ConfigEntry<float> TargetDesignatorOvershoot;
    public readonly ConfigEntry<bool> EnableNativeMenuUi;
    public readonly ConfigEntry<float> NativeMenuScale;
    public readonly ConfigEntry<float> NativeMenuDistance;
    public readonly ConfigEntry<float> NativeMenuHeightOffset;
    public readonly ConfigEntry<float> PitchLadderVisibleRange;
    public readonly ConfigEntry<float> PitchLadderScale;
    public readonly ConfigEntry<float> HudScale;
    public readonly ConfigEntry<float> CompassVerticalOffset;
    public readonly ConfigEntry<float> CockpitSeatForwardOffset;
    public readonly ConfigEntry<float> ExternalViewDistance;
    public readonly ConfigEntry<float> VrUiLayerRefreshInterval;

    public ModConfiguration(ConfigFile config)
    {
        Instance = this;

        Config = config;
        TargetDesignatorOvershoot = config.Bind(
            "General",
            "Target Designator Overshoot",
            1.2f,
            "How much the target designator should multiply rotation to make for easier high off boresight target designation. Set to 1.0 to disable");

        EnableNativeMenuUi = config.Bind(
            "Experimental",
            "Enable Native Menu UI",
            true,
            "Use NOVR's native VR menu UI for non-flight menus. Disable to fall back to the existing patched game UI.");

        NativeMenuScale = config.Bind(
            "Experimental",
            "Native Menu Scale",
            1.25f,
            "Size multiplier for NOVR's native VR menu UI. Values from 0.75 to 2.0 are supported.");

        NativeMenuDistance = config.Bind(
            "Experimental",
            "Native Menu Distance",
            3.0f,
            "Distance in meters from the headset when NOVR's native VR menu UI is opened or recentered. Values from 1.5 to 6.0 are supported.");

        NativeMenuHeightOffset = config.Bind(
            "Experimental",
            "Native Menu Height Offset",
            0.0f,
            "Vertical offset in meters applied when NOVR's native VR menu UI is opened or recentered. Values from -0.25 to 1.0 are supported.");

        HudScale = config.Bind(
            "HUD",
            "HUD Scale",
            0.5f,
            new ConfigDescription(
                "Overall size multiplier for the in-flight HUD (HUDCanvas and everything under it). 1.0 is the stock size, 0.5 is half size. Applies live.",
                new AcceptableValueRange<float>(0.25f, 2.0f)));

        CockpitSeatForwardOffset = config.Bind(
            "Camera",
            "Cockpit Seat Forward Offset",
            0.0f,
            new ConfigDescription(
                "How far forward or back you sit in the cockpit, in meters. Positive moves you forward toward the instrument panel. Adjustable from the VR menu under COCKPIT. Applies live.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        ExternalViewDistance = config.Bind(
            "Camera",
            "External View Distance",
            25.0f,
            new ConfigDescription(
                "Distance in meters the external (third person) camera sits behind the aircraft. NOVR pins this camera to the orbit pivot to stop it fighting headset rotation; 0 reproduces the old behaviour of sitting inside the cockpit.",
                new AcceptableValueRange<float>(0.0f, 100.0f)));

        CompassVerticalOffset = config.Bind(
            "HUD",
            "Compass Vertical Offset",
            -40.0f,
            new ConfigDescription(
                "Vertical shift for the heading compass at the top of the HUD, in canvas units. Negative moves it down. 0 is the stock position. Applies live.",
                new AcceptableValueRange<float>(-300.0f, 300.0f)));

        VrUiLayerRefreshInterval = config.Bind(
            "HUD",
            "VR UI Layer Refresh Interval",
            0.5f,
            new ConfigDescription(
                "Seconds between re-applying the VR UI layer to patched canvases. This is what makes menus built after startup (graphics settings, for example) visible in VR instead of showing missing or misplaced text. Set to 0 to disable.",
                new AcceptableValueRange<float>(0.0f, 5.0f)));

        PitchLadderVisibleRange = config.Bind(
            "HUD",
            "Pitch Ladder Visible Range",
            30.0f,
            new ConfigDescription(
                "Total degrees of pitch ladder shown around the aircraft's current pitch, matching how a real HUD only shows a narrow band. Set to 360 to restore the original full-sphere ladder.",
                new AcceptableValueRange<float>(10.0f, 360.0f)));

        PitchLadderScale = config.Bind(
            "HUD",
            "Pitch Ladder Scale",
            0.8f,
            new ConfigDescription(
                "Size multiplier for the pitch ladder slices. The stock value is 0.8; lower it to shrink the ladder. Requires re-entering a cockpit to take effect.",
                new AcceptableValueRange<float>(0.1f, 2.0f)));
    }
}
