using System;
using Beatform.Loader.Runtime;
using HarmonyLib;

namespace Beatform.Loader.Patches;

/// <summary>
/// A level with a "credits" event moves the game's own title card (song and artist) to that time:
/// the game waits levelStartShortDelayShowCredits frames (60 per second) from the level intro before
/// showing it, so that wait is lengthened by the event's time. Launched from past the event, the card
/// keeps the game's own timing.
/// </summary>
internal static class CreditsPatches
{
    public static void Apply(Harmony harmony)
    {
        // The wait is a virtual property; patch it on every level logic class that has its own.
        foreach (var t in typeof(ActorNormalLevelLogic).Assembly.GetTypes())
        {
            if (t != typeof(ActorNormalLevelLogic) && !t.IsSubclassOf(typeof(ActorNormalLevelLogic))) continue;
            var getter = t.GetProperty("levelStartShortDelayShowCredits", System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)?.GetGetMethod();
            if (getter == null) continue;
            try { harmony.Patch(getter, postfix: new HarmonyMethod(typeof(CreditsPatches), nameof(AfterGetDelay))); }
            catch (Exception e) { Plugin.Logger.LogWarning($"title card timing ({t.Name}): {e.Message}"); }
        }
    }

    private static void AfterGetDelay(ref float __result)
    {
        if (!CustomSession.Active || CustomSession.Level == null) return;
        var e = CustomSession.Level.Data.Events.Find(ev => ev.Type == Core.EventType.Credits);
        if (e != null && e.T > CustomSession.StartSongMs)
            __result += (float)((e.T - CustomSession.StartSongMs) * 60 / 1000);
    }
}
