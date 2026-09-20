using System;
using System.Collections;
using UnityEngine;

namespace NOVR.VrUi.SpecialBehavior;

public class UIRenderedCanvasBehavior : MonoBehaviour
{
    private bool _initialized;
    private Coroutine _layerRefreshRoutine;

    protected virtual bool ShouldInitializeCanvas => true;

    public virtual void Awake() => Initialize();

    public virtual void OnEnable()
    {
        Initialize();
        EnsureLayerRefreshRunning();
    }

    public virtual void OnDisable()
    {
        // Unity stops coroutines when the object is disabled; clear the handle so OnEnable restarts it.
        _layerRefreshRoutine = null;
    }

    private void EnsureLayerRefreshRunning()
    {
        if (!ShouldInitializeCanvas) return;
        if (_layerRefreshRoutine != null) return;
        if (!isActiveAndEnabled) return;

        _layerRefreshRoutine = StartCoroutine(ReapplyVrUiLayerPeriodically());
    }

    // Initialize() only runs once, so UI spawned later - the graphics settings pages, for example -
    // is created on the game's own HUD layer (9) and never moved to the VR UI layer (30). The VR UI
    // camera then does not render it, which shows up as missing or misplaced text. Re-applying the
    // layer on an interval catches anything added after the one-shot pass.
    private IEnumerator ReapplyVrUiLayerPeriodically()
    {
        while (true)
        {
            var interval = ModConfiguration.Instance?.VrUiLayerRefreshInterval.Value ?? 0.5f;

            if (interval <= 0f)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            yield return new WaitForSeconds(interval);
            ApplyVrUiLayerRecursive(transform);
        }
    }

    private void Initialize()
    {
        if (!ShouldInitializeCanvas) return;
        if (_initialized) return;
        _initialized = true;
        
        
        ApplyVrUiLayerRecursive(transform);
        transform.localPosition = transform.localPosition with { z = 0 };
        var canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null) return;
        
        
        canvas.renderMode = RenderMode.WorldSpace;
        Debug.Log($"{GetType().Name}: Set canvas render mode of {canvas.gameObject.name}. Is currently:  {canvas.renderMode}");
        canvas.worldCamera = APIBus.CockpitHudCamera;
        Debug.Log($"{GetType().Name}: Set canvas world camera of {canvas.gameObject.name}. Is currently:  {canvas.worldCamera}");
        canvas.planeDistance = 1f;

        EnsureLayerRefreshRunning();
    }
    
    
    private static void ApplyVrUiLayerRecursive(Transform root)
    {
        LayerHelper.SetLayerRecursive(root, LayerHelper.GetVrUiLayer());
    }


    protected static Transform FindChildStartingWith(Transform parent, string childNamePrefix)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name.StartsWith(childNamePrefix))
            {
                return child;
            }

            var nestedChild = FindChildStartingWith(child, childNamePrefix);
            if (nestedChild != null)
            {
                return nestedChild;
            }
        }

        return null;
    }
}
