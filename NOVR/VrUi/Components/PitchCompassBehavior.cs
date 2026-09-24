using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NOVR.VrUi.SpecialBehavior;

public class PitchCompassBehavior : MonoBehaviour
{
    private const int FullPitchStepCount = 36;
    private const int SliceCount = FullPitchStepCount + 1;
    private const float PitchRangeDegrees = 180f;
    private const float PitchStepDegrees = PitchRangeDegrees / FullPitchStepCount;
    private const string SliceRootName = "NOVR_PitchCompassSlices";

    private static readonly FieldInfo PitchCompassField = AccessTools.Field(typeof(global::FlightHud), "pitchCompass");
    private static readonly FieldInfo CockpitTransformField = AccessTools.Field(typeof(global::FlightHud), "cockpitTransform");

    private RawImage _sourcePitchCompass;
    private RectTransform _sliceRoot;
    private float _fullTextureDisplayHeight;
    private bool _hasBuiltSlices;

    // Each built slice paired with the pitch angle (degrees from the horizon) it represents,
    // so Update can show only the slices near the aircraft's current pitch.
    private readonly List<KeyValuePair<Transform, float>> _slicePitches = new();
    
    
    private float _appliedSliceScale = float.NaN;
    private FlightHud _flightHud;
    private Transform _cockpitTransform;

    private void Start()
    {
        BuildSlices();
    }

    private void OnEnable()
    {
        BuildSlices();
    }

    private void Update()
    {
        if (!_hasBuiltSlices) return;
        RefreshCockpitTransform();
        if (_sliceRoot == null || _cockpitTransform == null) return;
        
        _sliceRoot.transform.position = Vector3.zero;
        
        
        var inverseCockpitRotation = Quaternion.Inverse(_cockpitTransform.rotation);
        var targetUp = inverseCockpitRotation * Vector3.up;
        var planeReference = Vector3.forward;
        
        var targetRight = Vector3.Cross(targetUp, planeReference).normalized;
        var targetForward = Vector3.Cross(targetUp, targetRight).normalized;
        
        _sliceRoot.transform.rotation = Quaternion.LookRotation(targetForward, targetUp);

        _sourcePitchCompass.enabled = false;

        UpdateSliceVisibility();
    }

    // Real HUDs only show the few degrees of pitch ladder visible through the combiner glass.
    // The full sphere is still built, but only the band around the current pitch is enabled.
    private void UpdateSliceVisibility()
    {
        var config = ModConfiguration.Instance;
        var visibleRange = config?.PitchLadderVisibleRange.Value ?? 30f;
        var showAll = visibleRange >= 360f;

        // Optional: no ladder once the gear is up and locked (shown again while it cycles or is down).
        var hideAll = config?.PitchLadderHideWhenGearUp.Value == true
                      && GameManager.GetLocalAircraft(out var aircraft)
                      && aircraft.gearState == LandingGear.GearState.LockedRetracted;

        // Size applies live; only rescale when the setting actually changed.
        var sliceScale = config?.PitchLadderScale.Value ?? 0.8f;
        var rescale = !Mathf.Approximately(sliceScale, _appliedSliceScale);
        _appliedSliceScale = sliceScale;
        var halfRange = visibleRange * 0.5f;

        var cockpitForward = _cockpitTransform.forward;
        var currentPitch = Mathf.Asin(Mathf.Clamp(cockpitForward.y, -1f, 1f)) * Mathf.Rad2Deg;

        for (var i = 0; i < _slicePitches.Count; i++)
        {
            var sliceTransform = _slicePitches[i].Key;
            if (sliceTransform == null) continue;
            if (rescale) sliceTransform.localScale = new Vector3(sliceScale, sliceScale, sliceScale);

            var visible = !hideAll && (showAll ||
                          Mathf.Abs(Mathf.DeltaAngle(_slicePitches[i].Value, currentPitch)) <= halfRange);

            if (sliceTransform.gameObject.activeSelf != visible)
            {
                sliceTransform.gameObject.SetActive(visible);
            }
        }
    }
    

    
    private void BuildSlices()
    {
        if (_hasBuiltSlices)
        {
            return;
        }
        _flightHud = GetComponent<global::FlightHud>();
        if (_flightHud == null)
        {
            Debug.LogWarning($"{nameof(PitchCompassBehavior)}: Could not find FlightHud on {name}");
            return;
        }

        if (!RefreshCockpitTransform())
        {
            Debug.LogWarning($"{nameof(PitchCompassBehavior)}: Could not find cockpitTransform");
            return;
        }

        _sourcePitchCompass = PitchCompassField.GetValue(_flightHud) as RawImage;
        if (_sourcePitchCompass == null)
        {
            Debug.LogWarning($"{nameof(PitchCompassBehavior)}: Could not find pitchCompass RawImage");
            return;
        }

        var sourceTexture = _sourcePitchCompass.texture;
        if (sourceTexture == null)
        {
            Debug.LogWarning($"{nameof(PitchCompassBehavior)}: pitchCompass has no texture");
            return;
        }

        var sourceRectTransform = _sourcePitchCompass.rectTransform;
        _fullTextureDisplayHeight = sourceRectTransform.rect.height / _sourcePitchCompass.uvRect.height;
        _sliceRoot = CreateSliceRoot(sourceRectTransform);
        _slicePitches.Clear();

        for (var sliceIndex = 0; sliceIndex < FullPitchStepCount; sliceIndex++)
        {
            var slice = CreateSliceImage(sourceTexture, sliceIndex);
            
            var pitchDegrees = 90f - sliceIndex * PitchStepDegrees;
            var oppositePitchDegrees = pitchDegrees + 180;
            var opposite = Instantiate(slice);
            
            slice.transform.Rotate(Vector3.right, pitchDegrees);
            slice.transform.Rotate(Vector3.forward, 180, Space.Self);
            slice.transform.position = slice.transform.forward * 1000f;
            
            opposite.transform.Rotate(Vector3.right, oppositePitchDegrees);
            opposite.transform.Rotate(Vector3.forward, 180, Space.Self);
            opposite.transform.position = opposite.transform.forward * 1000f;
            
            slice.transform.SetParent(_sliceRoot, true);
            opposite.transform.SetParent(_sliceRoot, true);

            var sliceScale = ModConfiguration.Instance?.PitchLadderScale.Value ?? 0.8f;
            slice.transform.localScale = new Vector3(sliceScale, sliceScale, sliceScale);
            opposite.transform.localScale = new Vector3(sliceScale, sliceScale, sliceScale);

            // The `opposite` clone is the copy that lands in FRONT of the pilot displaying this
            // pitch value (verified in-game: the +05 clone sits at +3.79 deg above boresight with
            // the aircraft at +1.21 deg pitch). The original ends up in the rear hemisphere.
            _slicePitches.Add(new KeyValuePair<Transform, float>(opposite.transform, pitchDegrees));
            _slicePitches.Add(new KeyValuePair<Transform, float>(slice.transform, oppositePitchDegrees));
        }
        
        LayerHelper.SetLayerRecursive(_sliceRoot, LayerHelper.GetVrUiLayer());

        _hasBuiltSlices = true;
        Debug.Log($"{nameof(PitchCompassBehavior)}: Split pitch compass into {SliceCount} slices");
    }

    private bool RefreshCockpitTransform()
    {
        if (_flightHud == null)
        {
            return false;
        }

        var cockpitTransform = CockpitTransformField.GetValue(_flightHud) as Transform;
        if (cockpitTransform == null)
        {
            return false;
        }

        _cockpitTransform = cockpitTransform;
        return true;
    }

    private RectTransform CreateSliceRoot(RectTransform sourceRectTransform)
    {
        var existing = _flightHud.transform.parent.Find(SliceRootName);
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }

        var root = new GameObject(SliceRootName, typeof(RectTransform)).GetComponent<RectTransform>();
        
        root.SetParent(_flightHud.transform, false);
        root.anchorMin = sourceRectTransform.anchorMin;
        root.anchorMax = sourceRectTransform.anchorMax;
        root.pivot = sourceRectTransform.pivot;
        root.anchoredPosition = sourceRectTransform.anchoredPosition;
        root.sizeDelta = new Vector2(sourceRectTransform.rect.width, _fullTextureDisplayHeight);
        root.localScale = sourceRectTransform.localScale;
        root.localRotation = sourceRectTransform.localRotation;
        root.SetSiblingIndex(sourceRectTransform.GetSiblingIndex() + 1);
        root.position = Vector3.zero;
        return root;
    }

    private GameObject CreateSliceImage(Texture sourceTexture, int sliceIndex)
    {
        if (sliceIndex == 0)
        {
            return CreateWrappedEndCapSliceImage(sourceTexture);
        }

        var normalizedPitchCenter = sliceIndex / (float)FullPitchStepCount;
        var normalizedSliceHeight = 1f / FullPitchStepCount;
        var normalizedSliceCenter = Mathf.Lerp(1f, 0f, normalizedPitchCenter);
        var normalizedSliceBottom = Mathf.Clamp01(normalizedSliceCenter - normalizedSliceHeight * 0.5f);
        var pitchDegrees = 90f - sliceIndex * PitchStepDegrees;

        var sliceObject = new GameObject($"PitchCompassSlice_{pitchDegrees:+00;-00;000}", typeof(RectTransform), typeof(RawImage));
        var sliceTransform = sliceObject.GetComponent<RectTransform>();
        sliceTransform.SetParent(_sliceRoot, false);
        sliceTransform.anchorMin = new Vector2(0.5f, 0f);
        sliceTransform.anchorMax = new Vector2(0.5f, 0f);
        sliceTransform.pivot = new Vector2(0.5f, 0.5f);
        sliceTransform.anchoredPosition = new Vector2(0f, normalizedSliceBottom * _fullTextureDisplayHeight);
        sliceTransform.sizeDelta = new Vector2(_sourcePitchCompass.rectTransform.rect.width, normalizedSliceHeight * _fullTextureDisplayHeight);

        var sliceImage = sliceObject.GetComponent<RawImage>();
        sliceImage.texture = sourceTexture;
        sliceImage.color = _sourcePitchCompass.color;
        sliceImage.material = _sourcePitchCompass.material;
        sliceImage.raycastTarget = false;
        sliceImage.uvRect = new Rect(0f, normalizedSliceBottom, 1f, normalizedSliceHeight);
        return sliceObject;
 
    } 

    private GameObject CreateWrappedEndCapSliceImage(Texture sourceTexture)
    {
        const int sliceIndex = 0;
        var normalizedSliceHeight = 1f / FullPitchStepCount;
        var normalizedHalfHeight = normalizedSliceHeight * 0.5f;
        var pitchDegrees = 90f - sliceIndex * PitchStepDegrees;

        var sliceObject = new GameObject($"PitchCompassSlice_{pitchDegrees:+00;-00;000}_Wrapped", typeof(RectTransform));
        var sliceTransform = sliceObject.GetComponent<RectTransform>();
        sliceTransform.SetParent(_sliceRoot, false);
        sliceTransform.anchorMin = new Vector2(0.5f, 0f);
        sliceTransform.anchorMax = new Vector2(0.5f, 0f);
        sliceTransform.pivot = new Vector2(0.5f, 0.5f);
        sliceTransform.anchoredPosition = Vector2.zero;
        sliceTransform.sizeDelta = new Vector2(_sourcePitchCompass.rectTransform.rect.width, normalizedSliceHeight * _fullTextureDisplayHeight);

        CreateWrappedEndCapHalf(sourceTexture, sliceTransform, "Top", 0f, 1f - normalizedHalfHeight, normalizedHalfHeight);
        CreateWrappedEndCapHalf(sourceTexture, sliceTransform, "Bottom", normalizedHalfHeight, 0f, normalizedHalfHeight);

        return sliceObject;
    }

    private void CreateWrappedEndCapHalf(
        Texture sourceTexture,
        RectTransform parent,
        string nameSuffix,
        float normalizedLocalBottom,
        float normalizedUvBottom,
        float normalizedHalfHeight)
    {
        var halfObject = new GameObject($"PitchCompassSlice_Wrapped_{nameSuffix}", typeof(RectTransform), typeof(RawImage));
        var halfTransform = halfObject.GetComponent<RectTransform>();
        halfTransform.SetParent(parent, false);
        halfTransform.anchorMin = new Vector2(0f, 0f);
        halfTransform.anchorMax = new Vector2(1f, 0f);
        halfTransform.pivot = new Vector2(0.5f, 0f);
        halfTransform.anchoredPosition = new Vector2(0f, normalizedLocalBottom * _fullTextureDisplayHeight);
        halfTransform.sizeDelta = new Vector2(0f, normalizedHalfHeight * _fullTextureDisplayHeight);

        var halfImage = halfObject.GetComponent<RawImage>();
        halfImage.texture = sourceTexture;
        halfImage.color = _sourcePitchCompass.color;
        halfImage.material = _sourcePitchCompass.material;
        halfImage.raycastTarget = false;
        halfImage.uvRect = new Rect(0f, normalizedUvBottom, 1f, normalizedHalfHeight);
    }
}
