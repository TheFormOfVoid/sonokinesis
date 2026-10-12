using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace Beatform.Loader;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "dev.beatform.loader";
    public const string Name = "Beatform Loader";
    public const string Version = "0.1.0";

    internal static ManualLogSource Logger;
    internal static string DataDir;

    public override void Load()
    {
        Logger = Log;
        DataDir = System.IO.Path.Combine(Paths.BepInExRootPath, "beatform");
        System.IO.Directory.CreateDirectory(DataDir);

        Settings.Bind(Config);
        var harmony = new HarmonyLib.Harmony(Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);
        Patches.CreditsPatches.Apply(harmony);
        AddComponent<Runtime.BeatformRuntime>();
        if (Settings.DevConsole)
        {
            AddComponent<Dev.DevConsole>();
            Dev.SoundLog.Apply(harmony);
            Dev.FakeInput.Apply(harmony);
        }
        Runtime.EditorBridge.Status("ready", "loader started");
        Log.LogInfo($"{Name} {Version} loaded");
    }
}
