using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Godot;

namespace Sonokinesis.Editor.Hub;

/// <summary>
/// --selftest DIR: installs into a stand-in game folder (DIR with an empty JSB.exe), flips the switches,
/// adds and removes levels and uninstalls, checking each step. Prints PASS/FAIL lines, then quits.
/// </summary>
public static class SelfTest
{
    public static void Run(string dir)
    {
        var fails = new List<string>();
        void Check(bool ok, string what) { GD.Print((ok ? "PASS " : "FAIL ") + what); if (!ok) fails.Add(what); }
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "JSB.exe"), "");
            File.WriteAllText(Path.Combine(dir, "game-file.txt"), "untouched");
            GameInstall.OverrideGame = dir;
            var before = Snapshot(dir);

            Check(GameInstall.Payload != null, "loader payload found: " + GameInstall.Payload);
            Check(GameInstall.State(out _) == LoaderState.NotInstalled, "state before install is NotInstalled");
            var log = GameInstall.Install(dir);
            Check(log.Any(l => l.StartsWith("Installed BepInEx")), "BepInEx installed");
            Check(File.Exists(Path.Combine(dir, "BepInEx", "plugins", "Sonokinesis", "Sonokinesis.Loader.dll")), "plugin copied");
            Check(File.Exists(Path.Combine(dir, "doorstop_config.ini")), "doorstop copied");
            Check(Directory.Exists(Path.Combine(dir, "CustomLevels", "Neon Rehearsal")), "demo level added");
            Check(GameInstall.State(out _) == LoaderState.Installed, "state after install is Installed");
            Check(GameInstall.InstalledVersion(dir) == Core.BuildInfo.Version, "version recorded");
            Check(GameInstall.ConsoleShown(dir) == EditorSettings.ShowConsole, "console follows the setting");

            GameInstall.SetModsEnabled(dir, false);
            Check(!GameInstall.ModsEnabled(dir), "mods off");
            GameInstall.SetModsEnabled(dir, true);
            Check(GameInstall.ModsEnabled(dir), "mods on again");
            GameInstall.SetConsoleShown(dir, true);
            Check(GameInstall.ConsoleShown(dir), "console on");
            GameInstall.SetConsoleShown(dir, false);
            Check(!GameInstall.ConsoleShown(dir), "console off");

            // A second install (an update) keeps BepInEx and the switches.
            GameInstall.SetModsEnabled(dir, false);
            log = GameInstall.Install(dir);
            Check(log.Any(l => l.StartsWith("BepInEx is already")), "reinstall keeps BepInEx");
            Check(!GameInstall.ModsEnabled(dir), "reinstall leaves the off switch alone");
            GameInstall.SetModsEnabled(dir, true);

            // Levels folder setting: empty, custom, back to default.
            Check(GameInstall.LevelsFolder(dir) == Path.Combine(dir, "CustomLevels"), "default levels folder");
            GameInstall.SetLevelsFolder(dir, "MyLevels");
            Check(GameInstall.LevelsFolder(dir) == Path.Combine(dir, "MyLevels"), "relative levels folder");
            GameInstall.SetLevelsFolder(dir, "");
            Check(GameInstall.LevelsFolder(dir) == Path.Combine(dir, "CustomLevels"), "empty setting means default");

            // Adding levels: a zip with the level one folder deep (backslash names), a folder, a level.json, junk.
            var src = Path.Combine(dir, "..", "selftest-src");
            if (Directory.Exists(src)) Directory.Delete(src, true);
            var lvl = Path.Combine(src, "My Level");
            Directory.CreateDirectory(lvl);
            File.WriteAllText(Path.Combine(lvl, "level.json"), "{\"meta\":{\"title\":\"Zipped Title\",\"bpm\":140,\"lengthMs\":90000},\"theme\":{\"background\":\"#101020\",\"hazard\":\"#33e5ff\"}}");
            var zip = Path.Combine(src, "zipped.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                z.CreateEntryFromFile(Path.Combine(lvl, "level.json"), @"My Level\level.json");
                z.CreateEntry(@"My Level\scripts\a.js").Open().Dispose();
            }
            File.WriteAllText(Path.Combine(src, "notes.txt"), "not a level");
            var levels = GameInstall.LevelsFolder(dir);
            var res = LevelLibrary.Add(levels, new[] { zip, lvl, Path.Combine(lvl, "level.json"), Path.Combine(src, "notes.txt") });
            Check(res.Count(r => r.Ok) == 3 && res.Count(r => !r.Ok) == 1, "add: 3 levels and 1 refusal: " + string.Join(" / ", res.Select(r => r.Text)));
            var scan = LevelLibrary.Scan(levels);
            var zipped = scan.FirstOrDefault(l => l.IsZip);
            Check(zipped != null && zipped.Title == "Zipped Title" && zipped.Bpm == 140, "zipped level read");
            Check(scan.Count(l => l.Error == null) == 3, "three readable levels (demo, folder, zip)");
            var unpacked = LevelLibrary.Unpack(zipped, Path.Combine(src, "unpacked"));
            Check(File.Exists(Path.Combine(unpacked, "level.json")) && File.Exists(Path.Combine(unpacked, "scripts", "a.js")), "zip unpacks with its scripts");

            log = GameInstall.Uninstall(dir);
            Check(log.Any(l => l.StartsWith("Removed BepInEx")), "uninstall removes BepInEx it installed");
            var after = Snapshot(dir);
            var left = after.Except(before).Where(p => !p.StartsWith("CustomLevels")).ToList();
            Check(left.Count == 0, "game folder as before, apart from levels" + (left.Count > 0 ? ": " + string.Join(", ", left.Take(5)) : ""));
            Check(File.ReadAllText(Path.Combine(dir, "game-file.txt")) == "untouched", "game files untouched");
            Directory.Delete(src, true);
        }
        catch (Exception e) { Check(false, "exception: " + e); }
        GameInstall.OverrideGame = null;
        GD.Print(fails.Count == 0 ? "SELFTEST OK" : $"SELFTEST FAILED ({fails.Count})");
        Hub.I.GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    private static HashSet<string> Snapshot(string dir) =>
        Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(dir, p)).ToHashSet();
}
