using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Sonokinesis.Editor;

/// <summary>Editor preferences kept in the user data folder: recent levels, game folder, author name.</summary>
public static class EditorSettings
{
    private sealed class Data
    {
        public List<string> recent { get; set; } = new();
        public string gameDir { get; set; }
        public string levelsRoot { get; set; }
        public string author { get; set; }
        public bool showConsole { get; set; }
        public bool checkUpdates { get; set; } = true;
    }

    private static Data data = new();
    private static string FilePath => Path.Combine(OS.GetUserDataDir(), "settings.json");

    public static IReadOnlyList<string> Recent => data.recent;

    public static string GameDir
    {
        get => data.gameDir;
        set { data.gameDir = value; Save(); }
    }

    public static string AuthorName
    {
        get => data.author ?? "";
        set { data.author = value; Save(); }
    }

    /// <summary>Whether BepInEx shows its log console next to the game (off unless asked for).</summary>
    public static bool ShowConsole
    {
        get => data.showConsole;
        set { data.showConsole = value; Save(); }
    }

    /// <summary>Whether the app looks for a new release when it starts.</summary>
    public static bool CheckUpdates
    {
        get => data.checkUpdates;
        set { data.checkUpdates = value; Save(); }
    }

    public static string LevelsRoot
    {
        get
        {
            var root = data.levelsRoot;
            if (string.IsNullOrEmpty(root))
            {
                var docs = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
                root = Path.Combine(docs, "Sonokinesis Levels");
                // Before the rename the default folder was "Beatform Levels": keep using it if it is there.
                var old = Path.Combine(docs, "Beatform Levels");
                if (!Directory.Exists(root) && Directory.Exists(old)) root = old;
            }
            Directory.CreateDirectory(root);
            return root;
        }
        set { data.levelsRoot = value; Save(); }
    }

    public static void Load()
    {
        try
        {
            // Settings from older versions carry over once: the app was "Sonokinesis Editor", before
            // that "Beatform Editor".
            foreach (var oldName in new[] { "Sonokinesis Editor", "Beatform Editor" })
            {
                var old = Path.Combine(Path.GetDirectoryName(OS.GetUserDataDir()) ?? "", oldName, "settings.json");
                if (File.Exists(FilePath) || !File.Exists(old)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.Copy(old, FilePath);
            }
            if (File.Exists(FilePath)) data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
        }
        catch { data = new Data(); }
    }

    public static void AddRecent(string folder)
    {
        data.recent.RemoveAll(r => string.Equals(r, folder, StringComparison.OrdinalIgnoreCase));
        data.recent.Insert(0, folder);
        if (data.recent.Count > 12) data.recent.RemoveRange(12, data.recent.Count - 12);
        Save();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
