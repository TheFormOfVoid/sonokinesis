using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace Sonokinesis.Loader;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "dev.sonokinesis.loader";
    public const string Name = "Sonokinesis Loader";
    public const string Version = Core.BuildInfo.Version;

    internal static ManualLogSource Logger;
    internal static string DataDir;

    public override void Load()
    {
        Logger = Log;
        DataDir = System.IO.Path.Combine(Paths.BepInExRootPath, "sonokinesis");
        MigrateFromBeatform();
        System.IO.Directory.CreateDirectory(DataDir);

        Settings.Bind(Config);
        var harmony = new HarmonyLib.Harmony(Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);
        Patches.CreditsPatches.Apply(harmony);
        AddComponent<Runtime.SonokinesisRuntime>();
        if (Settings.DevConsole)
        {
            AddComponent<Dev.DevConsole>();
            Dev.SoundLog.Apply(harmony);
            Dev.FakeInput.Apply(harmony);
        }
        Runtime.EditorBridge.Status("ready", "loader started");
        Log.LogInfo($"{Name} {Version} loaded");
    }

    /// <summary>
    /// Sonokinesis used to be called Beatform: carry over its settings and data folder once, and warn if
    /// the old plugin is still installed (both would run).
    /// </summary>
    private void MigrateFromBeatform()
    {
        try
        {
            var oldCfg = System.IO.Path.Combine(Paths.ConfigPath, "dev.beatform.loader.cfg");
            if (System.IO.File.Exists(oldCfg) && !System.IO.File.Exists(Config.ConfigFilePath))
            {
                System.IO.File.Copy(oldCfg, Config.ConfigFilePath);
                Config.Reload();
            }
            var oldData = System.IO.Path.Combine(Paths.BepInExRootPath, "beatform");
            if (System.IO.Directory.Exists(oldData) && !System.IO.Directory.Exists(DataDir)) System.IO.Directory.Move(oldData, DataDir);
            if (System.IO.File.Exists(System.IO.Path.Combine(Paths.PluginPath, "Beatform", "Beatform.Loader.dll")))
                Log.LogError("The old Beatform plugin is still installed (BepInEx/plugins/Beatform). Delete that folder: it is replaced by Sonokinesis.");
        }
        catch (System.Exception e) { Log.LogWarning("Could not carry over Beatform's settings: " + e.Message); }
    }
}
