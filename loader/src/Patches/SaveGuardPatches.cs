using Beatform.Loader.Runtime;
using HarmonyLib;

namespace Beatform.Loader.Patches;

/// <summary>
/// Custom levels must never change the player's save file or Steam achievements. While a custom
/// session is active (from launch until the game returns to a menu) saving and achievement unlocks
/// are skipped. A config switch can block saves entirely for development.
/// </summary>
[HarmonyPatch]
internal static class SaveGuardPatches
{
    private static bool Blocked => CustomSession.Active || Settings.BlockAllSaves;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.Save))]
    private static bool BlockSave()
    {
        if (!Blocked) return true;
        Plugin.Logger.LogInfo("save skipped (custom level session or dev save block)");
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.UnlockAchievement))]
    private static bool BlockAchievement() => !CustomSession.Active;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.UnlockForFirstPlayer))]
    private static bool BlockAchievementFirst() => !CustomSession.Active;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.UnlockForAllLocalPlayers))]
    private static bool BlockAchievementAll() => !CustomSession.Active;
}
