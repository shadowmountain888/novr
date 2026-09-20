using System;
using System.Reflection;
using HarmonyLib;
using NOVR.VrUi.HarmonyPatches;
using UnityEngine;

namespace NOVR.Compat;

/// <summary>
/// Makes mosdef31's FunnelGunSight mod visible in the headset.
///
/// That mod computes its sight as 2D screen-pixel points (CameraStateManager.mainCamera
/// .WorldToScreenPoint) and draws them in FunnelRenderer.OnGUI with GL.LoadPixelMatrix. Immediate-mode
/// GL in OnGUI only ever reaches the desktop window, never the XR eye textures, so in VR the sight is
/// computed correctly every frame and then drawn where the pilot cannot see it.
///
/// Nothing in the other mod is patched. It already publishes everything it draws in
/// FunnelRenderer._d (a SightDrawData struct), so this reads that by reflection, turns each pixel
/// back into a world ray with the SAME camera the mod projected with (ScreenPointToRay is the exact
/// inverse of its WorldToScreenPoint, whatever pixel space XR gives that camera), maps the ray onto
/// NOVR's HUD sphere, and redraws the primitives with LineRenderers on the VR UI layer.
/// Text labels (range / ground readout) are not reproduced.
/// </summary>
public class FunnelGunSightVrAdapter : MonoBehaviour
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int RingSegments = 40;
    private const float SearchInterval = 1f;
    private const int MaxDataAgeFrames = 2;

    private static readonly Type RendererType = AccessTools.TypeByName("FunnelGunSight.FunnelRenderer");
    private static readonly Type DataType = AccessTools.TypeByName("FunnelGunSight.SightDrawData");

    public static bool ModPresent => RendererType != null && DataType != null;

    private static readonly FieldInfo DataField = RendererType?.GetField("_d", Any);
    private static readonly FieldInfo HasDataField = RendererType?.GetField("_hasData", Any);
    private static readonly FieldInfo DataFrameField = RendererType?.GetField("_dataFrame", Any);

    private static readonly FieldInfo GunCrossField = DataType?.GetField("GunCross", Any);
    private static readonly FieldInfo LeftWallField = DataType?.GetField("LeftWall", Any);
    private static readonly FieldInfo RightWallField = DataType?.GetField("RightWall", Any);
    private static readonly FieldInfo WallCountField = DataType?.GetField("WallCount", Any);
    private static readonly FieldInfo IsVisibleField = DataType?.GetField("IsVisible", Any);
    private static readonly FieldInfo ShowWallsField = DataType?.GetField("ShowWalls", Any);
    private static readonly FieldInfo DotPosField = DataType?.GetField("DotPos", Any);
    private static readonly FieldInfo DotRadiusField = DataType?.GetField("DotRadius", Any);
    private static readonly FieldInfo InSolutionField = DataType?.GetField("InSolution", Any);
    private static readonly FieldInfo GroundPipperField = DataType?.GetField("GroundPipper", Any);
    private static readonly FieldInfo GroundRadiusField = DataType?.GetField("GroundPipperRadius", Any);
    private static readonly FieldInfo LeadDotField = DataType?.GetField("LeadDot", Any);
    private static readonly FieldInfo LeadRadiusField = DataType?.GetField("LeadDotRadius", Any);

    private Component _renderer;
    private float _nextSearchTime;

    private Material _material;
    private LineRenderer _crossHorizontal;
    private LineRenderer _crossVertical;
    private LineRenderer _leftWall;
    private LineRenderer _rightWall;
    private LineRenderer _rangeRing;
    private LineRenderer _groundRing;
    private LineRenderer _leadRing;
    private LineRenderer[] _all;

    private readonly Vector3[] _ringPoints = new Vector3[RingSegments];
    private Vector3[] _wallPoints = new Vector3[64];

    private void Awake()
    {
        var shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            Debug.LogWarning("[NOVR] FunnelGunSight adapter: shader 'Hidden/Internal-Colored' not found; adapter disabled.");
            enabled = false;
            return;
        }

        _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        _material.SetInt("_ZWrite", 0);
        // HUD symbology must not be occluded by the canopy frame or cockpit geometry.
        _material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

        _crossHorizontal = CreateLine("GunCrossH", false);
        _crossVertical = CreateLine("GunCrossV", false);
        _leftWall = CreateLine("LeftWall", false);
        _rightWall = CreateLine("RightWall", false);
        _rangeRing = CreateLine("RangeRing", true);
        _groundRing = CreateLine("GroundRing", true);
        _leadRing = CreateLine("LeadRing", true);
        _all = new[] { _crossHorizontal, _crossVertical, _leftWall, _rightWall, _rangeRing, _groundRing, _leadRing };
        HideAll();

        Debug.Log("[NOVR] FunnelGunSight VR adapter active.");
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }

    private LineRenderer CreateLine(string lineName, bool loop)
    {
        var lineObject = new GameObject("NOVR_Funnel_" + lineName);
        lineObject.transform.SetParent(transform, false);
        lineObject.layer = (int)LayerHelper.GetVrUiLayer();

        var line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = loop;
        line.sharedMaterial = _material;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.positionCount = 0;
        return line;
    }

    private void HideAll()
    {
        if (_all == null) return;
        for (var index = 0; index < _all.Length; index++)
        {
            if (_all[index] != null && _all[index].enabled) _all[index].enabled = false;
        }
    }

    // LateUpdate so the other mod's Update/FixedUpdate has already published this frame's data.
    private void LateUpdate()
    {
        if (ModConfiguration.Instance?.FunnelGunSightVrAdapter.Value == false || !ModPresent)
        {
            HideAll();
            return;
        }

        if (!TryGetFreshData(out var data))
        {
            HideAll();
            return;
        }

        var projectionCamera = SceneSingleton<CameraStateManager>.i != null
            ? SceneSingleton<CameraStateManager>.i.mainCamera
            : null;
        var headCamera = APIBus.MainCamera;
        var hudCamera = APIBus.CockpitHudCamera;
        if (projectionCamera == null || headCamera == null || hudCamera == null)
        {
            HideAll();
            return;
        }

        var gunCross = (Vector2)GunCrossField.GetValue(data);
        if (!ToHud(projectionCamera, headCamera, gunCross, out var crossCentre) ||
            !ToHud(projectionCamera, headCamera, gunCross + Vector2.right, out var crossPlusOne))
        {
            HideAll();
            return;
        }

        // World units on the HUD sphere covered by one of the mod's screen pixels.
        var unitsPerPixel = Mathf.Max((crossPlusOne - crossCentre).magnitude, 0.01f);
        var width = Mathf.Max(2f * unitsPerPixel, 0.6f);
        var colour = (bool)InSolutionField.GetValue(data) ? Color.white : new Color(0f, 1f, 0f, 1f);
        var right = hudCamera.transform.right;
        var up = hudCamera.transform.up;

        var crossArm = 8f * unitsPerPixel;
        SetSegment(_crossHorizontal, crossCentre - right * crossArm, crossCentre + right * crossArm, width, colour);
        SetSegment(_crossVertical, crossCentre - up * crossArm, crossCentre + up * crossArm, width, colour);

        var showWalls = (bool)ShowWallsField.GetValue(data);
        var wallCount = (int)WallCountField.GetValue(data);
        SetPolyline(_leftWall, showWalls ? LeftWallField.GetValue(data) as Vector2[] : null, wallCount, projectionCamera, headCamera, width, colour);
        SetPolyline(_rightWall, showWalls ? RightWallField.GetValue(data) as Vector2[] : null, wallCount, projectionCamera, headCamera, width, colour);

        SetRing(_rangeRing, DotPosField.GetValue(data), (float)DotRadiusField.GetValue(data), projectionCamera, headCamera, right, up, width, colour);
        SetRing(_groundRing, GroundPipperField.GetValue(data), (float)GroundRadiusField.GetValue(data), projectionCamera, headCamera, right, up, width, colour);
        SetRing(_leadRing, LeadDotField.GetValue(data), (float)LeadRadiusField.GetValue(data), projectionCamera, headCamera, right, up, width, colour);
    }

    private bool TryGetFreshData(out object data)
    {
        data = null;

        if (_renderer == null)
        {
            if (Time.unscaledTime < _nextSearchTime) return false;
            _nextSearchTime = Time.unscaledTime + SearchInterval;
            _renderer = FindObjectOfType(RendererType) as Component;
            if (_renderer == null) return false;
        }

        if (!(bool)HasDataField.GetValue(_renderer)) return false;
        if (Time.frameCount - (int)DataFrameField.GetValue(_renderer) > MaxDataAgeFrames) return false;

        data = DataField.GetValue(_renderer);
        return data != null && (bool)IsVisibleField.GetValue(data);
    }

    // Inverse of the other mod's projection: its pixel -> world ray (same camera) -> NOVR HUD sphere.
    private static bool ToHud(Camera projectionCamera, Camera headCamera, Vector2 pixel, out Vector3 hudPosition)
    {
        var ray = projectionCamera.ScreenPointToRay(new Vector3(pixel.x, pixel.y, 0f));
        var farPoint = headCamera.transform.position + ray.direction * 5000f;
        return VrHudProjection.TryProjectDirectionToCockpitHud(farPoint, out hudPosition);
    }

    private static void SetSegment(LineRenderer line, Vector3 start, Vector3 end, float width, Color colour)
    {
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        Style(line, width, colour);
    }

    private void SetPolyline(LineRenderer line, Vector2[] pixels, int count, Camera projectionCamera, Camera headCamera, float width, Color colour)
    {
        if (pixels == null || count < 2)
        {
            line.enabled = false;
            return;
        }

        count = Mathf.Min(count, pixels.Length);
        if (_wallPoints.Length < count) _wallPoints = new Vector3[count];

        var written = 0;
        for (var index = 0; index < count; index++)
        {
            if (ToHud(projectionCamera, headCamera, pixels[index], out var hudPoint))
            {
                _wallPoints[written++] = hudPoint;
            }
        }

        if (written < 2)
        {
            line.enabled = false;
            return;
        }

        line.positionCount = written;
        line.SetPositions(_wallPoints);
        Style(line, width, colour);
    }

    private void SetRing(LineRenderer line, object boxedCentre, float radiusPixels, Camera projectionCamera, Camera headCamera, Vector3 right, Vector3 up, float width, Color colour)
    {
        if (boxedCentre == null || radiusPixels <= 1f)
        {
            line.enabled = false;
            return;
        }

        var centrePixel = (Vector2)boxedCentre;
        if (!ToHud(projectionCamera, headCamera, centrePixel, out var centre) ||
            !ToHud(projectionCamera, headCamera, centrePixel + Vector2.right * radiusPixels, out var edge))
        {
            line.enabled = false;
            return;
        }

        var radius = (edge - centre).magnitude;
        for (var index = 0; index < RingSegments; index++)
        {
            var angle = index * (Mathf.PI * 2f / RingSegments);
            _ringPoints[index] = centre + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
        }

        line.positionCount = RingSegments;
        line.SetPositions(_ringPoints);
        Style(line, width, colour);
    }

    private static void Style(LineRenderer line, float width, Color colour)
    {
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = colour;
        line.endColor = colour;
        if (!line.enabled) line.enabled = true;
    }
}
