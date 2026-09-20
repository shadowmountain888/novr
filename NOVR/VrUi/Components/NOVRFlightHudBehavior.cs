using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NOVR.VrUi.SpecialBehavior;

public class NOVRFlightHudBehavior : UIRenderedCanvasBehavior
{
    // HUDCanvas is the single parent of every HUD element. UIRenderedCanvasBehavior converts it
    // to a world-space canvas (layer 30, positioned at z=1000), and CanvasScaler does not drive
    // localScale in world-space mode - so scaling the transform directly is what actually works.
    private Vector3 _baseLocalScale;
    private bool _capturedBaseLocalScale;

    // The heading compass lives on FlightHud.compass; shift it vertically without touching layout.
    private static readonly FieldInfo CompassField = AccessTools.Field(typeof(global::FlightHud), "compass");
    private RectTransform _compassRect;
    private Vector2 _baseCompassAnchoredPosition;
    private bool _capturedCompassBase;

    public override void Awake()
    {
        base.Awake();
        var hudcenter = FindChildStartingWith(transform, "HUDCenter");
        if (hudcenter != null) hudcenter.gameObject.AddComponent(typeof(NoVrHudBehavior));
        
        var hmdcenter = FindChildStartingWith(transform, "HMDCenter");
        if (hmdcenter != null) hmdcenter.gameObject.AddComponent(typeof(NOVRHMDBehavior));
        
        // Stock NOVR pulls the weapon (TopRightPanel) and minimap (LowerLeftPanel) panels off the
        // helmet display, pins them among the fixed forward HUD symbology and strips their
        // backgrounds. In helmet mode they stay head-locked with the stock background and layout,
        // and are placed by angle toward the corners of the view (see PlaceHelmetSidePanels).
        _helmetSidePanels = ModConfiguration.Instance?.HelmetMountedSidePanels.Value ?? true;
        if (_helmetSidePanels && hmdcenter != null)
        {
            _hmdCenter = hmdcenter;
            _hudCenter = hudcenter;
            _weaponPanel = FindChildStartingWith(transform, "TopRightPanel");
            _mapPanel = FindChildStartingWith(transform, "LowerLeftPanel");
            if (_weaponPanel != null) _weaponPanel.SetParent(hmdcenter, false);
            if (_mapPanel != null) _mapPanel.SetParent(hmdcenter, false);
        }
        else if (hudcenter != null)
        {
            MoveHmdPanelToHud("TopRightPanel", hudcenter, new Vector3(330, 290, 0f), new Vector3(0.6f, 0.6f, 0.6f));
            MoveHmdPanelToHud("LowerLeftPanel", hudcenter, new Vector3(-400f, 80f, 0f), new Vector3(0.6f, 0.6f, 0.6f));
        }

        var targetDesignator = FindChildStartingWith(transform, "targetDesignator");
        if (targetDesignator != null) targetDesignator.gameObject.AddComponent(typeof(NOVRTargetDesignatorBehavior));

        if (!gameObject.TryGetComponent<PitchCompassBehavior>(out _))
        {
            gameObject.AddComponent<PitchCompassBehavior>();
        }

        if (!gameObject.TryGetComponent<NOVR.VrUi.Native.CockpitHudTuningMenu>(out _))
        {
            gameObject.AddComponent<NOVR.VrUi.Native.CockpitHudTuningMenu>();
        }

        // Only when mosdef31's FunnelGunSight is installed: it draws with OnGUI + GL in screen
        // pixels, which never reaches the headset, so its sight is redrawn on the HUD sphere.
        if (NOVR.Compat.FunnelGunSightVrAdapter.ModPresent &&
            !gameObject.TryGetComponent<NOVR.Compat.FunnelGunSightVrAdapter>(out _))
        {
            gameObject.AddComponent<NOVR.Compat.FunnelGunSightVrAdapter>();
        }

        // var velocityVector = FindChildStartingWith(transform, "velocityVector");
        // if (velocityVector != null) velocityVector.gameObject.AddComponent(typeof(NOVRVelocityVectorBehavior));
    }
    
    private void Update()
    {
        transform.position = new Vector3(0f, 0f, 1000f);
        transform.rotation = Quaternion.identity;
        ApplyHudScale();
        ApplyCompassOffset();
        PlaceHelmetSidePanels();
    }

    private bool _helmetSidePanels;
    private Transform _hmdCenter;
    private Transform _hudCenter;
    private Transform _weaponPanel;
    private Transform _mapPanel;

    // HMDCenter is head-slaved by NOVRHMDBehavior (1000 units along the head's forward, same
    // rotation), so a child at local (x, y, 0) sits at a fixed spot in the pilot's view. Placement is
    // by ANGLE from the view centre rather than canvas pixels so the panels do not drift when HUD
    // Scale changes, and their size is divided by the parent's lossy scale for the same reason.
    // Each panel is yawed/pitched to face the eye so it does not look sheared out at the corner.
    private void PlaceHelmetSidePanels()
    {
        if (!_helmetSidePanels || _hmdCenter == null) return;

        var config = ModConfiguration.Instance;
        if (config == null) return;

        // Follow Head on: HMDCenter, which NOVRHMDBehavior slaves to the head. Off: HUDCenter, which
        // NoVrHudBehavior pins straight ahead of the aircraft. Both sit 1000 units out, so the same
        // angle-based placement works for either; the panels are simply re-parented when it changes.
        var parent = config.SidePanelsFollowHead.Value || _hudCenter == null ? _hmdCenter : _hudCenter;
        var parentScale = parent.lossyScale.x;
        if (parentScale <= Mathf.Epsilon) return;

        var showBackground = config.SidePanelBackgrounds.Value;

        if (_weaponPanel != null && _weaponPanel.parent != parent) _weaponPanel.SetParent(parent, false);
        if (_mapPanel != null && _mapPanel.parent != parent) _mapPanel.SetParent(parent, false);

        PlaceHelmetPanel(_weaponPanel, config.WeaponPanelHorizontalAngle.Value, config.WeaponPanelVerticalAngle.Value, config.WeaponPanelSize.Value, parentScale, showBackground);
        PlaceHelmetPanel(_mapPanel, config.MapPanelHorizontalAngle.Value, config.MapPanelVerticalAngle.Value, config.MapPanelSize.Value, parentScale, showBackground);
    }

    private static void PlaceHelmetPanel(Transform panel, float yawDegrees, float pitchDegrees, float size, float parentScale, bool showBackground)
    {
        if (panel == null) return;

        const float helmetDistance = 1000f;
        panel.localPosition = new Vector3(
            Mathf.Tan(yawDegrees * Mathf.Deg2Rad) * helmetDistance / parentScale,
            Mathf.Tan(pitchDegrees * Mathf.Deg2Rad) * helmetDistance / parentScale,
            0f);
        panel.localEulerAngles = new Vector3(-pitchDegrees, yawDegrees, 0f);

        // 0.4 world units per canvas pixel is what the panels measured at before (0.6 local x 0.67).
        var localScale = 0.4f * size / parentScale;
        panel.localScale = new Vector3(localScale, localScale, localScale);

        if (panel.TryGetComponent<Image>(out var background) && background.enabled != showBackground)
        {
            background.enabled = showBackground;
        }
    }

    private void ApplyCompassOffset()
    {
        if (!_capturedCompassBase)
        {
            if (CompassField == null) return;
            if (!TryGetComponent<global::FlightHud>(out var flightHud)) return;

            if (CompassField.GetValue(flightHud) is not Component compass) return;

            _compassRect = compass.GetComponent<RectTransform>();
            if (_compassRect == null) return;

            _baseCompassAnchoredPosition = _compassRect.anchoredPosition;
            _capturedCompassBase = true;
        }

        var verticalOffset = ModConfiguration.Instance?.CompassVerticalOffset.Value ?? 0f;
        var targetPosition = _baseCompassAnchoredPosition + new Vector2(0f, verticalOffset);

        if ((_compassRect.anchoredPosition - targetPosition).sqrMagnitude > 1e-6f)
        {
            _compassRect.anchoredPosition = targetPosition;
        }
    }

    private void ApplyHudScale()
    {
        var hudScale = ModConfiguration.Instance?.HudScale.Value ?? 1.0f;
        if (hudScale < 0.05f) hudScale = 0.05f;

        if (!_capturedBaseLocalScale)
        {
            // Capture the stock scale once the canvas has been set up (it is 2.0 on a 4K display,
            // derived from the screen-space CanvasScaler before the world-space conversion).
            var currentScale = transform.localScale;
            if (currentScale.x <= Mathf.Epsilon) return;

            _baseLocalScale = currentScale;
            _capturedBaseLocalScale = true;
        }

        var targetScale = _baseLocalScale * hudScale;
        if ((transform.localScale - targetScale).sqrMagnitude > 1e-8f)
        {
            transform.localScale = targetScale;
        }
    }
    
    private void MoveHmdPanelToHud(string panelName, Transform noVrHudParent, Vector3 localPosition, Vector3 localScale)
    {
        if (noVrHudParent == null)
            return;
        
        var panel = FindChildStartingWith(transform, panelName);
        if (panel == null)
            return;
        
        panel.SetParent(noVrHudParent, false);
        panel.localPosition = localPosition;
        panel.localEulerAngles = Vector3.zero;
        panel.localScale = localScale;
        MakePanelInvisible(panel);
        
        if (panelName == "TopRightPanel")
        {
            PositionTopRightPanelChildren(panel);
        }
    }
    
    private static void MakePanelInvisible(Transform panel)
    {
        var image = panel.GetComponent<Image>();
        if (image != null)
            image.enabled = false;
    }
    
    private static void MakeChildImageInvisible(Transform parent, string childName)
    {
        var child = FindChildStartingWith(parent, childName);
        if (child == null)
            return;
        
        var image = child.GetComponent<Image>();
        if (image != null)
            image.enabled = false;
    }
    
    
    private static void PositionTopRightPanelChildren(Transform topRightPanel)
    {
        SetChildLocalPosition(topRightPanel, "countermeasuresBackground", new Vector3(-750f, -55f, 0f));
        SetChildLocalPosition(topRightPanel, "weaponPanel", new Vector3(-100f, -55f, 0f));
        SetChildLocalPosition(topRightPanel, "PowerPanel", new Vector3(-350f, -80f, 0f));
        var powerPanel = FindChildStartingWith(topRightPanel, "PowerPanel");
        if (powerPanel == null)
            return;
        
        MakePanelInvisible(powerPanel);
        MakeChildImageInvisible(powerPanel, "chargeBarBackground");
    }
    
    private static void SetChildLocalPosition(Transform parent, string childName, Vector3 localPosition)
    {
        var child = FindChildStartingWith(parent, childName);
        if (child == null)
            return;
        
        child.localPosition = localPosition;
    }
}
