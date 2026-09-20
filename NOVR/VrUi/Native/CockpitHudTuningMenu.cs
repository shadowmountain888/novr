using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NOVR.VrUi.Native;

/// <summary>
/// An in-cockpit tuning menu: press the toggle key (default F8) while seated and a panel of sliders
/// appears in front of you for the HUD, the helmet side panels and the seat position. Every entry
/// it edits already applies live, so dragging a slider changes what you see immediately; the config
/// file is written when the menu closes.
///
/// Interaction is the ordinary mouse. NOVR's VR cursor is only drawn while the real OS cursor is
/// visible, and the game locks and hides it in flight, so while the menu is open the cursor is
/// unlocked and shown every frame (the game re-locks it otherwise) and restored on close. The VR
/// cursor's projection is pointed at the panel the same way the native main menu does it.
/// Lives on HUDCanvas for lifetime only; the panel itself is a separate root so it is not affected
/// by HUD Scale or the HUD's 1000-unit placement.
/// </summary>
public class CockpitHudTuningMenu : MonoBehaviour
{
    private const float CanvasScale = 0.0011f;
    private const float PanelDistance = 1.15f;
    private const float PanelWidth = 1000f;
    private const float RowHeight = 54f;
    private const float HeaderHeight = 40f;

    private static readonly Color PanelColor = new(0.04f, 0.05f, 0.06f, 0.94f);
    private static readonly Color RowColor = new(0.09f, 0.10f, 0.115f, 0.85f);
    private static readonly Color TrackColor = new(0.20f, 0.23f, 0.25f, 1f);
    private static readonly Color FillColor = new(0.16f, 0.55f, 0.30f, 1f);
    private static readonly Color HandleColor = new(0.85f, 0.92f, 0.88f, 1f);
    private static readonly Color ButtonColor = new(0.24f, 0.29f, 0.31f, 0.96f);
    private static readonly Color CloseColor = new(0.62f, 0.12f, 0.14f, 0.96f);
    private static readonly Color HeaderTextColor = new(0.84f, 0.90f, 0.92f, 1f);

    private readonly List<Action> _refreshers = new();
    private readonly List<Action> _resetters = new();

    private GameObject _root;
    private Canvas _canvas;
    private Font _font;
    private bool _open;
    private Key _toggleKey = Key.F8;
    private Vector3 _anchorPosition;
    private Vector3 _anchorForward = Vector3.forward;
    private CursorLockMode _previousLockState;
    private bool _previousCursorVisible;
    private float _cursorY;

    private void Start()
    {
        var keyName = ModConfiguration.Instance?.TuningMenuKey.Value ?? "F8";
        if (!Enum.TryParse(keyName, true, out _toggleKey) || _toggleKey == Key.None)
        {
            Debug.LogWarning($"[NOVR] Tuning menu key '{keyName}' is not a valid InputSystem Key name; using F8.");
            _toggleKey = Key.F8;
        }

        Debug.Log($"[NOVR] Cockpit HUD tuning menu ready - press {_toggleKey} in the cockpit.");
    }

    private void OnDestroy()
    {
        if (_open) Close();
        if (_root != null) Destroy(_root);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[_toggleKey].wasPressedThisFrame)
        {
            if (_open) Close();
            else Open();
        }
    }

    // After the game's own Update/LateUpdate work, which is where it re-locks the cursor.
    private void LateUpdate()
    {
        if (!_open) return;

        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;

        if (_canvas != null && _canvas.worldCamera != APIBus.CockpitHudCamera)
        {
            _canvas.worldCamera = APIBus.CockpitHudCamera;
        }
    }

    private void Open()
    {
        if (ModConfiguration.Instance == null || APIBus.CockpitHudReference == null) return;

        EnsureBuilt();
        CaptureAnchor();
        ApplyPlacement();
        for (var index = 0; index < _refreshers.Count; index++) _refreshers[index]();

        _previousLockState = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        _root.SetActive(true);
        _open = true;
    }

    private void Close()
    {
        _open = false;
        if (_root != null) _root.SetActive(false);

        VrUiCursor.I?.ClearProjectionReferenceRotation();
        Cursor.lockState = _previousLockState;
        Cursor.visible = _previousCursorVisible;

        ModConfiguration.Instance?.Config.Save();
    }

    // Same anchoring idea as NativeVrUiRoot.CaptureMenuAnchor: level with the horizon, relative to
    // where the head was facing when the menu was opened. Captured once per open so that dragging
    // the menu's own placement sliders moves the panel instead of it chasing the head.
    private void CaptureAnchor()
    {
        var reference = APIBus.CockpitHudReference.transform;
        var forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        _anchorForward = forward.normalized;
        _anchorPosition = reference.position;
    }

    // Off to one side and small by default so it does not sit on top of the HUD being tuned. The
    // panel is turned to face the pilot, and the VR cursor's projection follows it.
    private void ApplyPlacement()
    {
        if (_root == null) return;

        var config = ModConfiguration.Instance;
        var sideAngle = config?.TuningMenuSideAngle.Value ?? 35f;
        var heightAngle = config?.TuningMenuHeightAngle.Value ?? -5f;
        var size = config?.TuningMenuSize.Value ?? 0.6f;

        var baseRotation = Quaternion.LookRotation(_anchorForward, Vector3.up);
        var direction = baseRotation * (Quaternion.Euler(-heightAngle, sideAngle, 0f) * Vector3.forward);
        var rotation = Quaternion.LookRotation(direction, Vector3.up);

        _root.transform.SetPositionAndRotation(_anchorPosition + direction * PanelDistance, rotation);
        _root.transform.localScale = Vector3.one * (CanvasScale * size);

        VrUiCursor.I?.SetProjectionReferenceRotation(rotation);
    }

    private void EnsureBuilt()
    {
        if (_root != null) return;

        _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        var config = ModConfiguration.Instance;

        _root = new GameObject("NOVR Cockpit HUD Tuning Menu");
        var rootRect = _root.AddComponent<RectTransform>();
        rootRect.pivot = new Vector2(0.5f, 0.5f);

        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = APIBus.CockpitHudCamera;
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = 6000;
        _root.AddComponent<GraphicRaycaster>();

        // Build top-down; the panel height is whatever the rows add up to.
        var rows = new List<Action<RectTransform>>
        {
            panel => AddHeader(panel, "HUD"),
            panel => AddSlider(panel, "HUD SIZE", config.HudScale),
            panel => AddSlider(panel, "COMPASS HEIGHT", config.CompassVerticalOffset),
            panel => AddSlider(panel, "PITCH LADDER RANGE (deg)", config.PitchLadderVisibleRange, 5f),
            panel => AddSlider(panel, "PITCH LADDER SIZE *", config.PitchLadderScale),
            panel => AddHeader(panel, "HELMET SIDE PANELS  (weapons right, map left)"),
            panel => AddSlider(panel, "DISTANCE FROM CENTRE (deg)", config.SidePanelHorizontalAngle),
            panel => AddSlider(panel, "WEAPON PANEL HEIGHT (deg)", config.WeaponPanelVerticalAngle),
            panel => AddSlider(panel, "MAP PANEL HEIGHT (deg)", config.MapPanelVerticalAngle),
            panel => AddSlider(panel, "PANEL SIZE", config.SidePanelSize),
            panel => AddToggle(panel, "PANEL BACKGROUNDS", config.SidePanelBackgrounds),
            panel => AddHeader(panel, "SEAT"),
            panel => AddSlider(panel, "SEAT FORWARD / BACK (m)", config.CockpitSeatForwardOffset),
            panel => AddSlider(panel, "SEAT UP / DOWN (m)", config.CockpitSeatHeightOffset),
            panel => AddSlider(panel, "EXTERNAL VIEW DISTANCE (m)", config.ExternalViewDistance),
            panel => AddHeader(panel, "THIS MENU"),
            panel => AddSlider(panel, "MENU SIZE", config.TuningMenuSize, 0f, ApplyPlacement),
            panel => AddSlider(panel, "MENU LEFT / RIGHT (deg)", config.TuningMenuSideAngle, 1f, ApplyPlacement),
            panel => AddSlider(panel, "MENU UP / DOWN (deg)", config.TuningMenuHeightAngle, 1f, ApplyPlacement),
        };

        var headerCount = 4;
        var contentHeight = headerCount * HeaderHeight + (rows.Count - headerCount) * RowHeight;
        var panelHeight = contentHeight + 150f;
        rootRect.sizeDelta = new Vector2(PanelWidth, panelHeight);

        var panelRect = CreateRect("Panel", rootRect, Vector2.zero, new Vector2(PanelWidth, panelHeight));
        panelRect.gameObject.AddComponent<Image>().color = PanelColor;

        CreateText("Title", panelRect, "NOVR  -  HUD & SEAT TUNING", new Vector2(0f, panelHeight * 0.5f - 30f), new Vector2(PanelWidth - 60f, 34f), 22, TextAnchor.MiddleCenter, Color.white);

        _cursorY = panelHeight * 0.5f - 66f;
        for (var index = 0; index < rows.Count; index++) rows[index](panelRect);

        var footerY = -panelHeight * 0.5f + 40f;
        CreateButton(panelRect, "RESET ALL", new Vector2(-250f, footerY), new Vector2(200f, 42f), ButtonColor, ResetAll);
        CreateButton(panelRect, $"CLOSE ({_toggleKey})", new Vector2(250f, footerY), new Vector2(200f, 42f), CloseColor, Close);
        CreateText("Hint", panelRect, "* applies when you next enter a cockpit.  Everything else is live.  Saved on close.", new Vector2(0f, footerY + 40f), new Vector2(PanelWidth - 60f, 24f), 13, TextAnchor.MiddleCenter, HeaderTextColor);

        LayerHelper.SetLayerRecursive(_root.transform, LayerHelper.GetVrUiLayer());
        _root.SetActive(false);
    }

    private void ResetAll()
    {
        for (var index = 0; index < _resetters.Count; index++) _resetters[index]();
        for (var index = 0; index < _refreshers.Count; index++) _refreshers[index]();
    }

    private void AddHeader(RectTransform panel, string title)
    {
        _cursorY -= HeaderHeight * 0.5f;
        CreateText(title + " Header", panel, title, new Vector2(0f, _cursorY), new Vector2(PanelWidth - 80f, 26f), 16, TextAnchor.MiddleLeft, HeaderTextColor);
        _cursorY -= HeaderHeight * 0.5f;
    }

    private RectTransform AddRow(RectTransform panel, string label)
    {
        _cursorY -= RowHeight * 0.5f;
        var row = CreateRect(label + " Row", panel, new Vector2(0f, _cursorY), new Vector2(PanelWidth - 60f, RowHeight - 8f));
        row.gameObject.AddComponent<Image>().color = RowColor;
        CreateText(label + " Label", row, label, new Vector2(-310f, 0f), new Vector2(300f, 30f), 15, TextAnchor.MiddleLeft, Color.white);
        _cursorY -= RowHeight * 0.5f;
        return row;
    }

    // step > 0 snaps the value to multiples of step (the ladder range moves in 5 degree increments).
    private void AddSlider(RectTransform panel, string label, ConfigEntry<float> entry, float step = 0f, Action onChanged = null)
    {
        var row = AddRow(panel, label);

        var min = 0f;
        var max = 1f;
        if (entry.Description?.AcceptableValues is AcceptableValueRange<float> range)
        {
            min = range.MinValue;
            max = range.MaxValue;
        }

        var valueText = CreateText(label + " Value", row, "", new Vector2(330f, 0f), new Vector2(110f, 30f), 15, TextAnchor.MiddleCenter, Color.white);
        var slider = CreateSlider(row, new Vector2(60f, 0f), new Vector2(400f, 26f), min, max);

        var format = step >= 1f || max - min > 50f ? "0" : "0.00";
        var refreshing = false;
        void Refresh()
        {
            refreshing = true;
            slider.value = entry.Value;
            valueText.text = entry.Value.ToString(format);
            refreshing = false;
        }

        slider.onValueChanged.AddListener(value =>
        {
            if (refreshing) return;
            if (step > 0f)
            {
                var snapped = Mathf.Clamp(Mathf.Round(value / step) * step, min, max);
                if (!Mathf.Approximately(snapped, value))
                {
                    refreshing = true;
                    slider.value = snapped;
                    refreshing = false;
                }
                value = snapped;
            }
            entry.Value = value;
            valueText.text = value.ToString(format);
            onChanged?.Invoke();
        });

        var defaultValue = (float)entry.DefaultValue;
        CreateButton(row, "RESET", new Vector2(425f, 0f), new Vector2(64f, 30f), ButtonColor, () =>
        {
            entry.Value = defaultValue;
            Refresh();
            onChanged?.Invoke();
        }, 11);

        _refreshers.Add(Refresh);
        _resetters.Add(() => entry.Value = defaultValue);
    }

    private void AddToggle(RectTransform panel, string label, ConfigEntry<bool> entry)
    {
        var row = AddRow(panel, label);
        Text buttonText = null;
        Button button = null;

        void Refresh()
        {
            if (buttonText != null) buttonText.text = entry.Value ? "ON" : "OFF";
            if (button != null) NativeButtonFeedback.SetNormalColor(button, entry.Value ? FillColor : CloseColor);
        }

        button = CreateButton(row, "ON", new Vector2(60f, 0f), new Vector2(140f, 32f), FillColor, () =>
        {
            entry.Value = !entry.Value;
            Refresh();
        });
        buttonText = button.GetComponentInChildren<Text>();

        var defaultValue = (bool)entry.DefaultValue;
        _refreshers.Add(Refresh);
        _resetters.Add(() => entry.Value = defaultValue);
    }

    private static RectTransform CreateRect(string objectName, RectTransform parent, Vector2 anchoredPosition, Vector2 size)
    {
        var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        return rect;
    }

    private Text CreateText(string objectName, RectTransform parent, string content, Vector2 anchoredPosition, Vector2 size, int fontSize, TextAnchor alignment, Color color)
    {
        var rect = CreateRect(objectName, parent, anchoredPosition, size);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = _font;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.text = content;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        return text;
    }

    private Button CreateButton(RectTransform parent, string label, Vector2 anchoredPosition, Vector2 size, Color color, Action onClick, int fontSize = 14)
    {
        var rect = CreateRect(label + " Button", parent, anchoredPosition, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());
        NativeButtonFeedback.Configure(button, color);
        CreateText(label + " Text", rect, label, Vector2.zero, size, fontSize, TextAnchor.MiddleCenter, Color.white);
        return button;
    }

    // A standard UnityEngine.UI.Slider assembled by hand (there is no prefab to instantiate).
    private static Slider CreateSlider(RectTransform parent, Vector2 anchoredPosition, Vector2 size, float min, float max)
    {
        var root = CreateRect("Slider", parent, anchoredPosition, size);

        var track = CreateRect("Track", root, Vector2.zero, new Vector2(size.x, 8f));
        track.gameObject.AddComponent<Image>().color = TrackColor;

        var fillArea = CreateRect("Fill Area", root, Vector2.zero, new Vector2(size.x, 8f));
        var fill = new GameObject("Fill", typeof(RectTransform)).GetComponent<RectTransform>();
        fill.SetParent(fillArea, false);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.sizeDelta = Vector2.zero;
        fill.gameObject.AddComponent<Image>().color = FillColor;

        var handleArea = CreateRect("Handle Slide Area", root, Vector2.zero, new Vector2(size.x - 20f, size.y));
        var handle = new GameObject("Handle", typeof(RectTransform)).GetComponent<RectTransform>();
        handle.SetParent(handleArea, false);
        handle.sizeDelta = new Vector2(20f, 0f);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = HandleColor;

        // A transparent full-size hit area so clicking anywhere on the row's slider jumps the handle.
        var hitArea = root.gameObject.AddComponent<Image>();
        hitArea.color = new Color(0f, 0f, 0f, 0f);

        var slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = false;
        return slider;
    }
}
