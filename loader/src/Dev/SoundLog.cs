using System.Collections.Generic;
using HarmonyLib;

namespace Beatform.Loader.Dev;

/// <summary>
/// Development only: records the id of every game sound that is created, to find which sound the
/// game uses for an event (for example a player's death). Commands: soundlog on|off|clear, soundlog.
/// </summary>
internal static class SoundLog
{
    public static bool Recording;
    public static readonly List<string> Entries = new();

    public static void Apply(Harmony harmony)
    {
        try
        {
            var create = AccessTools.Method(typeof(ModelSound), nameof(ModelSound.createSound));
            harmony.Patch(create, prefix: new HarmonyMethod(typeof(SoundLog), nameof(BeforeCreate)));
            foreach (var m in typeof(JukeBox).GetMethods())
                if (m.Name.StartsWith("playSound") && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(ModelSound))
                    harmony.Patch(m, prefix: new HarmonyMethod(typeof(SoundLog), nameof(BeforePlay)));
        }
        catch (System.Exception e) { Plugin.Logger.LogWarning("sound log: " + e.Message); }

        DevConsole.Commands["soundlog"] = a =>
        {
            if (a.Length > 0 && a[0] == "on") { Recording = true; Entries.Clear(); return "recording sounds"; }
            if (a.Length > 0 && a[0] == "off") { Recording = false; return "stopped"; }
            if (a.Length > 0 && a[0] == "clear") { Entries.Clear(); return "cleared"; }
            return string.Join("\n", Entries);
        };
    }

    private static void BeforeCreate(ModelSound __instance) => Add("create " + __instance?.id);

    private static void BeforePlay(object[] __args) => Add("play " + (__args.Length > 0 ? (__args[0] as ModelSound)?.id : "?"));

    private static void Add(string s)
    {
        if (!Recording) return;
        Entries.Add($"{UnityEngine.Time.unscaledTime:0.00} {s}");
        if (Entries.Count > 500) Entries.RemoveAt(0);
    }
}
