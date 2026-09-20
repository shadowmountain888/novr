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
    public readonly ConfigEntry<int> SinglePlayerMinimumRank;
    public readonly ConfigEntry<float> SinglePlayerMinimumFunds;
    public readonly ConfigEntry<bool> DisableTrees;
    public readonly ConfigEntry<bool> HideChatLayoutSpacers;
    public readonly ConfigEntry<bool> HideVirtualMfdWhenMapClosed;
    public readonly ConfigEntry<bool> HideParkedMfdScreens;
    public readonly ConfigEntry<bool> DisableUnityXrCameraAutoTracking;
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

        SinglePlayerMinimumRank = config.Bind(
            "Gameplay",
            "Single Player Minimum Rank",
            0,
            new ConfigDescription(
                "Raises your rank in stock single player missions so rank-gated aircraft are selectable, for missions whose starting rank is baked into the game assets. 0 disables it. Only applies while you are the host - on another player's server rank is server-authoritative and this does nothing.",
                new AcceptableValueRange<int>(0, 50)));

        SinglePlayerMinimumFunds = config.Bind(
            "Gameplay",
            "Single Player Minimum Funds",
            0.0f,
            new ConfigDescription(
                "Holds your faction's funds at this floor in stock single player missions, whose starting balance is baked into the game assets. 0 disables it. Hosting only - on another player's server funds are server-authoritative and this does nothing.",
                new AcceptableValueRange<float>(0.0f, 10000000.0f)));

        DisableTrees = config.Bind(
            "Graphics",
            "Disable Trees",
            false,
            "Forces the tree draw range to zero. The stock graphics menu only has a tree distance slider with no off switch, so its lowest setting still draws nearby trees. Helps a lot with VR GPU cost.");

        HideChatLayoutSpacers = config.Bind(
            "HUD",
            "Hide Chat Layout Spacers",
            true,
            "Disables the Image on ChatCanvas/TopPanel's LeftSpace, MiddleSpace and RightSpace layout spacers. They are opaque white with no sprite, which is invisible in the stock screen-space canvas but renders as large white panels about 3 meters ahead once the canvas is moved to world space for VR.");

        HideVirtualMfdWhenMapClosed = config.Bind(
            "HUD",
            "Hide Virtual MFD When Map Closed",
            true,
            "Holds a CanvasGroup alpha of 0 on GameplayUICanvas/VirtualMFD whenever the stock map is not maximized. The stock game only deactivates the MFD buttons and parks its screens 2560 px off-screen; the two 50x550 button-column backgrounds and the white 450x650 screen frames stay drawn, which is invisible on a monitor but shows up as tall thin rectangles 1.4 m and 9.2 m to each side of the pilot once the canvas is in world space for VR. Toggling the map (M) still shows the MFD.");

        HideParkedMfdScreens = config.Bind(
            "HUD",
            "Hide Parked MFD Screens",
            true,
            "Disables the frame Image on each VirtualMFD screen (MAP, HUD, PALA, MIS and so on) while that screen is closed. Closed screens are parked at localPosition +/-Screen.width, off-screen in the stock game but about 9 m to each side in VR. Also applies while the map is maximized, so only the screen you have open is drawn.");

        DisableUnityXrCameraAutoTracking = config.Bind(
            "Camera",
            "Disable Unity XR Camera Auto Tracking",
            true,
            "Stops Unity's XR integration from also applying the headset pose to NOVR's cameras. NOVR poses them itself; with both active, positional tracking is applied twice and leaning moves the view about 2x. Existed in 0.2.0, dropped in 0.4.x, restored here.");

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
