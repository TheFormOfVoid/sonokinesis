using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Beatform.Core;
using Godot;
using FileAccess = System.IO.File;

namespace Beatform.Editor.Scripts;

/// <summary>
/// The scripts the editor knows: the built-in library (library/ next to the editor) and the open level's
/// own scripts (LEVEL/scripts/*.js). A level only ever runs its own copies, so placing a library script
/// copies it into the level first; edit that copy freely. Watches the level folder so changes made
/// outside the editor (a text editor, an AI) show up at once.
/// </summary>
public sealed class ScriptsService
{
    public static ScriptsService I { get; } = new();

    /// <summary>The built-in library folder, or null if it is missing.</summary>
    public string LibraryDir { get; private set; }
    public string LevelFolder { get; private set; }
    public string LevelScriptsDir => LevelFolder == null ? null : Path.Combine(LevelFolder, "scripts");

    /// <summary>Script sources by name.</summary>
    public readonly Dictionary<string, string> LevelSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> libraryPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> libraryCategoryDirs = new(StringComparer.OrdinalIgnoreCase);
    private ScriptLibrary library, level;

    /// <summary>Raised on the main thread after scripts were (re)loaded.</summary>
    public event Action ScriptsChanged;
    /// <summary>Raised on the main thread when level.json changed on disk (not by our own save).</summary>
    public event Action LevelFileChanged;

    private FileSystemWatcher watcher;
    private volatile bool scriptsDirty, levelDirty;
    private string lastSavedLevelJson;

    private ScriptsService()
    {
        LibraryDir = FindLibrary();
        LoadLibrary();
    }

    private static string FindLibrary()
    {
        var candidates = new List<string>();
        try
        {
            var exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
            candidates.Add(Path.Combine(exeDir, "library"));
            // In the release, library/ sits beside the editor's folder (shared with the beatform tool).
            candidates.Add(Path.GetFullPath(Path.Combine(exeDir, "..", "library")));
        }
        catch { }
        try { candidates.Add(Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "library"))); } catch { }
        return candidates.FirstOrDefault(Directory.Exists);
    }

    private void LoadLibrary()
    {
        libraryPaths.Clear();
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (LibraryDir != null)
        {
            foreach (var f in Directory.GetFiles(LibraryDir, "*.js", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                libraryPaths[name] = f;
                libraryCategoryDirs[name] = Path.GetFileName(Path.GetDirectoryName(f));
                try { sources[name] = FileAccess.ReadAllText(f); } catch { }
            }
        }
        library = new ScriptLibrary(sources);
    }

    /// <summary>Switches to a level folder (null for an unsaved level): loads its scripts and watches it.</summary>
    public void Bind(string folder)
    {
        LevelFolder = folder;
        watcher?.Dispose();
        watcher = null;
        lastSavedLevelJson = null;
        if (folder != null && Directory.Exists(folder))
        {
            try
            {
                watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                watcher.Changed += OnFsEvent;
                watcher.Created += OnFsEvent;
                watcher.Deleted += OnFsEvent;
                watcher.Renamed += (s, e) => OnFsEvent(s, e);
                watcher.EnableRaisingEvents = true;
            }
            catch { watcher = null; }
            try { lastSavedLevelJson = FileAccess.ReadAllText(Path.Combine(folder, "level.json")); } catch { }
        }
        ReloadLevelScripts();
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        var name = Path.GetFileName(e.FullPath);
        if (e.FullPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) scriptsDirty = true;
        else if (string.Equals(name, "level.json", StringComparison.OrdinalIgnoreCase)) levelDirty = true;
    }

    /// <summary>Call every frame (main thread): applies changes seen on disk.</summary>
    public void Poll()
    {
        if (scriptsDirty)
        {
            scriptsDirty = false;
            ReloadLevelScripts();
        }
        if (levelDirty)
        {
            levelDirty = false;
            try
            {
                var now = FileAccess.ReadAllText(Path.Combine(LevelFolder, "level.json"));
                if (now != lastSavedLevelJson) { lastSavedLevelJson = now; LevelFileChanged?.Invoke(); }
            }
            catch { }
        }
    }

    /// <summary>The editor saved level.json itself: do not treat that as an outside change.</summary>
    public void NoteSaved(string json) => lastSavedLevelJson = json;

    public void ReloadLevelScripts()
    {
        LevelSources.Clear();
        var dir = LevelScriptsDir;
        if (dir != null && Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir, "*.js"))
            {
                try { LevelSources[Path.GetFileNameWithoutExtension(f)] = FileAccess.ReadAllText(f); } catch { }
            }
        level = new ScriptLibrary(LevelSources);
        ScriptsChanged?.Invoke();
    }

    public void ReloadLibrary()
    {
        LoadLibrary();
        ScriptsChanged?.Invoke();
    }

    /// <summary>A script by name: the level's copy first, else the library's.</summary>
    public ScriptType Find(string name) => level?.Get(name) ?? library?.Get(name);
    public ScriptType FindInLevel(string name) => level?.Get(name);
    public bool IsInLevel(string name) => LevelSources.ContainsKey(name ?? "");
    public bool IsInLibrary(string name) => libraryPaths.ContainsKey(name ?? "");

    /// <summary>Every script the palette can offer: the level's own, then library ones it does not override.</summary>
    public IEnumerable<ScriptType> All
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (level != null) foreach (var t in level.All.OrderBy(t => t.Title)) { seen.Add(t.Name); yield return t; }
            if (library != null) foreach (var t in library.All.OrderBy(t => t.Title)) if (!seen.Contains(t.Name)) yield return t;
        }
    }

    public IEnumerable<ScriptError> LevelCompileErrors => level?.CompileErrors ?? Enumerable.Empty<ScriptError>();

    /// <summary>Where a script lives (the level's copy, else the library file).</summary>
    public string PathOf(string name)
    {
        if (LevelScriptsDir != null)
        {
            var p = Path.Combine(LevelScriptsDir, name + ".js");
            if (FileAccess.Exists(p)) return p;
        }
        return libraryPaths.TryGetValue(name, out var lp) ? lp : null;
    }

    /// <summary>The library folder a script comes from ("attacks", "effects", "patterns"), or "".</summary>
    public string LibraryCategory(string name) => libraryCategoryDirs.TryGetValue(name, out var c) ? c : "";

    private static readonly Regex SpawnRef = new(@"spawn\(\s*['""]([A-Za-z0-9_\-]+)['""]");

    /// <summary>
    /// Makes sure the level has its own copy of a script (copying it from the library, with the scripts it
    /// spawns or lists in meta.uses). False with a reason when that is not possible.
    /// </summary>
    public bool EnsureInLevel(string name, out string error)
    {
        error = null;
        if (IsInLevel(name)) return true;
        if (LevelFolder == null) { error = "Save the level first: scripts are kept in its folder."; return false; }
        var todo = new Stack<string>();
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        todo.Push(name);
        Directory.CreateDirectory(LevelScriptsDir);
        while (todo.Count > 0)
        {
            var n = todo.Pop();
            if (!copied.Add(n) || IsInLevel(n)) continue;
            if (!libraryPaths.TryGetValue(n, out var src)) { if (n == name) { error = $"No script named '{name}'."; return false; } continue; }
            var text = FileAccess.ReadAllText(src);
            FileAccess.WriteAllText(Path.Combine(LevelScriptsDir, n + ".js"), text);
            foreach (Match m in SpawnRef.Matches(text)) todo.Push(m.Groups[1].Value);
            var t = library.Get(n);
            if (t != null) foreach (var u in t.Uses) todo.Push(u);
        }
        ReloadLevelScripts();
        return true;
    }

    /// <summary>Creates LEVEL/scripts/NAME.js from a template; returns its path, or null with a reason.</summary>
    public string Create(string name, string kind, out string error)
    {
        error = null;
        if (LevelFolder == null) { error = "Save the level first: scripts are kept in its folder."; return null; }
        if (!Regex.IsMatch(name ?? "", "^[A-Za-z0-9_\\-]+$")) { error = "Use letters, digits, _ and - only."; return null; }
        Directory.CreateDirectory(LevelScriptsDir);
        var path = Path.Combine(LevelScriptsDir, name + ".js");
        if (FileAccess.Exists(path)) { error = "A script with that name already exists."; return null; }
        FileAccess.WriteAllText(path, ScriptTemplates.For(kind, name));
        ReloadLevelScripts();
        return path;
    }
}
