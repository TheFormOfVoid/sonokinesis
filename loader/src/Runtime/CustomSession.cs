using System;

namespace Beatform.Loader.Runtime;

/// <summary>
/// The custom level currently being played, from launch until the game leaves the level.
/// While a session is active the save guard is up and the hazard runtime draws the level. The game's
/// in-memory progress (beat points, stats, ribbons) is snapshotted at the start and restored at the
/// end, so a custom level leaves no trace even when the game saves later.
/// </summary>
internal static class CustomSession
{
    public static bool Active { get; private set; }
    public static LoadedLevel Level { get; private set; }
    public static ModelStoryCheckpoint Model { get; private set; }

    private static string progressSnapshot;

    public static void Begin(LoadedLevel level, ModelStoryCheckpoint model)
    {
        Level = level;
        Model = model;
        Active = true;
        try { progressSnapshot = MetaGameProgress.instance?.Save(); }
        catch (Exception e) { progressSnapshot = null; Plugin.Logger.LogWarning("could not snapshot progress: " + e.Message); }
        Plugin.Logger.LogInfo($"custom session started: {level.Data.Meta.Title} ({model.id})");
    }

    public static void End()
    {
        if (!Active) return;
        RestoreProgress();
        Plugin.Logger.LogInfo($"custom session ended: {Level?.Data.Meta.Title}");
        Active = false;
        Level = null;
        Model = null;
    }

    private static void RestoreProgress()
    {
        if (string.IsNullOrEmpty(progressSnapshot)) return;
        try
        {
            MetaGameProgress.instance.Load(progressSnapshot);
            GameBridge.RefreshMenus();
            Plugin.Logger.LogInfo("restored the game's progress to its state before the custom level");
        }
        catch (Exception e) { Plugin.Logger.LogError("could not restore progress: " + e.Message); }
        progressSnapshot = null;
    }
}
