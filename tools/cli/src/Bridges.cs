using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;

namespace Beatform.Cli;

/// <summary>
/// Talks to the Beatform loader inside the game through files in BepInEx/beatform: bridge/play.json
/// starts a level, bridge/status.json says how it went. The developer console (cmd.txt / out.txt,
/// only when DevConsole = true in BepInEx/config/dev.beatform.loader.cfg) can take screenshots.
/// </summary>
public static class GameLink
{
    private const string GameFolderName = "Just Shapes & Beats";

    public static string FindGame()
    {
        var env = Environment.GetEnvironmentVariable("BEATFORM_GAME_DIR");
        if (IsGame(env)) return env;
        foreach (var lib in SteamLibraries())
        {
            var dir = Path.Combine(lib, "steamapps", "common", GameFolderName);
            if (IsGame(dir)) return dir;
        }
        return null;
    }

    private static bool IsGame(string dir) => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "JSB.exe"));

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
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "steam"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Steam"));
        var res = new List<string>();
        foreach (var root in roots.Distinct())
        {
            res.Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                res.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        return res.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string Game() => FindGame() ?? throw new CliError("could not find Just Shapes & Beats (set BEATFORM_GAME_DIR to its folder)");
    private static string DataDir(string game) => Path.Combine(game, "BepInEx", "beatform");

    public static bool Running => Process.GetProcessesByName("JSB").Length > 0;

    /// <summary>Asks the game to play a level folder from a song time, and waits for its answer.</summary>
    public static JsonObject Play(string folder, double fromMs, double waitSeconds = 60)
    {
        var game = Game();
        if (!File.Exists(Path.Combine(game, "BepInEx", "plugins", "Beatform", "Beatform.Loader.dll")))
            throw new CliError("the Beatform loader is not installed in the game (run the Beatform installer)");
        var bridge = Path.Combine(DataDir(game), "bridge");
        Directory.CreateDirectory(bridge);
        var status = Path.Combine(bridge, "status.json");
        var before = File.Exists(status) ? File.ReadAllText(status) : null;
        File.WriteAllText(Path.Combine(bridge, "play.json"), JsonSerializer.Serialize(new { folder = Path.GetFullPath(folder), startMs = Math.Max(0, fromMs), hard = false }));
        if (!Running) return new JsonObject { ["sent"] = true, ["state"] = "waiting", ["detail"] = "The game is not running. Start it: the level opens when the main menu is reached." };
        var until = DateTime.UtcNow.AddSeconds(waitSeconds);
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(300);
            try
            {
                if (!File.Exists(status)) continue;
                var text = File.ReadAllText(status);
                if (text == before) continue;
                var node = JsonNode.Parse(text) as JsonObject;
                var state = (string)node?["state"];
                if (state is "launching" or "error") return node;
            }
            catch { }
        }
        return new JsonObject { ["sent"] = true, ["state"] = "no answer", ["detail"] = "The game did not answer in time (is it on a menu, focused, with the loader?)" };
    }

    public static JsonObject Status()
    {
        var game = FindGame();
        var res = new JsonObject { ["gameFolder"] = game, ["running"] = Running };
        if (game == null) return res;
        res["loaderInstalled"] = File.Exists(Path.Combine(game, "BepInEx", "plugins", "Beatform", "Beatform.Loader.dll"));
        var status = Path.Combine(DataDir(game), "bridge", "status.json");
        try { if (File.Exists(status)) res["lastPlay"] = JsonNode.Parse(File.ReadAllText(status)); } catch { }
        res["devConsole"] = DevConsoleEnabled(game);
        return res;
    }

    private static bool DevConsoleEnabled(string game)
    {
        var cfg = Path.Combine(game, "BepInEx", "config", "dev.beatform.loader.cfg");
        return File.Exists(cfg) && Regex.IsMatch(File.ReadAllText(cfg), @"^\s*DevConsole\s*=\s*true", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    }

    /// <summary>Sends a developer-console command and returns its reply.</summary>
    public static string Command(string command, double waitSeconds = 20)
    {
        var game = Game();
        if (!Running) throw new CliError("the game is not running");
        if (!DevConsoleEnabled(game)) throw new CliError("the developer console is off: set DevConsole = true in BepInEx/config/dev.beatform.loader.cfg and restart the game");
        var dir = DataDir(game);
        Directory.CreateDirectory(dir);
        var outFile = Path.Combine(dir, "out.txt");
        if (File.Exists(outFile)) File.Delete(outFile);
        File.WriteAllText(Path.Combine(dir, "cmd.txt"), command + "\n");
        var until = DateTime.UtcNow.AddSeconds(waitSeconds);
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(200);
            if (!File.Exists(outFile)) continue;
            Thread.Sleep(100);
            try { return File.ReadAllText(outFile); } catch { }
        }
        throw new CliError("the game did not answer the console command");
    }

    /// <summary>A screenshot of the game (needs the developer console).</summary>
    public static string Screenshot(string outPath)
    {
        var game = Game();
        string name = "cli_" + DateTime.UtcNow.Ticks;
        Command("shot " + name);
        var src = Path.Combine(DataDir(game), "shots", name + ".png");
        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(200);
            try
            {
                if (File.Exists(src) && new FileInfo(src).Length > 0)
                {
                    Thread.Sleep(200);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
                    File.Copy(src, outPath, true);
                    return Path.GetFullPath(outPath);
                }
            }
            catch { }
        }
        throw new CliError("the screenshot did not appear");
    }
}

/// <summary>
/// Drives the running Beatform Editor through %LOCALAPPDATA%/Beatform/editor-bridge (command.json in,
/// result.json out). Commands: ping, state, open, reload, save, seek, play, stop, select, shot.
/// </summary>
public static class EditorLink
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Beatform", "editor-bridge");

    public static JsonObject Send(JsonObject command, double waitSeconds = 15)
    {
        Directory.CreateDirectory(Dir);
        var id = Guid.NewGuid().ToString("N")[..8];
        command["id"] = id;
        var result = Path.Combine(Dir, "result.json");
        if (File.Exists(result)) File.Delete(result);
        var tmp = Path.Combine(Dir, "command.tmp");
        File.WriteAllText(tmp, command.ToJsonString());
        File.Move(tmp, Path.Combine(Dir, "command.json"), true);
        var until = DateTime.UtcNow.AddSeconds(waitSeconds);
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(100);
            try
            {
                if (!File.Exists(result)) continue;
                var node = JsonNode.Parse(File.ReadAllText(result)) as JsonObject;
                if ((string)node?["id"] != id) continue;
                return node;
            }
            catch { }
        }
        throw new CliError("the editor did not answer (is Beatform Editor running?)");
    }
}
