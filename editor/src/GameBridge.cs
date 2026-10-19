using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sonokinesis.Editor;

/// <summary>
/// Talks to the Sonokinesis loader inside the game: finds the game folder, asks the loader to play a
/// level (BepInEx/sonokinesis/bridge/play.json) and reports what it says back (status.json).
/// </summary>
public static class GameBridge
{
    private const string GameFolderName = "Just Shapes & Beats";
    private static DateTime watchUntil;
    private static string lastStatus;

    public static string FindGame()
    {
        if (IsGame(EditorSettings.GameDir)) return EditorSettings.GameDir;
        foreach (var lib in SteamLibraries())
        {
            var dir = Path.Combine(lib, "steamapps", "common", GameFolderName);
            if (IsGame(dir)) { EditorSettings.GameDir = dir; return dir; }
        }
        return null;
    }

    private static bool IsGame(string dir) => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "JSB.exe"));

    public static bool LoaderInstalled(string game) => File.Exists(Path.Combine(game, "BepInEx", "plugins", "Sonokinesis", "Sonokinesis.Loader.dll"));

    /// <summary>Steam library folders, read from Steam's own libraryfolders.vdf.</summary>
    private static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string sp) roots.Add(sp.Replace('/', Path.DirectorySeparatorChar));
            }
        }
        catch { }
        roots.Add(@"C:\Program Files (x86)\Steam");
        var result = new List<string>();
        foreach (var root in roots.Distinct())
        {
            result.Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                result.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static (bool ok, string message) RequestPlay(string levelFolder, double startMs)
    {
        var game = FindGame();
        if (game == null) return (false, "Could not find Just Shapes & Beats. Set its folder in Level… → Editor.");
        if (!LoaderInstalled(game)) return (false, "Sonokinesis is not installed in the game yet. Go Home and press Play once: it is installed for you.");
        var bridge = Path.Combine(game, "BepInEx", "sonokinesis", "bridge");
        Directory.CreateDirectory(bridge);
        var json = JsonSerializer.Serialize(new { folder = levelFolder, startMs = Math.Max(0, startMs), hard = false });
        File.WriteAllText(Path.Combine(bridge, "play.json"), json);
        watchUntil = DateTime.UtcNow.AddSeconds(60);
        lastStatus = null;
        bool running = System.Diagnostics.Process.GetProcessesByName("JSB").Length > 0;
        return (true, running
            ? "Sent to the game. It will open the level from the playhead."
            : "Sent. Start Just Shapes & Beats: the level opens as soon as the main menu is reached.");
    }

    /// <summary>Shows the loader's replies in the status bar for a while after a request.</summary>
    public static void Poll(App app)
    {
        if (DateTime.UtcNow > watchUntil) return;
        var game = FindGame();
        if (game == null) return;
        var file = Path.Combine(game, "BepInEx", "sonokinesis", "bridge", "status.json");
        try
        {
            if (!File.Exists(file)) return;
            var text = File.ReadAllText(file);
            if (text == lastStatus) return;
            lastStatus = text;
            using var doc = JsonDocument.Parse(text);
            var state = doc.RootElement.GetProperty("state").GetString();
            var detail = doc.RootElement.GetProperty("detail").GetString();
            app.Status(state switch
            {
                "launching" => "The game is starting the level.",
                "working" => "Game: " + detail,
                "error" => "Game: " + detail,
                _ => "Game: " + state,
            }, state == "error");
            if (state is "launching" or "error") watchUntil = DateTime.UtcNow;
        }
        catch { }
    }
}
