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
        
        if (hudcenter != null)
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

        // var velocityVector = FindChildStartingWith(transform, "velocityVector");
        // if (velocityVector != null) velocityVector.gameObject.AddComponent(typeof(NOVRVelocityVectorBehavior));
    }
    
    private void Update()
    {
        transform.position = new Vector3(0f, 0f, 1000f);
        transform.rotation = Quaternion.identity;
        ApplyHudScale();
        ApplyCompassOffset();
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
