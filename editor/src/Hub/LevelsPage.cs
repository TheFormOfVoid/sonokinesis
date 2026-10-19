using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Sonokinesis.Editor.UI;

namespace Sonokinesis.Editor.Hub;

/// <summary>Levels: what the game will list, with adding by drag and drop, playing, editing and removing.</summary>
public partial class LevelsPage : HubPage
{
    private Label countLabel, folderLabel;
    private LineEdit search;
    private VBoxContainer list;
    private List<LevelInfo> levels = new();
    private string shownFolder, shownStamp;

    public LevelsPage()
    {
        countLabel = HubUi.Text("", 15, UiTheme.Dim);
        countLabel.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        var title = HubUi.PageTitle("Levels");
        var add = HubUi.Primary("＋  Add levels…", PickLevels, UiTheme.Cyan, 15);
        Body.AddChild(HubUi.Row(14, title, countLabel, HubUi.Grow(), HubUi.Secondary("Open folder", OpenFolder, "Open the levels folder in Explorer"), add));
        folderLabel = HubUi.Dim("", 13);
        Body.AddChild(folderLabel);

        var drop = new PanelContainer();
        var sb = HubUi.Box(new Color(UiTheme.Cyan, 0.05f), 12, new Color(UiTheme.Cyan, 0.35f), 2, 18, 20);
        drop.AddThemeStyleboxOverride("panel", sb);
        var dropText = HubUi.Text("⇩   Drop level .zip files or level folders anywhere on this window to add them.", 15, UiTheme.Cyan.Lightened(0.2f), HubUi.Semi);
        dropText.HorizontalAlignment = HorizontalAlignment.Center;
        drop.AddChild(dropText);
        Body.AddChild(drop);

        search = new LineEdit { PlaceholderText = "Search by title, artist or author", ClearButtonEnabled = true };
        search.TextChanged += _ => Rebuild();
        Body.AddChild(search);

        list = HubUi.Column(10);
        Body.AddChild(list);
    }

    private static string Folder => GameInstall.LevelsFolder(GameInstall.Game);

    public override void Refresh()
    {
        var folder = Folder;
        folderLabel.Text = folder == null ? "" : "Levels folder: " + folder;
        // Rescan only when something in the folder changed.
        var stamp = Stamp(folder);
        if (folder == shownFolder && stamp == shownStamp) return;
        shownFolder = folder;
        shownStamp = stamp;
        levels = LevelLibrary.Scan(folder);
        Rebuild();
    }

    private static string Stamp(string folder)
    {
        try
        {
            if (folder == null || !Directory.Exists(folder)) return "";
            return string.Join("|", Directory.EnumerateFileSystemEntries(folder).Select(e => e + ":" + File.GetLastWriteTimeUtc(e).Ticks)
                .Concat(Directory.GetDirectories(folder).Select(d => Path.Combine(d, "level.json")).Where(File.Exists).Select(f => File.GetLastWriteTimeUtc(f).Ticks.ToString())));
        }
        catch { return ""; }
    }

    private void Rebuild()
    {
        foreach (var c in list.GetChildren()) c.QueueFree();
        var q = search.Text.Trim();
        var shown = levels.Where(l => q.Length == 0 || $"{l.Title} {l.Artist} {l.Author}".Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        countLabel.Text = levels.Count == 1 ? "1 level" : levels.Count + " levels";

        if (GameInstall.Game == null)
        {
            list.AddChild(Empty("The game was not found yet.", "Choose the Just Shapes & Beats folder on the Play tab, then your levels show up here."));
            return;
        }
        if (levels.Count == 0) { list.AddChild(Empty("No levels yet.", "Drop level .zip files on this window, or make your own in the Create tab.")); return; }
        if (shown.Count == 0) { list.AddChild(Empty("Nothing matches your search.", "")); return; }
        foreach (var l in shown) list.AddChild(LevelCard(l));
    }

    private static Control Empty(string title, string text)
    {
        var card = HubUi.Card(out var b, 34);
        var t = HubUi.Text(title, 19, UiTheme.Text, HubUi.Semi);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        b.AddChild(t);
        if (text.Length > 0) { var d = HubUi.Dim(text); d.HorizontalAlignment = HorizontalAlignment.Center; b.AddChild(d); }
        return card;
    }

    private Control LevelCard(LevelInfo l)
    {
        var card = HubUi.Card(out var body, 14);
        var row = HubUi.Row(16);
        body.AddChild(row);
        row.AddChild(new LevelSwatch(UiTheme.ParseHex(l.Background, UiTheme.Ink), UiTheme.ParseHex(l.Hazard, UiTheme.Pink)));

        var text = HubUi.Column(3);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var title = HubUi.Text(l.Title, 18, UiTheme.Text, HubUi.Semi);
        title.ClipText = true;
        text.AddChild(title);
        if (l.Artist.Length > 0) { var a = HubUi.Text(l.Artist, 14, UiTheme.Dim); a.ClipText = true; text.AddChild(a); }
        var facts = new List<string>();
        if (l.Author.Length > 0) facts.Add("by " + l.Author);
        if (l.LengthMs > 0) facts.Add(TimeSpan.FromMilliseconds(l.LengthMs).ToString(@"m\:ss"));
        if (l.Bpm > 0) facts.Add($"{l.Bpm:0.#} BPM");
        if (l.Difficulty > 0) facts.Add(new string('★', Math.Clamp(l.Difficulty, 1, 5)) + new string('☆', 5 - Math.Clamp(l.Difficulty, 1, 5)));
        facts.Add(l.IsZip ? "zip" : "folder");
        text.AddChild(HubUi.Text(string.Join("   ·   ", facts), 13, UiTheme.Dim.Darkened(0.1f)));
        if (l.Error != null) text.AddChild(HubUi.Text("This level can't be read: " + l.Error, 13, UiTheme.Bad));
        row.AddChild(text);

        if (l.Error == null)
        {
            var play = HubUi.Primary("▶  Play", () => PlayLevel(l), UiTheme.Cyan, 14);
            play.TooltipText = "Start this level in the game";
            play.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(play);
            var edit = HubUi.Secondary("Edit", () => EditLevel(l), l.IsZip ? "Open a copy in the editor (zipped levels are unpacked into your own levels folder)" : "Open in the editor");
            edit.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(edit);
        }
        var more = new MenuButton { Text = "⋯", Flat = false, FocusMode = FocusModeEnum.None, TooltipText = "More", SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(44, 0) };
        more.AddThemeStyleboxOverride("normal", HubUi.Box(UiTheme.Panel2, 7, UiTheme.Line, 1, 9, 12));
        more.AddThemeStyleboxOverride("hover", HubUi.Box(new Color("232837"), 7, UiTheme.Cyan.Darkened(0.35f), 1, 9, 12));
        more.AddThemeStyleboxOverride("pressed", HubUi.Box(new Color("2b3142"), 7, UiTheme.Pink, 1, 9, 12));
        var pm = more.GetPopup();
        pm.AddItem("Show in Explorer", 0);
        pm.AddItem("Remove (to the Recycle Bin)", 1);
        pm.IdPressed += id =>
        {
            if (id == 0) OS.ShellShowInFileManager(l.Path);
            else Hub.I.Confirm("Remove level", $"Move '{l.Title}' to the Recycle Bin?", "Remove", () =>
            {
                try { LevelLibrary.Remove(l); Hub.I.Toast($"Moved '{l.Title}' to the Recycle Bin."); }
                catch (Exception e) { Hub.I.Toast("Could not remove it: " + e.Message, false); }
                shownStamp = null;
                Refresh();
            });
        };
        row.AddChild(more);
        if (l.Description.Length > 0) card.TooltipText = l.Description;
        return card;
    }

    // ---------------- actions ----------------

    public void AddLevels(IEnumerable<string> paths)
    {
        var folder = Folder;
        if (folder == null) { Hub.I.Toast("Just Shapes & Beats was not found, so there is no levels folder yet. Choose the game folder on the Play tab.", false); return; }
        var lines = LevelLibrary.Add(folder, paths);
        foreach (var (text, ok) in lines) Hub.I.Toast(text, ok);
        shownStamp = null;
        Refresh();
    }

    private void PickLevels() => Hub.I.PickFiles("Add levels", new[] { "*.zip ; Zipped levels", "level.json ; A level's level.json" }, f => AddLevels(f));

    private void OpenFolder()
    {
        var folder = Folder;
        if (folder == null) { Hub.I.Toast("The game was not found yet.", false); return; }
        Directory.CreateDirectory(folder);
        OS.ShellOpen(folder);
    }

    private static string Cache(string kind, LevelInfo l) =>
        Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Sonokinesis", kind, App.Sanitize(Path.GetFileNameWithoutExtension(l.Path)));

    private void PlayLevel(LevelInfo l)
    {
        Hub.I.Page<PlayPage>().EnsureReady(fresh =>
        {
            try
            {
                var folder = LevelLibrary.Unpack(l, Cache("play", l));
                var (ok, message) = GameBridge.RequestPlay(folder, 0);
                if (!ok) { Hub.I.Toast(message, false); return; }
                bool running = GameInstall.GameRunning;
                if (!running) GameInstall.LaunchGame();
                Hub.I.Toast(running ? $"'{l.Title}' starts in the game now."
                    : fresh ? $"Starting the game. '{l.Title}' opens once the main menu is reached (the first start after installing takes a minute or two)."
                    : $"Starting the game. '{l.Title}' opens once the main menu is reached.");
            }
            catch (Exception e) { Hub.I.Toast("Could not start it: " + e.Message, false); }
        });
    }

    private void EditLevel(LevelInfo l)
    {
        if (!l.IsZip) { Hub.I.OpenEditor(l.Path); return; }
        try
        {
            // Zipped levels are edited as a folder in the editor's levels folder.
            var name = App.Sanitize(l.Title.Length > 0 ? l.Title : Path.GetFileNameWithoutExtension(l.Path));
            var dest = Path.Combine(EditorSettings.LevelsRoot, name);
            for (int i = 2; Directory.Exists(dest); i++) dest = Path.Combine(EditorSettings.LevelsRoot, $"{name} ({i})");
            LevelLibrary.Unpack(l, dest);
            Hub.I.Toast($"Unpacked a copy to edit into {dest}.");
            Hub.I.OpenEditor(dest);
        }
        catch (Exception e) { Hub.I.Toast("Could not unpack it: " + e.Message, false); }
    }
}

/// <summary>A small picture of a level: its background with its hazard color.</summary>
public partial class LevelSwatch : Control
{
    private readonly Color bg, hazard;
    public LevelSwatch() : this(UiTheme.Ink, UiTheme.Pink) { }
    public LevelSwatch(Color bg, Color hazard) { this.bg = bg; this.hazard = hazard; CustomMinimumSize = new Vector2(64, 64); SizeFlagsVertical = SizeFlags.ShrinkCenter; }

    public override void _Draw()
    {
        float u = Size.X / 64f;   // drawn at 64 px, scaled to the size
        var sb = HubUi.Box(bg, (int)(10 * u), new Color(hazard, 0.5f), Math.Max(1, (int)(2 * u)), 0, 0);
        DrawStyleBox(sb, new Rect2(Vector2.Zero, Size));
        var c = Size / 2;
        DrawColoredPolygon(new[] { c + new Vector2(0, -15) * u, c + new Vector2(14, 11) * u, c + new Vector2(-14, 11) * u }, hazard);
        DrawCircle(c + new Vector2(16, -14) * u, 4.5f * u, new Color(hazard, 0.6f));
        DrawRect(new Rect2(c + new Vector2(-21, 15) * u, new Vector2(10, 3.5f) * u), new Color(hazard, 0.5f));
    }
}
