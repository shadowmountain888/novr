using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace NOVR.VrUi.Native;

/// <summary>
/// Late-bound access to the separate RealHUD mod (RealHUD.VrMenuApi), so the in-cockpit menu can
/// tune its layout inside the headset without NOVR depending on it. Null when RealHUD is absent or
/// its API version is not one this build understands.
/// </summary>
internal sealed class RealHudBridge
{
    private const int SupportedVersion = 1;

    private readonly Type _api;

    private RealHudBridge(Type api) => _api = api;

    public static RealHudBridge? TryCreate()
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "RealHUD") continue;

                var api = assembly.GetType("RealHUD.VrMenuApi", false);
                var version = api?.GetField("Version", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (api == null || version is not int number || number != SupportedVersion) return null;
                return new RealHudBridge(api);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NOVR] RealHUD menu page unavailable: {exception.Message}");
        }

        return null;
    }

    private object? Call(string method, params object[] arguments) =>
        _api.GetMethod(method, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, arguments);

    public List<object[]> Rows() => Call("Rows") as List<object[]> ?? new List<object[]>();
    public string Status() => Call("Status") as string ?? "";
    public bool UseF22() => Call("UseF22") is true;
    public void ToggleF22() => Call("ToggleF22");
    public bool UseGlobal() => Call("UseGlobal") is true;
    public void ToggleGlobal() => Call("ToggleGlobal");
    public void SaveToGlobal() => Call("SaveToGlobal");
    public void SaveToPlane() => Call("SaveToPlane");
    public void Revert() => Call("Revert");
    public float SavedValue(ConfigEntry<float> entry) => Call("SavedValue", entry) is float value ? value : entry.Value;
}
