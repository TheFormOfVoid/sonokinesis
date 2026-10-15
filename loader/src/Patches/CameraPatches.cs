using HarmonyLib;

namespace Sonokinesis.Loader.Patches;

/// <summary>The level's camera shift (sway, zoom point) goes on after the game has placed its camera.</summary>
[HarmonyPatch]
internal static class CameraPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CameraFlash), nameof(CameraFlash.update))]
    private static void AfterCameraUpdate() => Runtime.GameBridge.ApplyCameraShift();
}
