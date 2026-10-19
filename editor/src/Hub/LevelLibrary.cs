using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Sonokinesis.Editor.Hub;

/// <summary>A level in the game's levels folder: a folder with a level.json, or a .zip holding one.</summary>
public sealed class LevelInfo
{
    public string Path;        // the folder or the .zip
    public bool IsZip;
    public string Title = "", Artist = "", Author = "", Description = "";
    public double Bpm, LengthMs;
    public int Difficulty;
    public string Background = "#0b0d12", Hazard = "#ff2a6d";
    public string Error;
}

/// <summary>Lists, adds and removes the levels the game plays.</summary>
public static class LevelLibrary
{
    public static List<LevelInfo> Scan(string folder)
    {
        var list = new List<LevelInfo>();
        if (folder == null || !Directory.Exists(folder)) return list;
        foreach (var dir in Directory.GetDirectories(folder))
        {
            var json = System.IO.Path.Combine(dir, "level.json");
            if (!File.Exists(json)) continue;
            list.Add(ReadFolder(dir));
        }
        foreach (var zip in Directory.GetFiles(folder, "*.zip"))
        {
            var info = new LevelInfo { Path = zip, IsZip = true, Title = System.IO.Path.GetFileNameWithoutExtension(zip) };
            try
            {
                using var z = ZipFile.OpenRead(zip);
                var entry = LevelEntry(z) ?? throw new Exception("no level.json inside");
                using var r = new StreamReader(entry.Open());
                Fill(info, r.ReadToEnd());
            }
            catch (Exception e) { info.Error = e.Message; }
            list.Add(info);
        }
        return list.OrderBy(l => l.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>One level folder's details.</summary>
    public static LevelInfo ReadFolder(string dir)
    {
        var info = new LevelInfo { Path = dir, Title = System.IO.Path.GetFileName(dir) };
        try { Fill(info, File.ReadAllText(System.IO.Path.Combine(dir, "level.json"))); } catch (Exception e) { info.Error = e.Message; }
        return info;
    }

    /// <summary>The level.json of a zipped level: at the top or inside one folder.</summary>
    private static ZipArchiveEntry LevelEntry(ZipArchive z) => z.Entries
        .Where(e => { var n = e.FullName.Replace('\\', '/'); return (n == "level.json" || n.EndsWith("/level.json")) && n.Split('/').Length <= 2; })
        .OrderBy(e => e.FullName.Length).FirstOrDefault();

    private static void Fill(LevelInfo info, string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        if (root.TryGetProperty("meta", out var meta))
        {
            info.Title = Str(meta, "title") ?? info.Title;
            info.Artist = Str(meta, "artist") ?? "";
            info.Author = Str(meta, "author") ?? "";
            info.Description = Str(meta, "description") ?? "";
            info.Bpm = Num(meta, "bpm");
            info.LengthMs = Num(meta, "lengthMs");
            info.Difficulty = (int)Num(meta, "difficulty");
        }
        if (root.TryGetProperty("theme", out var theme))
        {
            info.Background = Str(theme, "background") ?? info.Background;
            info.Hazard = Str(theme, "hazard") ?? info.Hazard;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString().Length > 0 ? v.GetString() : null;

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    /// <summary>
    /// Copies levels (zips, level folders or a level.json) into the levels folder; one already there is
    /// replaced. Returns a line per dropped item.
    /// </summary>
    public static List<(string Text, bool Ok)> Add(string levelsFolder, IEnumerable<string> paths)
    {
        var result = new List<(string, bool)>();
        Directory.CreateDirectory(levelsFolder);
        foreach (var raw in paths)
        {
            var p = raw;
            var name = System.IO.Path.GetFileName(p.TrimEnd('\\', '/'));
            try
            {
                if (File.Exists(p) && name.Equals("level.json", StringComparison.OrdinalIgnoreCase)) { p = System.IO.Path.GetDirectoryName(p); name = System.IO.Path.GetFileName(p); }
                if (Directory.Exists(p))
                {
                    if (!File.Exists(System.IO.Path.Combine(p, "level.json"))) { result.Add(($"'{name}' is not a level (no level.json inside).", false)); continue; }
                    var dest = System.IO.Path.Combine(levelsFolder, name);
                    if (SamePath(p, dest)) { result.Add(($"'{name}' is already in your levels.", true)); continue; }
                    bool existed = Directory.Exists(dest);
                    if (existed) Directory.Delete(dest, true);
                    CopyDir(p, dest);
                    result.Add(($"{(existed ? "Updated" : "Added")} '{TitleOf(dest) ?? name}'.", true));
                }
                else if (File.Exists(p) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (var z = ZipFile.OpenRead(p))
                        if (LevelEntry(z) == null) { result.Add(($"'{name}' is not a level (no level.json inside).", false)); continue; }
                    var dest = System.IO.Path.Combine(levelsFolder, name);
                    if (SamePath(p, dest)) { result.Add(($"'{name}' is already in your levels.", true)); continue; }
                    bool existed = File.Exists(dest);
                    File.Copy(p, dest, true);
                    result.Add(($"{(existed ? "Updated" : "Added")} '{System.IO.Path.GetFileNameWithoutExtension(name)}'.", true));
                }
                else result.Add(($"'{name}' is not a level: drop a level .zip or a level folder.", false));
            }
            catch (Exception e) { result.Add(($"Could not add '{name}': {e.Message}", false)); }
        }
        return result;
    }

    private static string TitleOf(string folder)
    {
        try { var i = new LevelInfo(); Fill(i, File.ReadAllText(System.IO.Path.Combine(folder, "level.json"))); return i.Title.Length > 0 ? i.Title : null; }
        catch { return null; }
    }

    /// <summary>Moves a level to the Recycle Bin.</summary>
    public static void Remove(LevelInfo level)
    {
        var err = OS.MoveToTrash(level.Path);
        if (err != Error.Ok) throw new Exception("could not move it to the Recycle Bin (" + err + ")");
    }

    /// <summary>
    /// A folder holding the level, for playing or editing: the level's own folder, or a zip unpacked
    /// into <paramref name="into"/>.
    /// </summary>
    public static string Unpack(LevelInfo level, string into)
    {
        if (!level.IsZip) return level.Path;
        if (Directory.Exists(into)) Directory.Delete(into, true);
        Directory.CreateDirectory(into);
        using var z = ZipFile.OpenRead(level.Path);
        var entry = LevelEntry(z);
        var prefix = entry.FullName.Replace('\\', '/');
        prefix = prefix.Substring(0, prefix.Length - "level.json".Length);
        var root = System.IO.Path.GetFullPath(into) + System.IO.Path.DirectorySeparatorChar;
        foreach (var e in z.Entries)
        {
            var n = e.FullName.Replace('\\', '/');
            if (!n.StartsWith(prefix) || n.EndsWith("/")) continue;
            var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(into, n.Substring(prefix.Length)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // no paths out of the folder
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
            e.ExtractToFile(target, true);
        }
        return into;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('\\', '/'), System.IO.Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(d)));
    }
}
