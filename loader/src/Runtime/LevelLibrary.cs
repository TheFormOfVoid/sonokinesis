using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Sonokinesis.Core;

namespace Sonokinesis.Loader.Runtime;

/// <summary>A level found in the levels folder, with enough data for the level list.</summary>
internal sealed class LevelEntry
{
    public string Folder;
    public string DisplaySource;
    public LevelMeta Meta;
    public int ObjectCount;
    public double LengthMs;
    public string CoverPath;
    public string Error;
    public bool IsValid => Error == null;
}

/// <summary>Finds levels in the levels folder. Zipped levels are unpacked into a cache first.</summary>
internal static class LevelLibrary
{
    private static Task<List<LevelEntry>> scan;

    public static string CacheDir => Path.Combine(Plugin.DataDir, "cache");

    public static void Rescan() => scan = Task.Run(Scan);

    /// <summary>The latest scan result, or null while scanning.</summary>
    public static List<LevelEntry> Entries => scan != null && scan.IsCompleted ? (scan.IsFaulted ? new List<LevelEntry>() : scan.Result) : null;

    public static bool Scanning => scan != null && !scan.IsCompleted;

    private static List<LevelEntry> Scan()
    {
        var root = Settings.LevelsFolder;
        Directory.CreateDirectory(root);
        var result = new List<LevelEntry>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            if (File.Exists(Path.Combine(dir, "level.json"))) result.Add(Read(dir, Path.GetFileName(dir)));
        }
        foreach (var zip in Directory.GetFiles(root, "*.zip"))
        {
            try { result.Add(Read(Unzip(zip), Path.GetFileName(zip))); }
            catch (Exception e) { result.Add(new LevelEntry { DisplaySource = Path.GetFileName(zip), Error = "could not open zip: " + e.Message, Meta = new LevelMeta { Title = Path.GetFileNameWithoutExtension(zip) } }); }
        }
        return result.OrderBy(e => e.IsValid ? 0 : 1).ThenBy(e => e.Meta.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static LevelEntry Read(string dir, string source)
    {
        var entry = new LevelEntry { Folder = dir, DisplaySource = source, Meta = new LevelMeta { Title = Path.GetFileName(dir) } };
        try
        {
            var data = LevelData.Parse(File.ReadAllText(Path.Combine(dir, "level.json")));
            entry.Meta = data.Meta;
            entry.ObjectCount = data.Objects.Count;
            entry.LengthMs = data.Meta.LengthMs ?? (data.Objects.Count > 0 ? data.Objects.Max(o => o.End) : 0);
            if (!File.Exists(Path.Combine(dir, data.Meta.Audio))) entry.Error = $"audio file '{data.Meta.Audio}' is missing";
            var cover = Path.Combine(dir, "cover.png");
            if (File.Exists(cover)) entry.CoverPath = cover;
        }
        catch (Exception e)
        {
            entry.Error = e.Message;
        }
        return entry;
    }

    /// <summary>Unpacks a zipped level once, keyed by name, size and date, and returns the level folder.</summary>
    private static string Unzip(string zipPath)
    {
        var info = new FileInfo(zipPath);
        var key = $"{Path.GetFileNameWithoutExtension(zipPath)}-{info.Length}-{info.LastWriteTimeUtc.Ticks}";
        var target = Path.Combine(CacheDir, Sanitize(key));
        if (!Directory.Exists(target))
        {
            var tmp = target + ".tmp";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var full = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
                foreach (var e in archive.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;
                    var dest = Path.GetFullPath(Path.Combine(tmp, e.FullName));
                    // Refuse entries that would escape the cache folder.
                    if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
            }
            Directory.Move(tmp, target);
        }
        // The zip may hold the level files directly or inside one folder.
        if (File.Exists(Path.Combine(target, "level.json"))) return target;
        var inner = Directory.GetDirectories(target).FirstOrDefault(d => File.Exists(Path.Combine(d, "level.json")));
        return inner ?? throw new FileNotFoundException("no level.json inside the zip");
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }
}
