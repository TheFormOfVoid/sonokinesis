using System;
using System.IO;
using System.Text.Json;

namespace Sonokinesis.Loader.Runtime;

/// <summary>
/// Lets the Sonokinesis editor start a level in the running game ("test in game"). The editor writes
/// BepInEx/sonokinesis/bridge/play.json; the loader brings the game to the main menu if needed,
/// launches the level (optionally from a song time) and reports progress in status.json.
/// </summary>
internal static class EditorBridge
{
    private sealed class Request
    {
        public string folder { get; set; }
        public double startMs { get; set; }
        public bool hard { get; set; }
    }

    private static string Dir => Path.Combine(Plugin.DataDir, "bridge");
    private static Request pending;
    private static float nextPoll, requestedAt;
    private static bool askedToLeave;

    public static void Update(Launcher launcher)
    {
        if (UnityEngine.Time.unscaledTime >= nextPoll)
        {
            nextPoll = UnityEngine.Time.unscaledTime + 0.5f;
            Poll();
        }
        if (pending == null) return;

        if (launcher.Busy) return;
        if (UnityEngine.Time.unscaledTime - requestedAt > 60)
        {
            Status("error", "timed out waiting for the main menu");
            pending = null;
            return;
        }

        if (GameBridge.InOnlineMode())
        {
            Status("error", "the game is in online play; custom levels are solo and local co-op only");
            pending = null;
            return;
        }

        // Get to the main menu first: leave a running level, or skip the title screen.
        var logic = GameBridge.LevelLogic();
        if (logic != null)
        {
            if (!askedToLeave)
            {
                askedToLeave = true;
                Status("working", "leaving the current level");
                try { logic.LeaveToMainMenu(); } catch (Exception e) { Plugin.Logger.LogWarning(e.Message); }
            }
            return;
        }
        if (!GameBridge.InMenu())
        {
            var intro = GameBridge.FindUi<UI_MainMenu_Intro>();
            if (intro != null)
            {
                try { if (intro.isIntroDone()) intro.skipToMainMenu(); } catch { }
            }
            return;
        }

        var req = pending;
        pending = null;
        askedToLeave = false;
        var entry = new LevelEntry { Folder = req.folder, Meta = new Core.LevelMeta { Title = Path.GetFileName(req.folder) } };
        launcher.Launch(entry, req.hard, req.startMs);
        Status("launching", req.folder);
    }

    private static void Poll()
    {
        var file = Path.Combine(Dir, "play.json");
        if (!File.Exists(file)) return;
        try
        {
            var json = File.ReadAllText(file);
            File.Delete(file);
            var req = JsonSerializer.Deserialize<Request>(json);
            if (req == null || string.IsNullOrEmpty(req.folder) || !File.Exists(Path.Combine(req.folder, "level.json")))
            {
                Status("error", "play request has no valid level folder");
                return;
            }
            pending = req;
            requestedAt = UnityEngine.Time.unscaledTime;
            askedToLeave = false;
            Status("working", "request received");
        }
        catch (Exception e)
        {
            Status("error", "bad play request: " + e.Message);
        }
    }

    public static void Status(string state, string detail)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(new { state, detail, time = DateTime.UtcNow.ToString("o"), loader = Plugin.Version });
            File.WriteAllText(Path.Combine(Dir, "status.json"), json);
        }
        catch { }
    }
}
