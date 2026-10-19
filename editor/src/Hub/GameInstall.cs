using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Godot;

namespace Sonokinesis.Editor.Hub;

/// <summary>The state of the loader inside the game.</summary>
public enum LoaderState { NoGame, NotInstalled, Outdated, Installed }

/// <summary>
/// Puts the Sonokinesis loader into Just Shapes & Beats and takes it out again. BepInEx is added only when
/// the game has none, the plugin always replaces the old one, and a manifest records what was added so
/// removing leaves the game folder as it was. No game file is ever changed.
/// </summary>
public static class GameInstall
{
    public const string SteamAppId = "531510";
    private const string ManifestName = "install-manifest.txt";
    private const string BepInExMarker = "#bepinex-installed-by-sonokinesis";

    /// <summary>Set by the self-test to work on a stand-in game folder.</summary>
    public static string OverrideGame;

    public static string Game => OverrideGame ?? GameBridge.FindGame();

    public static bool GameRunning => Process.GetProcessesByName("JSB").Length > 0;

    /// <summary>The loader files this app installs: bepinex/, plugin/ and levels/ (the demo).</summary>
    public static string Payload
    {
        get
        {
            var candidates = new List<string>();
            try { candidates.Add(Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "loader")); } catch { }
            // Running from source: the folder the release script builds.
            try { candidates.Add(Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "dist", "Sonokinesis-" + Core.BuildInfo.Version, "loader"))); } catch { }
            return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "plugin", "Sonokinesis.Loader.dll")));
        }
    }

    private static string PluginDir(string game) => Path.Combine(game, "BepInEx", "plugins", "Sonokinesis");
    private static string DataDir(string game) => Path.Combine(game, "BepInEx", "sonokinesis");

    public static LoaderState State(out string game)
    {
        game = Game;
        if (game == null) return LoaderState.NoGame;
        var dll = Path.Combine(PluginDir(game), "Sonokinesis.Loader.dll");
        if (!File.Exists(dll)) return LoaderState.NotInstalled;
        var payload = Payload;
        if (payload == null) return LoaderState.Installed;
        return SameFile(dll, Path.Combine(payload, "plugin", "Sonokinesis.Loader.dll")) ? LoaderState.Installed : LoaderState.Outdated;
    }

    /// <summary>The version the installed loader was put in with (empty if unknown).</summary>
    public static string InstalledVersion(string game)
    {
        try { return File.ReadAllText(Path.Combine(DataDir(game), "version.txt")).Trim(); } catch { return ""; }
    }

    private static bool SameFile(string a, string b)
    {
        try
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            using var sha = SHA256.Create();
            using var fa = File.OpenRead(a);
            using var fb = File.OpenRead(b);
            return sha.ComputeHash(fa).AsSpan().SequenceEqual(SHA256.Create().ComputeHash(fb));
        }
        catch { return false; }
    }

    /// <summary>Installs or updates the loader. Returns what was done, line by line. Throws with a readable message.</summary>
    public static List<string> Install(string game)
    {
        var log = new List<string>();
        var payload = Payload ?? throw new Exception("The loader files are missing from the Sonokinesis folder. Reinstall Sonokinesis.");
        if (GameRunning) throw new Exception("Just Shapes & Beats is running. Close it first.");

        var bepinex = Path.Combine(game, "BepInEx");
        var dataDir = DataDir(game);
        var manifest = Path.Combine(dataDir, ManifestName);

        // Sonokinesis used to be called Beatform: move its data and settings over and remove the old plugin.
        var oldData = Path.Combine(bepinex, "beatform");
        if (Directory.Exists(oldData) && !Directory.Exists(dataDir))
        {
            Directory.Move(oldData, dataDir);
            if (File.Exists(manifest))
                File.WriteAllLines(manifest, File.ReadAllLines(manifest).Select(l => l == "#bepinex-installed-by-beatform" ? BepInExMarker : l));
            log.Add("Moved Beatform's data over.");
        }
        var oldCfg = Path.Combine(bepinex, "config", "dev.beatform.loader.cfg");
        var newCfg = Path.Combine(bepinex, "config", "dev.sonokinesis.loader.cfg");
        if (File.Exists(oldCfg) && !File.Exists(newCfg)) { File.Move(oldCfg, newCfg); log.Add("Kept the Beatform loader settings."); }
        var oldPlugin = Path.Combine(bepinex, "plugins", "Beatform");
        if (Directory.Exists(oldPlugin)) { Directory.Delete(oldPlugin, true); log.Add("Removed the old Beatform plugin."); }

        var added = new List<string>();
        if (File.Exists(manifest)) added.AddRange(File.ReadAllLines(manifest).Where(l => l.Length > 0));

        // 1. BepInEx, only when the game has none.
        if (File.Exists(Path.Combine(bepinex, "core", "BepInEx.Unity.IL2CPP.dll")))
            log.Add("BepInEx is already there.");
        else if (Directory.Exists(bepinex) && Directory.EnumerateFileSystemEntries(bepinex).Any(e => !Path.GetFileName(e).Equals("sonokinesis", StringComparison.OrdinalIgnoreCase)))
            throw new Exception("The game has a BepInEx folder that is not BepInEx 6 for IL2CPP. Remove it first, or install BepInEx 6 IL2CPP yourself.");
        else
        {
            var src = Path.Combine(payload, "bepinex");
            foreach (var entry in Directory.EnumerateFileSystemEntries(src))
            {
                var name = Path.GetFileName(entry);
                var target = Path.Combine(game, name);
                if (Directory.Exists(entry)) CopyDir(entry, target);
                else
                {
                    if (File.Exists(target)) throw new Exception("Unexpected file in the game folder: " + target);
                    File.Copy(entry, target);
                }
                added.Add(name);
            }
            added.Add(BepInExMarker);
            log.Add("Installed BepInEx 6, the mod loader Sonokinesis runs on.");
        }

        // 2. The plugin, always replaced.
        var pluginDir = PluginDir(game);
        Directory.CreateDirectory(pluginDir);
        foreach (var f in Directory.GetFiles(Path.Combine(payload, "plugin"))) File.Copy(f, Path.Combine(pluginDir, Path.GetFileName(f)), true);
        log.Add("Installed the Sonokinesis loader " + Core.BuildInfo.Version + ".");

        // 3. The levels folder with the demo level.
        var levels = Path.Combine(game, "CustomLevels");
        if (!Directory.Exists(levels)) { Directory.CreateDirectory(levels); added.Add("#created-CustomLevels"); }
        var demos = Path.Combine(payload, "levels");
        if (Directory.Exists(demos))
            foreach (var d in Directory.GetDirectories(demos))
            {
                var dest = Path.Combine(levels, Path.GetFileName(d));
                if (!Directory.Exists(dest)) { CopyDir(d, dest); log.Add("Added the demo level '" + Path.GetFileName(d) + "'."); }
            }

        // The console window follows the app's setting (the on/off switch is left as the player set it).
        SetConsoleShown(game, EditorSettings.ShowConsole);

        Directory.CreateDirectory(dataDir);
        File.WriteAllLines(manifest, added.Distinct());
        File.WriteAllText(Path.Combine(dataDir, "version.txt"), Core.BuildInfo.Version);
        return log;
    }

    /// <summary>
    /// Takes the loader out. BepInEx goes too when Sonokinesis put it there and no other plugin uses it.
    /// Levels are kept.
    /// </summary>
    public static List<string> Uninstall(string game)
    {
        var log = new List<string>();
        if (GameRunning) throw new Exception("Just Shapes & Beats is running. Close it first.");
        var bepinex = Path.Combine(game, "BepInEx");
        var manifest = Path.Combine(DataDir(game), ManifestName);
        var added = File.Exists(manifest) ? File.ReadAllLines(manifest).ToList() : new List<string>();

        foreach (var dir in new[] { PluginDir(game), Path.Combine(bepinex, "plugins", "Beatform") })
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        log.Add("Removed the Sonokinesis loader.");

        var plugins = Path.Combine(bepinex, "plugins");
        bool others = Directory.Exists(plugins) && Directory.EnumerateFileSystemEntries(plugins).Any();
        if (added.Contains(BepInExMarker) || added.Contains("#bepinex-installed-by-beatform"))
        {
            if (others) log.Add("Other BepInEx plugins are installed, so BepInEx stays.");
            else
            {
                foreach (var name in added.Where(n => n.Length > 0 && !n.StartsWith('#')))
                {
                    var p = Path.Combine(game, name);
                    if (Directory.Exists(p)) Directory.Delete(p, true);
                    else if (File.Exists(p)) File.Delete(p);
                }
                log.Add("Removed BepInEx (Sonokinesis had installed it).");
            }
        }
        else log.Add("BepInEx was there before Sonokinesis, so it stays.");
        try { if (Directory.Exists(DataDir(game))) Directory.Delete(DataDir(game), true); } catch { }
        log.Add("Your levels were kept.");
        return log;
    }

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    // ---------------- levels folder ----------------

    private static string ConfigFile(string game) => Path.Combine(game, "BepInEx", "config", "dev.sonokinesis.loader.cfg");

    /// <summary>The folder the loader reads levels from: its LevelsFolder setting, else &lt;game&gt;/CustomLevels.</summary>
    public static string LevelsFolder(string game)
    {
        if (game == null) return null;
        try
        {
            var cfg = ConfigFile(game);
            if (File.Exists(cfg))
            {
                var m = Regex.Match(File.ReadAllText(cfg), @"^\s*LevelsFolder\s*=(.*)$", RegexOptions.Multiline);
                var v = m.Success ? m.Groups[1].Value.Trim().Trim('"') : "";
                if (v.Length > 0) return Path.IsPathRooted(v) ? v : Path.Combine(game, v);
            }
        }
        catch { }
        return Path.Combine(game, "CustomLevels");
    }

    /// <summary>Changes the loader's LevelsFolder setting (empty for the default).</summary>
    public static void SetLevelsFolder(string game, string folder)
    {
        var cfg = ConfigFile(game);
        Directory.CreateDirectory(Path.GetDirectoryName(cfg));
        var line = "LevelsFolder = " + (folder ?? "");
        if (!File.Exists(cfg)) { File.WriteAllText(cfg, "[General]\r\n\r\n" + line + "\r\n"); return; }
        var text = File.ReadAllText(cfg);
        var re = new Regex(@"^\s*LevelsFolder\s*=.*?(\r?)$", RegexOptions.Multiline);
        if (re.IsMatch(text)) text = re.Replace(text, m => line + m.Groups[1].Value, 1);
        else
        {
            var general = new Regex(@"^\[General\]\s*?$", RegexOptions.Multiline).Match(text);
            text = general.Success ? text.Insert(general.Index + general.Length, "\r\n" + line) : "[General]\r\n" + line + "\r\n\r\n" + text;
        }
        File.WriteAllText(cfg, text);
    }

    // ---------------- switches ----------------

    private static string DoorstopIni(string game) => Path.Combine(game, "doorstop_config.ini");
    private static string BepInExCfg(string game) => Path.Combine(game, "BepInEx", "config", "BepInEx.cfg");

    /// <summary>
    /// Whether mods load when the game starts (BepInEx's doorstop switch). Off, the game starts exactly as
    /// it does without mods, but nothing is uninstalled.
    /// </summary>
    public static bool ModsEnabled(string game) => !string.Equals(ReadIni(DoorstopIni(game), "General", "enabled"), "false", StringComparison.OrdinalIgnoreCase);

    public static void SetModsEnabled(string game, bool on)
    {
        if (!File.Exists(DoorstopIni(game))) return;
        WriteIni(DoorstopIni(game), "General", "enabled", on ? "true" : "false");
    }

    /// <summary>Whether BepInEx opens its log console window next to the game.</summary>
    public static bool ConsoleShown(string game)
    {
        var v = ReadIni(BepInExCfg(game), "Logging.Console", "Enabled");
        return v == null || v.Equals("true", StringComparison.OrdinalIgnoreCase);   // BepInEx's default is on
    }

    public static void SetConsoleShown(string game, bool on)
    {
        if (!Directory.Exists(Path.Combine(game, "BepInEx"))) return;
        WriteIni(BepInExCfg(game), "Logging.Console", "Enabled", on ? "true" : "false");
    }

    private static string ReadIni(string file, string section, string key)
    {
        if (!File.Exists(file)) return null;
        string current = null;
        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { current = line[1..^1]; continue; }
            if (current != section || line.StartsWith('#') || line.StartsWith(';')) continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return line[(eq + 1)..].Trim();
        }
        return null;
    }

    /// <summary>Sets one key of an ini file, adding the key or the section when missing; the rest is kept.</summary>
    private static void WriteIni(string file, string section, string key, string value)
    {
        var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : new List<string>();
        int sectionAt = -1, end = lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (!(t.StartsWith('[') && t.EndsWith(']'))) continue;
            if (sectionAt >= 0) { end = i; break; }
            if (t[1..^1] == section) sectionAt = i;
        }
        if (sectionAt < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add("[" + section + "]");
            lines.Add(key + " = " + value);
        }
        else
        {
            int found = -1;
            for (int i = sectionAt + 1; i < end; i++)
            {
                var t = lines[i].Trim();
                int eq = t.IndexOf('=');
                if (!t.StartsWith('#') && !t.StartsWith(';') && eq > 0 && t[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { found = i; break; }
            }
            if (found >= 0) lines[found] = key + " = " + value;
            else lines.Insert(sectionAt + 1, key + " = " + value);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllLines(file, lines);
    }

    /// <summary>Starts the game through Steam.</summary>
    public static void LaunchGame() => OS.ShellOpen("steam://rungameid/" + SteamAppId);
}
