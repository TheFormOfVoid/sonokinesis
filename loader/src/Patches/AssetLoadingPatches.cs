using Sonokinesis.Loader.Runtime;
using HarmonyLib;

namespace Sonokinesis.Loader.Patches;

/// <summary>
/// Custom song slots have no asset bundle: their MetaSong and AudioClip are supplied by
/// Sonokinesis before the level starts, so the game's bundle loading is skipped for them.
/// </summary>
[HarmonyPatch]
internal static class AssetLoadingPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(LoadAllAssetsOfModelSongSimul), nameof(LoadAllAssetsOfModelSongSimul.StartLoading))]
    private static bool SkipBundleLoad(LoadAllAssetsOfModelSongSimul __instance, Callback callbackOnEnd)
    {
        if (!CustomSongRegistry.IsCustom(__instance.modelSong)) return true;
        Plugin.Logger.LogInfo($"skipping bundle load for {__instance.modelSong.id}");
        callbackOnEnd?.call();
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ModelStoryCheckpoint), nameof(ModelStoryCheckpoint.isAssetsLoaded))]
    private static bool AssetsLoaded(ModelStoryCheckpoint __instance, ref bool __result)
    {
        if (!CustomSongRegistry.IsCustom(__instance)) return true;
        __result = true;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ModelStoryCheckpoint), nameof(ModelStoryCheckpoint.unloadPreviewAssets))]
    private static bool KeepCustomAssets(ModelStoryCheckpoint __instance) => !CustomSongRegistry.IsCustom(__instance);
}
