using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOVR.VrUi.HarmonyPatches;

/// <summary>
/// Re-projects the objective / waypoint HUD markers (arrow, dot, size ring and label) onto the VR
/// HUD sphere, the same way HUDUnitMarkerViewPositionPatch does for unit markers.
///
/// Why this is needed: NOVR turns HUDCanvas into a world-space canvas pinned at (0, 0, 1000) and
/// renders it with the head-slaved VrCockpitHudCamera. Stock ObjectiveOverlay.UpdateOverlay is
/// written for the screen-space canvas: it computes a SCREEN-PIXEL position via
/// mainCamera.WorldToScreenPoint and writes it straight into transform.position of the pointer,
/// dot and size indicator, and ObjectiveOverlayManager.StopTextOverlap then writes a screen-pixel
/// Vector2 (z = 0) into the label's transform.position every frame. On a world-space canvas those
/// are absurd world coordinates near the world origin with no billboarding, which is what the
/// player sees as "warped and hard to see" waypoint markers. Every other HUD marker family already
/// has a NOVR projection patch; this file adds the missing one for objective overlays.
///
/// Scope: ObjectiveOverlay.UpdateOverlay is also called every frame by the mission editor's
/// WaypointObjectiveHandle, whose overlays live on a stock screen-space canvas where the stock
/// pixel maths is correct. Both prefixes therefore only take over when the overlay's canvas is a
/// world-space canvas rendered by the cockpit HUD camera (i.e. NOVR's converted HUDCanvas) and
/// fall through to stock everywhere else.
///
/// HideOverlay is deliberately left alone: it only toggles enabled flags.
/// </summary>
internal static class ObjectiveOverlayViewPositionPatch
{
    // Stock rule: show the arrow instead of the dot once the target is more than this far off the view axis.
    private const float PointerAngleDegrees = 10.0f;

    // Stock places the label 25 canvas units below the dot.
    private const float LabelBelowDotCanvasUnits = 25.0f;

    // ObjectiveOverlay private serialized fields.
    private static readonly FieldInfo ObjectivePointerField = AccessTools.Field(typeof(global::ObjectiveOverlay), "objectivePointer");
    private static readonly FieldInfo ObjectiveDotField = AccessTools.Field(typeof(global::ObjectiveOverlay), "objectiveDot");
    private static readonly FieldInfo SizeIndicatorField = AccessTools.Field(typeof(global::ObjectiveOverlay), "sizeIndicator");
    private static readonly FieldInfo ObjectiveInfoField = AccessTools.Field(typeof(global::ObjectiveOverlay), "objectiveInfo");
    private static readonly FieldInfo PointerTailField = AccessTools.Field(typeof(global::ObjectiveOverlay), "pointerTail");
    private static readonly FieldInfo HiddenField = AccessTools.Field(typeof(global::ObjectiveOverlay), "hidden");

    // ObjectiveOverlayManager private fields used by the label anti-overlap pass.
    private static readonly FieldInfo OverlaysField = AccessTools.Field(typeof(global::ObjectiveOverlayManager), "overlays");
    private static readonly FieldInfo TextDistanceField = AccessTools.Field(typeof(global::ObjectiveOverlayManager), "textDistance");
    private static readonly FieldInfo TextPushField = AccessTools.Field(typeof(global::ObjectiveOverlayManager), "textPush");
    private static readonly FieldInfo TextNudgeDecreaseField = AccessTools.Field(typeof(global::ObjectiveOverlayManager), "textNudgeDecrease");

    // FieldInfo.SetValue takes an object; a cached box avoids allocating one per overlay per frame.
    private static readonly object BoxedFalse = false;

    private static Image? GetObjectivePointer(global::ObjectiveOverlay overlay) => ObjectivePointerField.GetValue(overlay) as Image;
    private static Image? GetObjectiveDot(global::ObjectiveOverlay overlay) => ObjectiveDotField.GetValue(overlay) as Image;
    private static Image? GetSizeIndicator(global::ObjectiveOverlay overlay) => SizeIndicatorField.GetValue(overlay) as Image;
    // TMP_Text rather than TextMeshProUGUI so a future base-type change in the game does not break the cast.
    private static TMP_Text? GetObjectiveInfo(global::ObjectiveOverlay overlay) => ObjectiveInfoField.GetValue(overlay) as TMP_Text;
    private static Transform? GetPointerTail(global::ObjectiveOverlay overlay) => PointerTailField.GetValue(overlay) as Transform;

    // Null-tolerant: before ModConfiguration is constructed (or if the entry is missing) the patch is on.
    private static bool IsEnabled() => ModConfiguration.Instance?.ProjectObjectiveOverlaysInVr.Value ?? true;

    /// <summary>
    /// True only for NOVR's converted HUD canvas: world-space and rendered by the cockpit HUD camera
    /// (UIRenderedCanvasBehavior sets both). The mission editor's waypoint overlays sit on a stock
    /// screen-space canvas and must keep the stock behaviour.
    /// </summary>
    private static bool IsVrHudCanvas(Canvas? canvas, Camera cockpitHudCamera) =>
        canvas != null && canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == cockpitHudCamera;

    [HarmonyPatch(typeof(global::ObjectiveOverlay), nameof(global::ObjectiveOverlay.UpdateOverlay))]
    private static class UpdateOverlayPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(global::ObjectiveOverlay __instance, MissionPosition.PositionResult result)
        {
            if (!IsEnabled())
                return true;

            var mainCamera = APIBus.MainCamera;
            var cockpitHudCamera = APIBus.CockpitHudCamera;
            if (mainCamera == null || cockpitHudCamera == null)
                return true;

            var objectivePointer = GetObjectivePointer(__instance);
            var objectiveDot = GetObjectiveDot(__instance);
            var sizeIndicator = GetSizeIndicator(__instance);
            var objectiveInfo = GetObjectiveInfo(__instance);
            var pointerTail = GetPointerTail(__instance);
            if (objectivePointer == null || objectiveDot == null || sizeIndicator == null || objectiveInfo == null || pointerTail == null)
                return true; // Nothing mutated yet, so stock is still a valid fallback.

            // Not on the VR HUD canvas (mission editor, or HUDCanvas not converted yet): stock is correct there.
            if (!IsVrHudCanvas(objectivePointer.canvas, cockpitHudCamera))
                return true;

            EnsureHudSiblings(objectivePointer, objectiveDot, sizeIndicator, objectiveInfo);

            // Stock: hidden = false; objectivePointer.enabled = true; objectiveInfo.enabled = true.
            // The 'hidden' field is private and HideOverlay() is a no-op while it is true, so it MUST be
            // reset here or a marker that was hidden once could never be hidden again.
            HiddenField.SetValue(__instance, BoxedFalse);
            objectiveInfo.enabled = true;

            var cameraTransform = cockpitHudCamera.transform;
            var worldPosition = result.Position.ToLocalPosition();

            // Stock off-screen test: > 90 degrees from the view axis OR outside the screen rectangle.
            // PinToScreenEdge is the VR equivalent (behind the head OR outside the 50x50 degree ellipse)
            // and also hands back the edge-pinned sphere point plus the outward arrow angle.
            // The stock 50 degree window is a monitor-sized cone that follows the head, so a waypoint only
            // slightly off-axis was dragged around by every glance. Use a headset-sized cone instead.
            var edgeCone = (ModConfiguration.Instance?.ObjectiveMarkerEdgeCone.Value ?? 90.0f) * 0.5f;
            var offScreen = VrHudProjection.PinToScreenEdge(worldPosition, out var hudPosition, out var arrowAngle, edgeCone, edgeCone * 0.85f);
            if (!offScreen && VrHudProjection.TryProjectToCockpitHud(worldPosition, out var projectedHudPosition))
                hudPosition = projectedHudPosition;

            // Stock rule kept verbatim: arrow when the target is more than 10 degrees off the view axis, dot otherwise.
            // Stock swaps the dot for an arrow beyond 10 degrees from the view axis. With a head-driven
            // view axis that flips the symbol on every glance, so the arrow is kept for edge-pinned only.
            var showPointer = offScreen;

            // Face the eye from where the marker actually sits on the HUD sphere. Copying the head
            // rotation keeps the quad parallel to the view plane, which shears it off-axis and makes it
            // visibly swivel as the head turns.
            var hudRotation = Quaternion.LookRotation(hudPosition - cameraTransform.position, cameraTransform.up);

            objectivePointer.transform.position = hudPosition;
            objectiveDot.transform.position = hudPosition;
            sizeIndicator.transform.position = hudPosition;
            objectiveDot.transform.rotation = hudRotation;
            sizeIndicator.transform.rotation = hudRotation;
            objectiveInfo.transform.rotation = hudRotation;

            objectivePointer.enabled = showPointer;
            objectiveDot.enabled = !showPointer;
            sizeIndicator.enabled = !offScreen;

            Vector3 labelPosition;
            if (showPointer)
            {
                OrientPointer(objectivePointer.transform, arrowAngle, cameraTransform);
                // pointerTail is a child of the pointer, so its world position is only valid after the
                // pointer's position and rotation are set (same idiom as CombatHUD.targetArrowTail).
                labelPosition = pointerTail.position;
            }
            else
            {
                objectivePointer.transform.rotation = hudRotation;
                // Stock: dot position - Vector3.up * 25 canvas px. Canvas px -> world units via the parent's
                // lossy scale (HUDCanvas is ~0.67 with the default HUD Scale), and 'down' is the head's down.
                labelPosition = hudPosition - cameraTransform.up * (LabelBelowDotCanvasUnits * GetParentScale(objectiveInfo.transform));
            }
            objectiveInfo.transform.position = labelPosition;

            // Keep TextNoOverlap coherent. Its fields are screen-pixel Vector2s for the stock anti-overlap
            // pass, which StopTextOverlapPatch replaces below; we keep TargetPosition/PreviousPosition in
            // our own view-plane units so that, should the stock pass ever run (config toggled off at
            // runtime), the lerp converges from a sane value instead of dragging the label across the map.
            var textNoOverlap = __instance.TextNoOverlap;
            if (textNoOverlap != null)
            {
                var planar = ToViewPlane(labelPosition, cameraTransform, GetParentScale(objectiveInfo.transform));
                textNoOverlap.TargetPosition = planar;
                textNoOverlap.PreviousPosition = planar;
                textNoOverlap.AutomaticlalySetPosition = false;
            }

            UpdateSizeIndicator(sizeIndicator, result);

            var name = result.Objective != null ? result.Objective.SavedObjective.DisplayName : "Waypoint";
            objectiveInfo.text = name + " " + UnitConverter.DistanceReading(result.Distance);
            objectiveInfo.fontSize = (int)PlayerSettings.overlayTextSize;
            return false;
        }
    }

    /// <summary>
    /// Stock StopTextOverlap ends with 'Text.transform.position = (Vector2)v' for EVERY overlay, every
    /// frame, from screen-pixel coordinates - a Vector2 -> Vector3 conversion that lands the label at
    /// world (x_px, y_px, 0), i.e. a few hundred metres from the world origin with z = 0. It runs right
    /// after UpdateOverlays() inside ObjectiveOverlayManager.Update, so anything the prefix above sets
    /// would be overwritten one call later. Skip it entirely in VR and redo the pairwise label nudge in
    /// the HUD view plane instead, reusing the manager's own tuning values.
    /// </summary>
    [HarmonyPatch(typeof(global::ObjectiveOverlayManager), "StopTextOverlap")]
    private static class StopTextOverlapPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(global::ObjectiveOverlayManager __instance)
        {
            if (!IsEnabled())
                return true;

            var mainCamera = APIBus.MainCamera;
            var cockpitHudCamera = APIBus.CockpitHudCamera;
            if (mainCamera == null || cockpitHudCamera == null)
                return true;

            // Same gate as UpdateOverlayPatch: the manager sits under HUDCanvas, so this is false only
            // before NOVR has converted the canvas, and then stock must keep running.
            if (!IsVrHudCanvas(__instance.GetComponentInParent<Canvas>(), cockpitHudCamera))
                return true;

            if (OverlaysField != null && OverlaysField.GetValue(__instance) is IList overlays)
                NudgeLabelsApart(overlays, __instance, cockpitHudCamera.transform);

            return false;
        }
    }

    /// <summary>
    /// ObjectiveOverlay.Initialize reparents only objectivePointer (with its pointerTail child) and
    /// objectiveInfo to HUDCanvas/IconLayer. objectiveDot and sizeIndicator stay under the
    /// ObjectiveOverlay(Clone) beneath ObjectiveOverlayManager, whose RectTransform localScale is
    /// (1, 1, 0) in the stock prefab. That z = 0 is harmless on a flat screen-space canvas, but on the
    /// world-space HUD it flattens any head-slaved child along world Z and collapses transform.position
    /// writes onto the z = 1000 plane instead of the HUD sphere. Move them next to the pointer once,
    /// keeping local values (worldPositionStays = false) so the zero scale is not baked into the child.
    /// Also copy IconLayer's layer so the parts render on the VR UI layer immediately rather than after
    /// the next periodic layer refresh.
    /// </summary>
    private static void EnsureHudSiblings(Image objectivePointer, Image objectiveDot, Image sizeIndicator, TMP_Text objectiveInfo)
    {
        var iconLayer = objectivePointer.transform.parent;
        if (iconLayer == null)
            return;

        if (objectiveDot.transform.parent != iconLayer)
            objectiveDot.transform.SetParent(iconLayer, false);
        if (sizeIndicator.transform.parent != iconLayer)
            sizeIndicator.transform.SetParent(iconLayer, false);

        var layer = iconLayer.gameObject.layer;
        if (objectivePointer.gameObject.layer != layer)
        {
            LayerHelper.SetLayerRecursive(objectivePointer.transform, (LayerHelper.Layers)layer);
            objectiveDot.gameObject.layer = layer;
            sizeIndicator.gameObject.layer = layer;
            objectiveInfo.gameObject.layer = layer;
        }
    }

    /// <summary>
    /// Stock spins the arrow about screen Z so sprite +Y points from the screen centre toward the target
    /// (localEulerAngles.z = atan2(y, x) - 90, with a sign flip for targets behind the camera). The VR
    /// equivalent is a spin about the head's view axis: LookRotation(viewForward, up) with 'up' the
    /// outward direction in the (right, up) view plane. PinToScreenEdge already gives that direction as
    /// atan2(pitch, yaw), which is consistent with where it pins the marker and stays correct for targets
    /// behind the head - unlike a 'targetSpherePoint - markerSpherePoint' vector, whose view-plane
    /// projection folds back toward the centre once the target is behind.
    /// </summary>
    private static void OrientPointer(Transform pointer, float arrowAngle, Transform cameraTransform)
    {
        var desiredUp = cameraTransform.right * Mathf.Cos(arrowAngle) + cameraTransform.up * Mathf.Sin(arrowAngle);
        if (desiredUp.sqrMagnitude <= Mathf.Epsilon)
            desiredUp = cameraTransform.up;
        pointer.rotation = Quaternion.LookRotation(pointer.position - cameraTransform.position, desiredUp.normalized);
    }

    /// <summary>
    /// Stock: localScale = (canvasHeight / rectHeight) / tan(fov / 2) * range / distance, which is
    /// (diameter in canvas px) / rectHeight for a screen-space canvas and the desktop FOV. In VR the
    /// desktop FOV is meaningless (mainCamera.fieldOfView is the HMD eye FOV and NOVR blocks writes to
    /// it), so size the ring directly on the HUD sphere: a target of radius 'range' at 'distance' spans
    /// HudDistance * range / distance world units of radius on the sphere (small-angle, like stock), and
    /// the ring's rect is rectHeight canvas units scaled by its parent's lossy scale.
    /// </summary>
    private static void UpdateSizeIndicator(Image sizeIndicator, MissionPosition.PositionResult result)
    {
        var range = result.Range ?? 0.0f;
        var inverseDistance = 1.0f / (result.Distance != 0.0f ? result.Distance : 0.01f);

        var rectHeight = sizeIndicator.rectTransform.rect.height;
        if (rectHeight <= Mathf.Epsilon)
            rectHeight = 20.0f;

        var diameterWorld = 2.0f * VrHudProjection.HudDistance * range * inverseDistance;
        var scale = diameterWorld / (rectHeight * GetParentScale(sizeIndicator.transform));
        sizeIndicator.transform.localScale = Vector3.one * scale;

        // Stock alpha rule, unchanged.
        sizeIndicator.color = sizeIndicator.color.WithAlpha(Mathf.Clamp01(range * 20.0f * inverseDistance - 0.5f));
    }

    private readonly struct LabelEntry
    {
        public readonly TMP_Text Text;
        public readonly TextNoOverlap NoOverlap;
        public readonly Vector3 BasePosition;
        public readonly Vector2 Planar;
        public readonly float Scale;

        public LabelEntry(TMP_Text text, TextNoOverlap noOverlap, Vector3 basePosition, Vector2 planar, float scale)
        {
            Text = text;
            NoOverlap = noOverlap;
            BasePosition = basePosition;
            Planar = planar;
            Scale = scale;
        }
    }

    private static readonly List<LabelEntry> LabelBuffer = new();

    /// <summary>
    /// Pairwise label repulsion in the head's (right, up) plane, in canvas units so the manager's stock
    /// textDistance / textPush / textNudgeDecrease values keep their meaning. Base positions are what
    /// UpdateOverlayPatch wrote this frame (UpdateOverlays runs immediately before StopTextOverlap), so
    /// nudges never accumulate onto already-nudged positions. The stock 0.8 position lerp is dropped on
    /// purpose: lerping world positions under head motion would make labels trail their markers.
    /// </summary>
    private static void NudgeLabelsApart(IList overlays, global::ObjectiveOverlayManager manager, Transform cameraTransform)
    {
        var textDistance = GetFloat(TextDistanceField, manager, 30.0f);
        var textPush = GetVector2(TextPushField, manager, new Vector2(2000.0f, 500.0f));
        var textNudgeDecrease = GetFloat(TextNudgeDecreaseField, manager, 0.5f);
        var deltaTime = Time.deltaTime;

        LabelBuffer.Clear();
        for (var i = 0; i < overlays.Count; i++)
        {
            if (overlays[i] is not global::ObjectiveOverlay overlay)
                continue;

            var textNoOverlap = overlay.TextNoOverlap;
            var text = GetObjectiveInfo(overlay);
            if (textNoOverlap == null || text == null)
                continue;

            if (!text.enabled)
            {
                // Hidden overlays still decay, as in stock, so they do not reappear with a stale nudge.
                textNoOverlap.NudgeOffset *= textNudgeDecrease;
                continue;
            }

            var scale = GetParentScale(text.transform);
            var basePosition = text.transform.position;
            LabelBuffer.Add(new LabelEntry(text, textNoOverlap, basePosition, ToViewPlane(basePosition, cameraTransform, scale), scale));
        }

        for (var i = 0; i < LabelBuffer.Count; i++)
        {
            for (var j = i + 1; j < LabelBuffer.Count; j++)
            {
                var first = LabelBuffer[i];
                var second = LabelBuffer[j];
                if (Vector2.Distance(first.Planar, second.Planar) >= textDistance)
                    continue;

                // Stock push rule, verbatim.
                var direction = second.Planar - first.Planar;
                if (direction.y < 0.1f)
                    direction.y += 5.0f;
                direction.Normalize();
                var push = new Vector2(direction.x * textPush.x * deltaTime, direction.y * textPush.y * deltaTime);
                first.NoOverlap.NudgeOffset += push;
                second.NoOverlap.NudgeOffset -= push;
            }
        }

        foreach (var entry in LabelBuffer)
        {
            var nudge = entry.NoOverlap.NudgeOffset;
            entry.Text.transform.position = entry.BasePosition + (cameraTransform.right * nudge.x + cameraTransform.up * nudge.y) * entry.Scale;
            entry.NoOverlap.NudgeOffset = nudge * textNudgeDecrease;
            entry.NoOverlap.AutomaticlalySetPosition = false;
        }

        LabelBuffer.Clear();
    }

    /// <summary>World position -> (right, up) coordinates in the head's view plane, in canvas units.</summary>
    private static Vector2 ToViewPlane(Vector3 worldPosition, Transform cameraTransform, float scale)
    {
        var local = worldPosition - cameraTransform.position;
        return new Vector2(Vector3.Dot(local, cameraTransform.right), Vector3.Dot(local, cameraTransform.up)) / scale;
    }

    /// <summary>Canvas units -> world units for a HUD child: its parent's lossy scale (IconLayer under HUDCanvas).</summary>
    private static float GetParentScale(Transform transform)
    {
        var parent = transform.parent;
        if (parent == null)
            return 1.0f;

        var scale = parent.lossyScale.y;
        return scale > Mathf.Epsilon ? scale : 1.0f;
    }

    private static float GetFloat(FieldInfo? field, object instance, float fallback) =>
        field != null && field.GetValue(instance) is float value ? value : fallback;

    private static Vector2 GetVector2(FieldInfo? field, object instance, Vector2 fallback) =>
        field != null && field.GetValue(instance) is Vector2 value ? value : fallback;
}
