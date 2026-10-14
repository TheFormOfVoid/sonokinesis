using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Beatform.Editor.Audio;
using Beatform.Editor.Model;
using Beatform.Editor.UI;
using Godot;

namespace Beatform.Editor;

/// <summary>The editor window: layout, playback, files, shortcuts and the commands panels share.</summary>
public partial class App : Control
{
    public static App Instance { get; private set; }
    private static EditorState S => EditorState.I;

    public SongPlayer Song { get; private set; }
    public CanvasView Canvas { get; private set; }
    public TimelineView Timeline { get; private set; }
    public InspectorPanel Inspector { get; private set; }
    public PalettePanel Palette { get; private set; }

    private Label titleLabel, timeLabel, statusLabel, statsLabel;
    private Button playButton;
    private PopupMenu objectMenu;
    private double statusUntil;
    private readonly List<EditObject> clipboard = new();
    private double clipboardBase;
    private AutoTest autotest;

    public override void _Ready()
    {
        Instance = this;
        Theme = UiTheme.Build();
        GetWindow().Title = "Beatform Editor";
        GetWindow().MinSize = new Vector2I(1100, 640);
        EditorSettings.Load();

        Song = new SongPlayer();
        AddChild(Song);

        var bg = new ColorRect { Color = UiTheme.Ink };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        root.AddChild(BuildTopBar());

        var vsplit = new VSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(vsplit);

        var middle = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", 0);
        vsplit.AddChild(middle);
        Palette = new PalettePanel();
        middle.AddChild(Palette);
        Canvas = new CanvasView { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        middle.AddChild(Canvas);
        Canvas.Init(Song);
        Inspector = new InspectorPanel();
        middle.AddChild(Inspector);

        Timeline = new TimelineView { CustomMinimumSize = new Vector2(0, 250) };
        vsplit.AddChild(Timeline);
        Timeline.Init(Song);

        root.AddChild(BuildStatusBar());

        objectMenu = new PopupMenu();
        AddChild(objectMenu);
        objectMenu.IdPressed += OnObjectMenu;

        S.DocChanged += UpdateTitle;
        S.DocReplaced += () => { UpdateTitle(); ReloadSong(); };
        Scripts.ScriptsService.I.ScriptsChanged += () => S.ReloadScripts();
        Scripts.ScriptsService.I.LevelFileChanged += OnLevelFileChanged;
        S.ScriptsReloaded += () => { Canvas.QueueRedraw(); Inspector.Sync(); Palette.RefreshScripts(); };
        GetTree().AutoAcceptQuit = false;

        var args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--autotest");
        if (at >= 0 && at + 1 < args.Length) autotest = new AutoTest(args[at + 1]);
        else
        {
            int open = Array.IndexOf(args, "--open");
            if (open >= 0 && open + 1 < args.Length) OpenFolder(args[open + 1]);
            else if (EditorSettings.Recent.FirstOrDefault(r => File.Exists(Path.Combine(r, "level.json"))) is string recent) OpenFolder(recent);
            else S.Open(new LevelDocument());
        }
        UpdateTitle();
    }

    // ---------------- layout ----------------

    private Control BuildTopBar()
    {
        var panel = new PanelContainer();
        var sb = new StyleBoxFlat { BgColor = UiTheme.Panel, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6, BorderColor = UiTheme.Pink, BorderWidthBottom = 2 };
        panel.AddThemeStyleboxOverride("panel", sb);
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 6);
        panel.AddChild(bar);

        var logo = new Label { Text = "BEATFORM" };
        logo.AddThemeFontSizeOverride("font_size", 16);
        logo.AddThemeColorOverride("font_color", UiTheme.Pink);
        bar.AddChild(logo);
        bar.AddChild(Spacer(8));
        bar.AddChild(Btn("New", NewLevel, "New level from a song (Ctrl+N)"));
        var open = new MenuButton { Text = "Open ▾", TooltipText = "Open a level (Ctrl+O) or a recent one", Flat = false };
        open.AboutToPopup += () =>
        {
            var pm = open.GetPopup();
            pm.Clear();
            pm.AddItem("Open level…   Ctrl+O", 0);
            var recent = EditorSettings.Recent.Where(r => File.Exists(Path.Combine(r, "level.json"))).Take(10).ToList();
            if (recent.Count > 0) pm.AddSeparator("Recent");
            for (int i = 0; i < recent.Count; i++) { pm.AddItem(recent[i], 100 + i); }
        };
        open.GetPopup().IdPressed += id =>
        {
            if (id == 0) { OpenLevel(); return; }
            var recent = EditorSettings.Recent.Where(r => File.Exists(Path.Combine(r, "level.json"))).Take(10).ToList();
            int i = (int)id - 100;
            if (i >= 0 && i < recent.Count) { var folder = recent[i]; ConfirmDiscard(() => OpenFolder(folder)); }
        };
        bar.AddChild(open);
        bar.AddChild(Btn("Save", () => Save(), "Save (Ctrl+S)"));
        bar.AddChild(Btn("Level…", ShowLevelSettings, "Title, song, BPM, colors"));
        var export = new MenuButton { Text = "Share ▾", TooltipText = "Export or install the level", Flat = false };
        export.GetPopup().AddItem("Export as .zip…", 0);
        export.GetPopup().AddItem("Copy into the game's CustomLevels folder", 1);
        export.GetPopup().AddItem("Open level folder", 2);
        export.GetPopup().IdPressed += id => { if (id == 0) ExportZip(); else if (id == 1) InstallToGame(); else OpenLevelFolder(); };
        bar.AddChild(export);
        bar.AddChild(Spacer(10));

        titleLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        titleLabel.AddThemeColorOverride("font_color", UiTheme.Text);
        bar.AddChild(titleLabel);

        bar.AddChild(Btn("⏮", () => { S.Playhead = 0; PlayheadMoved(); }, "Back to start (Home)"));
        playButton = Btn("▶ Play", TogglePlay, "Play / pause (Space)");
        playButton.CustomMinimumSize = new Vector2(84, 0);
        bar.AddChild(playButton);
        timeLabel = new Label { CustomMinimumSize = new Vector2(150, 0), HorizontalAlignment = HorizontalAlignment.Center };
        timeLabel.AddThemeColorOverride("font_color", UiTheme.Cyan);
        bar.AddChild(timeLabel);

        var snap = new OptionButton { TooltipText = "Snap to the beat grid" };
        foreach (var (label, div) in new[] { ("snap off", 0), ("1 beat", 1), ("1/2 beat", 2), ("1/3 beat", 3), ("1/4 beat", 4), ("1/6 beat", 6), ("1/8 beat", 8), ("1/16 beat", 16) }) snap.AddItem(label, div);
        snap.Selected = 4;
        snap.ItemSelected += i => { S.SnapDivision = snap.GetItemId((int)i); Timeline.QueueRedraw(); };
        bar.AddChild(snap);
        var speed = new OptionButton { TooltipText = "Playback speed (changes pitch)" };
        foreach (var s in new[] { "25%", "50%", "75%", "100%" }) speed.AddItem(s);
        speed.Selected = 3;
        speed.ItemSelected += i => Song.Speed = new[] { 0.25f, 0.5f, 0.75f, 1f }[i];
        bar.AddChild(speed);
        bar.AddChild(Spacer(8));
        var inGame = Btn("Test in game", TestInGame, "Start the level in the running game from the playhead (F5). Needs the Beatform loader.");
        inGame.AddThemeColorOverride("font_color", UiTheme.Pink);
        bar.AddChild(inGame);
        return panel;
    }

    private Control BuildStatusBar()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("0e1016"), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 3, ContentMarginBottom = 3 });
        var bar = new HBoxContainer();
        panel.AddChild(bar);
        statusLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        statusLabel.AddThemeFontSizeOverride("font_size", 12);
        statusLabel.AddThemeColorOverride("font_color", UiTheme.Dim);
        bar.AddChild(statusLabel);
        statsLabel = new Label();
        statsLabel.AddThemeFontSizeOverride("font_size", 12);
        statsLabel.AddThemeColorOverride("font_color", UiTheme.Dim);
        bar.AddChild(statsLabel);
        return panel;
    }

    private static Control Spacer(float w) => new Control { CustomMinimumSize = new Vector2(w, 0) };

    private static Button Btn(string text, Action onPress, string tooltip)
    {
        var b = new Button { Text = text, TooltipText = tooltip, FocusMode = FocusModeEnum.None };
        b.Pressed += onPress;
        return b;
    }

    /// <summary>The last status-bar message (tests and the editor bridge read it).</summary>
    public string LastStatus { get; private set; } = "";

    public void Status(string text, bool warn = false)
    {
        LastStatus = text;
        if (warn) GD.Print("status: " + text);
        statusLabel.Text = text;
        statusLabel.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.Dim);
        statusUntil = Time.GetTicksMsec() / 1000.0 + 8;
    }

    private void UpdateTitle()
    {
        var d = S.Doc;
        titleLabel.Text = $"{d.Title}{(string.IsNullOrEmpty(d.Artist) ? "" : " — " + d.Artist)}{(S.Dirty ? "   ●" : "")}";
        GetWindow().Title = $"{(S.Dirty ? "● " : "")}{d.Title} · Beatform Editor";
        statsLabel.Text = $"{d.Objects.Count} objects · {d.Events.Count} events · {d.Bpm:0.##} BPM   ";
    }

    // ---------------- frame ----------------

    public override void _Process(double delta)
    {
        if (S.Playing)
        {
            if (Song.HasSong)
            {
                S.Playhead = Song.SongMs;
                double end = S.Doc.LengthMs ?? Math.Max(Song.LengthMs - S.Doc.OffsetMs, S.Doc.ContentEnd);
                if (!Song.Playing || S.Playhead >= end) StopPlayback();
            }
            else
            {
                S.Playhead += delta * 1000 * Song.Speed;
                if (S.Playhead > S.Doc.ContentEnd + 2000) StopPlayback();
            }
            Inspector.Refresh();
        }
        timeLabel.Text = $"{S.FormatTime(S.Playhead)}  ·  {S.FormatBeats(S.Playhead)}";
        if (statusUntil > 0 && Time.GetTicksMsec() / 1000.0 > statusUntil) { statusLabel.Text = ""; statusUntil = 0; }
        GameBridge.Poll(this);
        Scripts.ScriptsService.I.Poll();
        EditorBridge.Poll(this);
        autotest?.Step(this);
        AutosaveTick();
    }

    private const string AutosaveName = "level.autosave.json";
    private double nextAutosave = 120;

    /// <summary>Every two minutes, unsaved changes are written next to the level (never over it).</summary>
    private void AutosaveTick()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < nextAutosave) return;
        nextAutosave = now + 120;
        if (!S.Dirty || S.Doc.FolderPath == null || autotest != null) return;
        try
        {
            File.WriteAllText(Path.Combine(S.Doc.FolderPath, AutosaveName), S.Doc.ToJson());
            Status("Autosaved unsaved changes (" + AutosaveName + ").");
        }
        catch { }
    }

    /// <summary>After opening, offers the autosave when it is newer than the saved level.</summary>
    private void OfferAutosave(string folder)
    {
        var auto = Path.Combine(folder, AutosaveName);
        var main = Path.Combine(folder, "level.json");
        if (!File.Exists(auto) || File.GetLastWriteTimeUtc(auto) <= File.GetLastWriteTimeUtc(main)) return;
        var dlg = new ConfirmationDialog { Title = "Unsaved work found", DialogText = "This level has an autosave newer than its last save (the editor may have closed unexpectedly). Restore it?", OkButtonText = "Restore" };
        dlg.Confirmed += () =>
        {
            try
            {
                var doc = LevelDocument.FromJson(File.ReadAllText(auto), folder);
                S.Open(doc);
                S.Touch();
                Status("Restored the autosave. Save to keep it.");
            }
            catch (Exception e) { Status("Could not restore the autosave: " + e.Message, true); }
            dlg.QueueFree();
        };
        dlg.Canceled += () => { try { File.Delete(auto); } catch { } dlg.QueueFree(); };
        AddChild(dlg);
        dlg.PopupCentered();
    }

    public void PlayheadMoved()
    {
        if (S.Playing) StartPlayback(S.Playhead);
        Canvas.QueueRedraw();
        Timeline.QueueRedraw();
        Inspector.Refresh();
    }

    public void StartPlayback(double fromMs)
    {
        S.Playhead = fromMs;
        S.Playing = true;
        Song.Play(fromMs);
        playButton.Text = "❚❚ Pause";
    }

    public void StopPlayback()
    {
        S.Playing = false;
        Song.Stop();
        playButton.Text = "▶ Play";
        Canvas.QueueRedraw();
        Timeline.QueueRedraw();
        Inspector.Refresh();
    }

    private void TogglePlay()
    {
        if (S.Playing) StopPlayback();
        else StartPlayback(S.Playhead);
    }

    // ---------------- keyboard ----------------

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed) return;
        bool ctrl = k.CtrlPressed || k.MetaPressed;
        bool handled = true;
        switch (k.Keycode)
        {
            case Key.Space when !ctrl: TogglePlay(); break;
            case Key.S when ctrl: Save(); break;
            case Key.O when ctrl: OpenLevel(); break;
            case Key.N when ctrl: NewLevel(); break;
            case Key.Z when ctrl && k.ShiftPressed: S.Redo(); break;
            case Key.Z when ctrl: S.Undo(); break;
            case Key.Y when ctrl: S.Redo(); break;
            case Key.C when ctrl: Copy(); break;
            case Key.V when ctrl: Paste(); break;
            case Key.X when ctrl: Copy(); DeleteSelection(); break;
            case Key.D when ctrl: DuplicateSelection(); break;
            case Key.A when ctrl: S.Select(S.Doc.Objects.Select(o => o.Id)); break;
            case Key.G when ctrl: SelectGroup(); break;
            case Key.Delete or Key.Backspace: DeleteSelection(); break;
            case Key.K: KeyAll(); break;
            case Key.F5: TestInGame(); break;
            case Key.Home: S.Playhead = 0; PlayheadMoved(); break;
            case Key.End: S.Playhead = S.Doc.ContentEnd; PlayheadMoved(); break;
            case Key.Comma: Step(-1); break;
            case Key.Period: Step(1); break;
            case Key.Equal or Key.KpAdd: Timeline.ZoomBy(1.25); break;
            case Key.Minus or Key.KpSubtract: Timeline.ZoomBy(0.8); break;
            case Key.Left: Nudge(-1, 0, k.ShiftPressed); break;
            case Key.Right: Nudge(1, 0, k.ShiftPressed); break;
            case Key.Up: Nudge(0, -1, k.ShiftPressed); break;
            case Key.Down: Nudge(0, 1, k.ShiftPressed); break;
            case Key.Escape: S.ClearSelection(); break;
            default: handled = false; break;
        }
        if (handled) GetViewport().SetInputAsHandled();
    }

    private void Step(int dir)
    {
        double g = S.GridMs > 0 ? S.GridMs : S.Doc.BeatMs / 4;
        S.Playhead = Math.Max(0, S.Snap(S.Playhead) + dir * g);
        PlayheadMoved();
    }

    private void Nudge(int dx, int dy, bool big)
    {
        if (S.Selected.Count == 0) return;
        double step = big ? 10 : 1;
        S.Edit("Nudge", () =>
        {
            foreach (var o in S.SelectedObjects)
            {
                double lt = S.Playhead - o.Start;
                if (dx != 0) CanvasView.SetAt(o, "x", lt, CanvasView.Eval(o, "x", lt) + dx * step);
                if (dy != 0) CanvasView.SetAt(o, "y", lt, CanvasView.Eval(o, "y", lt) + dy * step);
            }
        });
    }

    private void KeyAll()
    {
        if (S.Selected.Count == 0) return;
        S.Edit("Key all tracks", () =>
        {
            foreach (var o in S.SelectedObjects)
                foreach (var t in new[] { "x", "y", "rot", "w", "h" }) CanvasView.AddKeyAt(o, t, S.Playhead - o.Start);
        });
        Status("Keyed position, rotation and size at the playhead. Now scrub and move the object to animate it.");
    }

    // ---------------- object commands ----------------

    public void ShowObjectMenu(Vector2 screenPos)
    {
        objectMenu.Clear();
        bool any = S.Selected.Count > 0;
        objectMenu.AddItem("Copy   Ctrl+C", 1); objectMenu.SetItemDisabled(0, !any);
        objectMenu.AddItem("Paste at playhead   Ctrl+V", 2); objectMenu.SetItemDisabled(1, clipboard.Count == 0);
        objectMenu.AddItem("Duplicate   Ctrl+D", 3); objectMenu.SetItemDisabled(2, !any);
        objectMenu.AddItem("Delete   Del", 4); objectMenu.SetItemDisabled(3, !any && S.SelectedEvent < 0 && S.SelectedCheckpoint < 0);
        objectMenu.AddSeparator();
        objectMenu.AddItem("Key all tracks at playhead   K", 5);
        objectMenu.AddItem("Select the whole pattern   Ctrl+G", 6);
        objectMenu.AddItem("Mirror horizontally", 7);
        objectMenu.AddItem("Mirror vertically", 8);
        objectMenu.AddItem("Repeat…", 11);
        objectMenu.AddItem("Stretch time…", 12);
        objectMenu.AddItem("Bring to front", 9);
        objectMenu.AddItem("Send to back", 10);
        objectMenu.Position = (Vector2I)screenPos;
        objectMenu.Popup();
    }

    private void OnObjectMenu(long id)
    {
        switch (id)
        {
            case 1: Copy(); break;
            case 2: Paste(); break;
            case 3: DuplicateSelection(); break;
            case 4: DeleteSelection(); break;
            case 5: KeyAll(); break;
            case 6: SelectGroup(); break;
            case 7: Mirror(true); break;
            case 8: Mirror(false); break;
            case 9: S.Edit("Bring to front", () => { int top = S.Doc.Objects.Max(o => o.Layer); foreach (var o in S.SelectedObjects) o.Layer = top + 1; }); break;
            case 11: ToolDialogs.ShowRepeat(this); break;
            case 12: ToolDialogs.ShowStretch(this); break;
            case 10: S.Edit("Send to back", () => { int bottom = S.Doc.Objects.Min(o => o.Layer); foreach (var o in S.SelectedObjects) o.Layer = bottom - 1; }); break;
        }
    }

    private void SelectGroup()
    {
        var g = S.Primary?.Group ?? S.SelectedObjects.FirstOrDefault()?.Group;
        if (g == null) return;
        S.Select(S.Doc.Objects.Where(o => o.Group == g).Select(o => o.Id));
    }

    private void Copy()
    {
        var sel = S.SelectedObjects.ToList();
        if (sel.Count == 0) return;
        clipboard.Clear();
        clipboard.AddRange(sel.Select(o => o.Clone(o.Id)));
        clipboardBase = sel.Min(o => o.Start + o.Warn);
        Status($"Copied {sel.Count} object(s).");
    }

    private void Paste()
    {
        if (clipboard.Count == 0) return;
        Dictionary<string, string> map = null;
        S.Edit("Paste", () => map = Insert(clipboard, S.Playhead - clipboardBase));
        S.Select(map.Values);
    }

    public void DuplicateSelection()
    {
        var sel = S.SelectedObjects.ToList();
        if (sel.Count == 0) return;
        Dictionary<string, string> map = null;
        double shift = sel.Max(o => o.End) - sel.Min(o => o.Start);
        S.Edit("Duplicate", () => map = Insert(sel, S.SnapDivision > 0 ? Math.Ceiling(shift / S.GridMs) * S.GridMs : shift));
        S.Select(map.Values);
    }

    /// <summary>Duplicates objects in place (used by Ctrl+drag) and selects the copies.</summary>
    public Dictionary<string, string> DuplicateObjects(List<EditObject> objs, double shift)
    {
        var map = Insert(objs, shift);
        S.Select(map.Values);
        return map;
    }

    /// <summary>Inserts copies with new ids, keeping parent links inside the copied set.</summary>
    private Dictionary<string, string> Insert(IEnumerable<EditObject> source, double shift)
    {
        var list = source.ToList();
        var map = list.ToDictionary(o => o.Id, _ => S.Doc.NewId() + "_");
        // NewId checks existing objects only, so assign real ids one by one.
        map.Clear();
        var groupMap = new Dictionary<string, string>();
        var copies = new List<EditObject>();
        foreach (var o in list)
        {
            var id = S.Doc.NewId();
            var c = o.Clone(id);
            c.Start += shift;
            c.End += shift;
            if (c.Group != null)
            {
                if (!groupMap.TryGetValue(c.Group, out var ng)) groupMap[c.Group] = ng = Pattern.NewGroupId(S.Doc, new string(c.Group.TakeWhile(char.IsLetter).ToArray()));
                c.Group = ng;
            }
            map[o.Id] = id;
            S.Doc.Objects.Add(c);
            copies.Add(c);
        }
        foreach (var c in copies)
            if (c.Parent != null && map.TryGetValue(c.Parent, out var np)) c.Parent = np;
        return map;
    }

    public void DeleteSelection()
    {
        if (S.Selected.Count > 0)
        {
            var ids = S.Selected.ToHashSet();
            S.Edit("Delete", () =>
            {
                S.Doc.Objects.RemoveAll(o => ids.Contains(o.Id));
                foreach (var o in S.Doc.Objects) if (o.Parent != null && ids.Contains(o.Parent)) o.Parent = null;
            });
            S.ClearSelection();
        }
        else if (S.SelectedEvent >= 0 && S.SelectedEvent < S.Doc.Events.Count)
        {
            int i = S.SelectedEvent;
            S.Edit("Delete event", () => S.Doc.Events.RemoveAt(i));
            S.ClearSelection();
        }
        else if (S.SelectedCheckpoint > 0 && S.SelectedCheckpoint < S.Doc.Checkpoints.Count)
        {
            int i = S.SelectedCheckpoint;
            S.Edit("Delete checkpoint", () => S.Doc.Checkpoints.RemoveAt(i));
            S.ClearSelection();
        }
    }

    /// <summary>Mirrors the selection across the vertical (horizontal flip) or horizontal axis.</summary>
    public void Mirror(bool horizontal)
    {
        if (S.Selected.Count == 0) return;
        S.Edit(horizontal ? "Mirror horizontally" : "Mirror vertically", () =>
        {
            foreach (var o in S.SelectedObjects)
            {
                foreach (var k in o.Track(horizontal ? "x" : "y")) k.V = -k.V;
                foreach (var k in o.Track("rot")) k.V = -k.V;
                if (horizontal) o.PivotX = -o.PivotX; else o.PivotY = -o.PivotY;
            }
        });
    }

    // ---------------- files ----------------

    private bool ConfirmDiscard(Action then)
    {
        if (!S.Dirty) { then(); return true; }
        var dlg = new ConfirmationDialog { Title = "Unsaved changes", DialogText = "The level has unsaved changes. Save them first?", OkButtonText = "Save" };
        dlg.AddButton("Don't save", true, "discard");
        dlg.Confirmed += () => { if (Save()) then(); dlg.QueueFree(); };
        dlg.CustomAction += a => { if (a == "discard") { then(); } dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
        return false;
    }

    private void NewLevel() => ConfirmDiscard(() =>
    {
        var fd = new FileDialog
        {
            Title = "Choose the song for the new level",
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Filters = new[] { "*.ogg, *.mp3, *.wav ; Songs" },
        };
        fd.FileSelected += path => { fd.QueueFree(); CreateLevelFromSong(path); };
        fd.Canceled += () => fd.QueueFree();
        AddChild(fd);
        fd.PopupCentered(new Vector2I(900, 600));
    });

    /// <summary>Creates a level in an exact folder (used by scripted tests).</summary>
    public void CreateLevelAt(string songPath, string folder)
    {
        Directory.CreateDirectory(folder);
        var ext = Path.GetExtension(songPath).ToLowerInvariant();
        File.Copy(songPath, Path.Combine(folder, "song" + ext), true);
        var doc = new LevelDocument { FolderPath = folder, Title = Path.GetFileName(folder), Audio = "song" + ext };
        doc.Save();
        S.Open(doc);
    }

    public void TestInGameNow() => TestInGame();

    private void CreateLevelFromSong(string songPath)
    {
        var name = Path.GetFileNameWithoutExtension(songPath);
        var root = EditorSettings.LevelsRoot;
        var folder = Path.Combine(root, Sanitize(name));
        for (int i = 2; Directory.Exists(folder); i++) folder = Path.Combine(root, Sanitize(name) + " " + i);
        Directory.CreateDirectory(folder);
        var ext = Path.GetExtension(songPath).ToLowerInvariant();
        File.Copy(songPath, Path.Combine(folder, "song" + ext));
        var doc = new LevelDocument { FolderPath = folder, Title = name, Audio = "song" + ext, Author = EditorSettings.AuthorName };
        doc.Save();
        EditorSettings.AddRecent(folder);
        S.Open(doc);
        Status($"New level in {folder}. Set the BPM in Level… so the grid matches the song.");
        ShowLevelSettings();
    }

    private void OpenLevel() => ConfirmDiscard(() =>
    {
        var fd = new FileDialog
        {
            Title = "Open a level (choose its level.json)",
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Filters = new[] { "level.json ; Beatform level" },
            CurrentDir = EditorSettings.LevelsRoot,
        };
        fd.FileSelected += path => { fd.QueueFree(); OpenFolder(Path.GetDirectoryName(path)); };
        fd.Canceled += () => fd.QueueFree();
        AddChild(fd);
        fd.PopupCentered(new Vector2I(900, 600));
    });

    public void OpenFolder(string folder)
    {
        try
        {
            var doc = LevelDocument.Load(folder);
            EditorSettings.AddRecent(folder);
            S.Open(doc);
            Timeline.ShowRange(0, Math.Max(20000, doc.ContentEnd));
            Status($"Opened {folder}");
            if (autotest == null) OfferAutosave(folder);
        }
        catch (Exception e)
        {
            Status("Could not open the level: " + e.Message, true);
            S.Open(new LevelDocument());
        }
    }

    public bool Save()
    {
        if (S.Doc.FolderPath == null)
        {
            var folder = Path.Combine(EditorSettings.LevelsRoot, Sanitize(S.Doc.Title));
            S.Doc.FolderPath = folder;
        }
        try
        {
            bool newFolder = Scripts.ScriptsService.I.LevelFolder != S.Doc.FolderPath;
            S.Doc.Save();
            Scripts.ScriptsService.I.NoteSaved(File.ReadAllText(Path.Combine(S.Doc.FolderPath, "level.json")));
            if (newFolder) Scripts.ScriptsService.I.Bind(S.Doc.FolderPath);
            S.MarkSaved();
            try { File.Delete(Path.Combine(S.Doc.FolderPath, AutosaveName)); } catch { }
            EditorSettings.AddRecent(S.Doc.FolderPath);
            Status("Saved " + Path.Combine(S.Doc.FolderPath, "level.json"));
            return true;
        }
        catch (Exception e)
        {
            Status("Could not save: " + e.Message, true);
            return false;
        }
    }

    /// <summary>level.json was changed by something else (a text editor, an AI): reload it if that loses nothing.</summary>
    private void OnLevelFileChanged()
    {
        if (S.Doc.FolderPath == null) return;
        if (S.Dirty)
        {
            Status("level.json changed on disk, but you have unsaved edits: save to keep yours, or reopen the level to load the new file.", true);
            return;
        }
        try
        {
            double ph = S.Playhead;
            var sel = S.Selected.ToArray();
            var doc = LevelDocument.Load(S.Doc.FolderPath);
            S.Open(doc);
            S.Playhead = ph;
            S.Select(sel.Where(id => doc.Find(id) != null));
            PlayheadMoved();
            Status("Reloaded level.json (it changed on disk).");
        }
        catch (Exception e) { Status("level.json changed on disk but could not be read: " + e.Message, true); }
    }

    private void ReloadSong()
    {
        Song.Load(S.Doc.AudioPath, S.Doc.OffsetMs);
        if (Song.Error != null) Status(Song.Error, true);
    }

    public void SongSettingsChanged()
    {
        Song.SetOffset(S.Doc.OffsetMs);
        if (Song.LoadedPath != S.Doc.AudioPath) ReloadSong();
    }

    private void ExportZip()
    {
        if (!Save()) return;
        var fd = new FileDialog
        {
            Title = "Export level as .zip",
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Filters = new[] { "*.zip ; Zip archive" },
            CurrentFile = Sanitize(S.Doc.Title) + ".zip",
        };
        fd.FileSelected += path =>
        {
            fd.QueueFree();
            try
            {
                if (File.Exists(path)) File.Delete(path);
                using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
                foreach (var f in Directory.GetFiles(S.Doc.FolderPath))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is ".bak" or ".tmp") continue;
                    System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, f, Path.GetFileName(f));
                }
                Status("Exported " + path);
            }
            catch (Exception e) { Status("Export failed: " + e.Message, true); }
        };
        fd.Canceled += () => fd.QueueFree();
        AddChild(fd);
        fd.PopupCentered(new Vector2I(900, 600));
    }

    private void InstallToGame()
    {
        if (!Save()) return;
        var game = GameBridge.FindGame();
        if (game == null) { Status("Could not find Just Shapes & Beats. Set its folder in Level… → Editor.", true); return; }
        var dest = Path.Combine(game, "CustomLevels", Sanitize(S.Doc.Title));
        try
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.GetFiles(S.Doc.FolderPath))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".bak" or ".tmp") continue;
                File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            }
            Status("Copied to " + dest + ". It shows up in the game's custom level list (F1 on the main menu).");
        }
        catch (Exception e) { Status("Copy failed: " + e.Message, true); }
    }

    private void OpenLevelFolder()
    {
        if (S.Doc.FolderPath != null) OS.ShellOpen(S.Doc.FolderPath);
    }

    private void TestInGame()
    {
        if (!Save()) return;
        var result = GameBridge.RequestPlay(S.Doc.FolderPath, S.Playhead);
        Status(result.message, !result.ok);
    }

    public void ShowLevelSettings() => LevelSettingsDialog.Show(this);

    public static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        s = s.Trim();
        return string.IsNullOrEmpty(s) ? "Untitled" : s;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            if (autotest != null) { GetTree().Quit(); return; }
            ConfirmDiscard(() => GetTree().Quit());
        }
    }
}
