using UnityEngine;
using UnityEngine.UI;

namespace NOVR.VrUi.SpecialBehavior;

public class NOVRGameplayUIBehaviour : UIRenderedCanvasBehavior
{
    private static readonly string[] ChatSpacerNames = { "LeftSpace", "MiddleSpace", "RightSpace" };

    private Image[] _chatSpacerImages;

    private bool _virtualMfdSearched;
    private bool _virtualMfdGroupAdded;
    private float _virtualMfdShownAlpha = 1f;
    private global::VirtualMFD _virtualMfd;
    private CanvasGroup _virtualMfdGroup;
    private global::MFDScreen[] _mfdScreens;
    private Image[] _mfdScreenFrames;
    private bool[] _mfdScreenFramesStockEnabled;

    private void Update()
    {
        PlaceCanvas();
        PlaceDialogueBox();
        HideChatLayoutSpacers();
        HideLeakedVirtualMfd();
    }

    private const float CanvasDistance = 3f;
    private const float CanvasScale = 0.003f;

    private bool _chatChecked;
    private bool _isChatCanvas;

    private bool _dialogueSearched;
    private Transform _dialogueBox;
    private Vector3 _dialogueBaseLocalPosition;
    private Vector3 _dialogueBaseLocalScale;

    // Every canvas this behaviour drives sits 3 m ahead at 0.003 scale. The one carrying MessageUI
    // (chat, mission messages, kill feed) can additionally be moved by angle and resized from the
    // tuning menu; it is turned to keep facing the pilot so it does not shear when moved off-centre.
    private void PlaceCanvas()
    {
        if (!_chatChecked)
        {
            _chatChecked = true;
            _isChatCanvas = TryGetComponent<global::MessageUI>(out _);
        }

        var config = ModConfiguration.Instance;
        if (!_isChatCanvas || config == null)
        {
            transform.localScale = new Vector3(CanvasScale, CanvasScale, CanvasScale);
            transform.position = new Vector3(0f, 0f, CanvasDistance);
            return;
        }

        var yaw = config.ChatHorizontalAngle.Value;
        var pitch = config.ChatVerticalAngle.Value;
        var scale = CanvasScale * config.ChatSize.Value;
        transform.localScale = new Vector3(scale, scale, scale);
        transform.position = new Vector3(
            Mathf.Tan(yaw * Mathf.Deg2Rad) * CanvasDistance,
            Mathf.Tan(pitch * Mathf.Deg2Rad) * CanvasDistance,
            CanvasDistance);
        // Face the actual eye, not the origin: on SteamVR the seated origin can sit well away
        // from the head, and an angle-only rotation then leaves the canvas turned away.
        transform.rotation = FacingEye(transform.position, Vector3.up);
    }

    internal static Quaternion FacingEye(Vector3 position, Vector3 up)
    {
        var camera = APIBus.CockpitHudCamera;
        var eye = camera != null ? camera.transform.position : Vector3.zero;
        var direction = position - eye;
        return direction.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(direction, up) : Quaternion.identity;
    }

    // The mission dialogue box is a child of GameplayUICanvas, which also carries the MFD and other
    // widgets, so it is offset inside the canvas instead of moving the whole canvas.
    private void PlaceDialogueBox()
    {
        if (!_dialogueSearched)
        {
            _dialogueSearched = true;
            if (TryGetComponent<global::GameplayUI>(out var gameplayUi) && gameplayUi.DialogueBox != null)
            {
                _dialogueBox = gameplayUi.DialogueBox.transform;
                _dialogueBaseLocalPosition = _dialogueBox.localPosition;
                _dialogueBaseLocalScale = _dialogueBox.localScale;
            }
        }

        var config = ModConfiguration.Instance;
        if (_dialogueBox == null || config == null) return;

        // Canvas pixels per metre at the canvas distance: tan(angle) * 3 m / 0.003.
        var pixelsAtDistance = CanvasDistance / CanvasScale;
        var offset = new Vector3(
            Mathf.Tan(config.DialogueHorizontalAngle.Value * Mathf.Deg2Rad) * pixelsAtDistance,
            Mathf.Tan(config.DialogueVerticalAngle.Value * Mathf.Deg2Rad) * pixelsAtDistance,
            0f);
        var size = config.DialogueSize.Value;
        var atDefaults = offset == Vector3.zero && Mathf.Approximately(size, 1f);
        if (atDefaults && !_dialogueMoved) return;

        _dialogueMoved = !atDefaults;
        _dialogueBox.localPosition = _dialogueBaseLocalPosition + offset;
        _dialogueBox.localScale = _dialogueBaseLocalScale * size;
    }

    private bool _dialogueMoved;

    // ChatCanvas/TopPanel lays its contents out with three HorizontalLayoutGroup spacers -
    // LeftSpace, MiddleSpace and RightSpace. Each carries an opaque white Image with no sprite,
    // which is harmless in the stock screen-space canvas but renders as a solid white panel once
    // this behaviour converts the canvas to world space on the VR UI layer: at 0.003 scale and 3 m
    // out, a 640x170 spacer is roughly 1.9 m wide, so the left and right ones sit in the pilot's
    // peripheral vision. Only the Image is disabled; the layout itself is untouched.
    private void HideChatLayoutSpacers()
    {
        if (ModConfiguration.Instance?.HideChatLayoutSpacers.Value != true) return;

        if (_chatSpacerImages == null)
        {
            var topPanel = FindChildStartingWith(transform, "TopPanel");
            if (topPanel == null) return;

            var found = new System.Collections.Generic.List<Image>(ChatSpacerNames.Length);
            foreach (var spacerName in ChatSpacerNames)
            {
                var spacer = FindChildStartingWith(topPanel, spacerName);
                if (spacer == null) continue;
                if (!spacer.TryGetComponent<Image>(out var spacerImage)) continue;

                found.Add(spacerImage);
            }

            if (found.Count == 0)
            {
                // Cache the miss as well: GameplayUICanvas has a TopPanel with no spacers, and without
                // this the whole canvas subtree was re-walked recursively every frame.
                _chatSpacerImages = System.Array.Empty<Image>();
                return;
            }
            _chatSpacerImages = found.ToArray();
        }

        // Re-asserted every frame rather than latched once: MessageUI rebuilds the chat bar as
        // messages arrive and re-enables these Images, which brought the white panels back.
        for (var index = 0; index < _chatSpacerImages.Length; index++)
        {
            var spacerImage = _chatSpacerImages[index];
            if (spacerImage != null && spacerImage.enabled)
            {
                spacerImage.enabled = false;
            }
        }
    }

    // GameplayUICanvas/VirtualMFD is the stock in-flight MFD (map/HUD/faction/mission screens with a
    // button column either side). The stock game "hides" it in two ways that both rely on the canvas
    // being a flat screen-space overlay: VirtualMFD.ToggleAllButtons(false) deactivates the six
    // Button children but leaves the LeftButtons/RightButtons column backgrounds (50x550 sliced
    // "Background" Images) active at +/-455 px, and MFDScreen.CloseScreen parks each screen at
    // localPosition +/-Screen.width (2560 px here) with its white 450x650 "UISprite" frame Image still
    // enabled. Off-screen on a monitor; on this world-space canvas (0.003 scale, 3 m out) those are
    // real quads 1.4 m and 9.2 m to each side of the pilot that follow the aircraft on every type.
    // Two fixes, both re-asserted every frame because the stock event handlers rewrite this state
    // whenever DynamicMap is maximized/minimized:
    //  1. While the map is not maximized (the only state in which the stock game shows the MFD) a
    //     CanvasGroup on the VirtualMFD root is held at alpha 0 so nothing under it draws or
    //     intercepts VR cursor raycasts. Toggling the map (Rewired "Map", default M) still shows it.
    //  2. Each closed MFDScreen's own frame Image is disabled while MFDScreen.isActive is false, so
    //     the parked frames stay hidden even with the map maximized. The prefab's enabled state is
    //     recorded on first sight and restored when the screen opens, so an open screen looks stock.
    private void HideLeakedVirtualMfd()
    {
        var config = ModConfiguration.Instance;
        if (config == null) return;

        var hideWhenMapClosed = config.HideVirtualMfdWhenMapClosed.Value;
        var hideParkedScreens = config.HideParkedMfdScreens.Value;
        // Both off and nothing ever hidden: skip the lookup. If something was hidden earlier the
        // pass still runs so a live config change restores the stock state.
        if (!hideWhenMapClosed && !hideParkedScreens && _virtualMfd == null) return;

        if (!TryFindVirtualMfd()) return;

        ApplyVirtualMfdVisibility(hidden: hideWhenMapClosed && !global::DynamicMap.mapMaximized);
        ApplyMfdScreenFrameVisibility(hideParkedScreens);
    }

    // Searched once: VirtualMFD is baked into the GameplayUICanvas prefab, and the other canvases
    // this behaviour is attached to (ChatCanvas, MenuCanvas, MaximizedMapCanvas) never get one, so a
    // per-frame GetComponentInChildren on those would be wasted work.
    private bool TryFindVirtualMfd()
    {
        if (_virtualMfdSearched) return _virtualMfd != null;
        _virtualMfdSearched = true;

        _virtualMfd = GetComponentInChildren<global::VirtualMFD>(true);
        if (_virtualMfd == null) return false;

        if (_virtualMfd.TryGetComponent<CanvasGroup>(out var existingGroup))
        {
            _virtualMfdGroup = existingGroup;
            _virtualMfdShownAlpha = existingGroup.alpha;
        }

        _mfdScreens = _virtualMfd.GetComponentsInChildren<global::MFDScreen>(true);
        _mfdScreenFrames = new Image[_mfdScreens.Length];
        _mfdScreenFramesStockEnabled = new bool[_mfdScreens.Length];
        for (var index = 0; index < _mfdScreens.Length; index++)
        {
            if (!_mfdScreens[index].TryGetComponent<Image>(out var frame)) continue;
            _mfdScreenFrames[index] = frame;
            _mfdScreenFramesStockEnabled[index] = frame.enabled;
        }

        return true;
    }

    private void ApplyVirtualMfdVisibility(bool hidden)
    {
        if (_virtualMfdGroup == null)
        {
            // Only add the group when there is something to hide; a shown MFD needs no changes.
            if (!hidden) return;

            _virtualMfdGroup = _virtualMfd.gameObject.AddComponent<CanvasGroup>();
            _virtualMfdGroupAdded = true;
            _virtualMfdShownAlpha = 1f;
        }

        var alpha = hidden ? 0f : _virtualMfdShownAlpha;
        if (!Mathf.Approximately(_virtualMfdGroup.alpha, alpha))
        {
            _virtualMfdGroup.alpha = alpha;
        }

        // Raycast/interaction flags are only ours to touch on a group we created; a stock group's
        // flags are left as authored.
        if (!_virtualMfdGroupAdded) return;
        if (_virtualMfdGroup.blocksRaycasts == hidden) _virtualMfdGroup.blocksRaycasts = !hidden;
        if (_virtualMfdGroup.interactable == hidden) _virtualMfdGroup.interactable = !hidden;
    }

    private void ApplyMfdScreenFrameVisibility(bool hideParkedScreens)
    {
        if (_mfdScreens == null) return;

        for (var index = 0; index < _mfdScreens.Length; index++)
        {
            var screen = _mfdScreens[index];
            var frame = _mfdScreenFrames[index];
            if (screen == null || frame == null) continue;

            var shouldDraw = _mfdScreenFramesStockEnabled[index] && (!hideParkedScreens || screen.isActive);
            if (frame.enabled != shouldDraw)
            {
                frame.enabled = shouldDraw;
            }
        }
    }
}
