using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Beatform.Editor;

/// <summary>Editor preferences kept in the user data folder: recent levels, game folder, author name.</summary>
public static class EditorSettings
{
    private sealed class Data
    {
        public List<string> recent { get; set; } = new();
        public string gameDir { get; set; }
        public string levelsRoot { get; set; }
        public string author { get; set; }
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

    public static string LevelsRoot
    {
        get
        {
            var root = data.levelsRoot;
            if (string.IsNullOrEmpty(root))
                root = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "Beatform Levels");
            Directory.CreateDirectory(root);
            return root;
        }
        set { data.levelsRoot = value; Save(); }
    }

    public static void Load()
    {
        try
        {
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
