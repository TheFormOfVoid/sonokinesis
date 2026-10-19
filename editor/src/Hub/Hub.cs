using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Sonokinesis.Editor.UI;

namespace Sonokinesis.Editor.Hub;

/// <summary>A page of the app (Play, Levels, Create, Settings).</summary>
public abstract partial class HubPage : ScrollContainer
{
    protected VBoxContainer Body { get; private set; }

    protected HubPage()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + side, 44);
        margin.AddThemeConstantOverride("margin_top", 36);
        margin.AddThemeConstantOverride("margin_bottom", 36);
        AddChild(margin);
        Body = HubUi.Column(18);
        Body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        margin.AddChild(Body);
    }

    /// <summary>Called when the page is shown and every couple of seconds while it is.</summary>
    public virtual void Refresh() { }
}

/// <summary>
/// The Sonokinesis app: a home for playing (install into the game, start it, manage levels) and the
/// door to the editor, which runs in the same window. Also keeps the app up to date.
/// </summary>
public partial class Hub : Control
{
    public static Hub I { get; private set; }

    private readonly ConcurrentQueue<Action> mainThread = new();
    private readonly Dictionary<string, HubPage> pages = new();
    private readonly Dictionary<string, Button> nav = new();
    private string current;
    private Control shell, toastBox;
    private App editor;
    private double refreshIn, bridgeCheckIn;

    private PanelContainer updateBanner;
    private Label updateText;
    private ProgressBar updateProgress;
    private Button updateButton, sidebarUpdate;
    public ReleaseInfo NewRelease { get; private set; }
    public string UpdateCheckResult { get; private set; } = "";
    public event Action UpdateStateChanged;

    public bool EditorOpen => editor != null && editor.Visible;

    public override void _Ready()
    {
        I = this;
        EditorSettings.Load();
        Theme = HubUi.BuildTheme();
        GetTree().AutoAcceptQuit = false;
        var win = GetWindow();
        win.Title = "Sonokinesis";
        win.MinSize = new Vector2I(980, 640);
        win.FilesDropped += OnFilesDropped;

        var bg = new ColorRect { Color = UiTheme.Ink };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        shell = new HBoxContainer();
        shell.SetAnchorsPreset(LayoutPreset.FullRect);
        shell.AddThemeConstantOverride("separation", 0);
        AddChild(shell);
        shell.AddChild(BuildSidebar());

        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 0);
        shell.AddChild(content);
        content.AddChild(BuildUpdateBanner());
        var stack = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddChild(stack);

        AddPage(stack, "play", new PlayPage());
        AddPage(stack, "levels", new LevelsPage());
        AddPage(stack, "create", new CreatePage());
        AddPage(stack, "settings", new SettingsPage());

        toastBox = HubUi.Column(8);
        toastBox.SetAnchorsPreset(LayoutPreset.BottomRight);
        toastBox.GrowHorizontal = GrowDirection.Begin;
        toastBox.GrowVertical = GrowDirection.Begin;
        toastBox.Position = new Vector2(-24, -24);
        toastBox.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(toastBox);

        ShowPage("play");
        CleanUpOldUpdates();

        // The command line opens the editor directly (the AI tool and scripted tests use this).
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--open") || args.Contains("--autotest")) OpenEditor();
        else if (EditorSettings.CheckUpdates) CheckForUpdates(quiet: true);
        DevShots(args);
        int st = Array.IndexOf(args, "--selftest");
        if (st >= 0 && st + 1 < args.Length) SelfTest.Run(args[st + 1]);
    }

    /// <summary>
    /// For checking the pages without clicking: --shot DIR saves a picture of each page (or only the one
    /// given with --page), then quits.
    /// </summary>
    private async void DevShots(string[] args)
    {
        int at = Array.IndexOf(args, "--shot");
        if (at < 0 || at + 1 >= args.Length) return;
        var dir = args[at + 1];
        System.IO.Directory.CreateDirectory(dir);
        int p = Array.IndexOf(args, "--page");
        var ids = p >= 0 && p + 1 < args.Length ? new[] { args[p + 1] } : pages.Keys.ToArray();
        foreach (var id in ids)
        {
            if (id == "editor") OpenEditor(); else ShowPage(id);
            await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, id + ".png"));
        }
        GetTree().Quit();
    }

    private void AddPage(Control stack, string id, HubPage page)
    {
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        page.Visible = false;
        stack.AddChild(page);
        pages[id] = page;
    }

    // ---------------- sidebar ----------------

    private Control BuildSidebar()
    {
        var side = new PanelContainer { CustomMinimumSize = new Vector2(236, 0) };
        var sb = HubUi.Box(UiTheme.Panel, 0, null, 0, 26, 16);
        sb.BorderColor = UiTheme.Line;
        sb.BorderWidthRight = 1;
        side.AddThemeStyleboxOverride("panel", sb);
        var col = HubUi.Column(6);
        side.AddChild(col);

        var logo = HubUi.Row(10, new Logo(), HubUi.Column(0));
        var words = (VBoxContainer)logo.GetChild(1);
        words.AddChild(HubUi.Text("SONOKINESIS", 19, UiTheme.Text, HubUi.Bold));
        words.AddChild(HubUi.Text("custom levels for JSAB", 12, UiTheme.Dim));
        col.AddChild(logo);
        col.AddChild(HubUi.Gap(22));

        foreach (var (id, label) in new[] { ("play", "▶   Play"), ("levels", "▦   Levels"), ("create", "✎   Create"), ("settings", "⚙   Settings") })
        {
            var b = new Button { Text = label, Alignment = HorizontalAlignment.Left, ToggleMode = true, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, 46) };
            b.AddThemeFontOverride("font", HubUi.Semi);
            b.AddThemeFontSizeOverride("font_size", 16);
            b.AddThemeStyleboxOverride("normal", HubUi.Box(new Color(0, 0, 0, 0), 9, null, 0, 8, 16));
            b.AddThemeStyleboxOverride("hover", HubUi.Box(UiTheme.Panel2, 9, null, 0, 8, 16));
            var on = HubUi.Box(new Color(UiTheme.Pink, 0.14f), 9, null, 0, 8, 16);
            on.BorderColor = UiTheme.Pink;
            on.BorderWidthLeft = 3;
            b.AddThemeStyleboxOverride("pressed", on);
            b.AddThemeStyleboxOverride("hover_pressed", on);
            b.AddThemeColorOverride("font_color", UiTheme.Dim);
            b.AddThemeColorOverride("font_hover_color", UiTheme.Text);
            b.AddThemeColorOverride("font_pressed_color", UiTheme.Text);
            b.AddThemeColorOverride("font_hover_pressed_color", UiTheme.Text);
            b.Pressed += () => ShowPage(id);
            col.AddChild(b);
            nav[id] = b;
        }

        col.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        sidebarUpdate = HubUi.Primary("Update available", () => updateButton.EmitSignal(BaseButton.SignalName.Pressed), UiTheme.Gold, 14);
        sidebarUpdate.Visible = false;
        col.AddChild(sidebarUpdate);
        col.AddChild(HubUi.Text("Version " + Core.BuildInfo.Version, 12, UiTheme.Dim));
        return side;
    }

    public void ShowPage(string id)
    {
        current = id;
        foreach (var (k, p) in pages) p.Visible = k == id;
        foreach (var (k, b) in nav) b.SetPressedNoSignal(k == id);
        pages[id].ScrollVertical = 0;
        pages[id].Refresh();
        refreshIn = 2;
    }

    public T Page<T>() where T : HubPage => pages.Values.OfType<T>().First();

    // ---------------- background work ----------------

    /// <summary>Runs work off the main thread, then ok or fail on it.</summary>
    public void Run<T>(Func<T> work, Action<T> ok, Action<Exception> fail = null)
    {
        Task.Run(() =>
        {
            try { var r = work(); mainThread.Enqueue(() => ok?.Invoke(r)); }
            catch (Exception e) { mainThread.Enqueue(() => (fail ?? (x => Toast(x.Message, false)))(Unwrap(e))); }
        });
    }

    public void RunAsync(Func<Task> work, Action ok, Action<Exception> fail = null)
    {
        Task.Run(async () =>
        {
            try { await work(); mainThread.Enqueue(() => ok?.Invoke()); }
            catch (Exception e) { mainThread.Enqueue(() => (fail ?? (x => Toast(x.Message, false)))(Unwrap(e))); }
        });
    }

    public void Post(Action a) => mainThread.Enqueue(a);

    private static Exception Unwrap(Exception e) => e is AggregateException a && a.InnerException != null ? a.InnerException : e;

    public override void _Process(double delta)
    {
        while (mainThread.TryDequeue(out var a))
        {
            try { a(); } catch (Exception e) { GD.PrintErr(e); }
        }
        if (!EditorOpen && current != null && (refreshIn -= delta) <= 0)
        {
            refreshIn = 2;
            pages[current].Refresh();
        }
        // The AI tool talks to the editor; a command arriving while another page shows opens the editor.
        if (!EditorOpen && (bridgeCheckIn -= delta) <= 0)
        {
            bridgeCheckIn = 0.3;
            if (System.IO.File.Exists(System.IO.Path.Combine(EditorBridge.Dir, "command.json"))) OpenEditor();
        }
    }

    // ---------------- messages and dialogs ----------------

    /// <summary>A short message in the corner that fades away.</summary>
    public void Toast(string text, bool ok = true)
    {
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(360, 0) };
        var sb = HubUi.Box(UiTheme.Panel2, 10, ok ? UiTheme.Cyan.Darkened(0.3f) : UiTheme.Bad, 1, 12, 16);
        sb.ShadowColor = new Color(0, 0, 0, 0.4f);
        sb.ShadowSize = 10;
        panel.AddThemeStyleboxOverride("panel", sb);
        var label = HubUi.Text(text, 15, UiTheme.Text, null, true);
        label.CustomMinimumSize = new Vector2(330, 0);
        label.MouseFilter = MouseFilterEnum.Ignore;
        panel.AddChild(label);
        toastBox.AddChild(panel);
        while (toastBox.GetChildCount() > 4) toastBox.GetChild(0).QueueFree();
        var tw = panel.CreateTween();
        panel.Modulate = new Color(1, 1, 1, 0);
        tw.TweenProperty(panel, "modulate:a", 1.0, 0.15);
        tw.TweenInterval(Math.Clamp(text.Length * 0.06, 3.5, 9));
        tw.TweenProperty(panel, "modulate:a", 0.0, 0.4);
        tw.TweenCallback(Callable.From(panel.QueueFree));
    }

    public void Confirm(string title, string text, string okText, Action then)
    {
        var dlg = new ConfirmationDialog { Title = title, DialogText = text, OkButtonText = okText, DialogAutowrap = true, MinSize = new Vector2I(460, 0) };
        dlg.Confirmed += () => { dlg.QueueFree(); then(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered(new Vector2I(480, 0));
    }

    public void PickFolder(string title, Action<string> then)
    {
        var fd = new FileDialog { Title = title, FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true };
        fd.DirSelected += d => { fd.QueueFree(); then(d); };
        fd.Canceled += () => fd.QueueFree();
        AddChild(fd);
        fd.PopupCentered(new Vector2I(900, 600));
    }

    public void PickFiles(string title, string[] filters, Action<string[]> then)
    {
        var fd = new FileDialog { Title = title, FileMode = FileDialog.FileModeEnum.OpenFiles, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true, Filters = filters };
        fd.FilesSelected += f => { fd.QueueFree(); then(f); };
        fd.Canceled += () => fd.QueueFree();
        AddChild(fd);
        fd.PopupCentered(new Vector2I(900, 600));
    }

    private void OnFilesDropped(string[] files)
    {
        if (EditorOpen) return;
        ShowPage("levels");
        Page<LevelsPage>().AddLevels(files);
    }

    // ---------------- editor ----------------

    /// <summary>Shows the editor in this window, optionally opening a level folder or starting a new level.</summary>
    public void OpenEditor(string folder = null, bool newLevel = false)
    {
        if (editor == null)
        {
            editor = GD.Load<PackedScene>("res://Main.tscn").Instantiate<App>();
            AddChild(editor);
            MoveChild(editor, toastBox.GetIndex());
        }
        editor.Visible = true;
        editor.ProcessMode = ProcessModeEnum.Inherit;
        shell.Visible = false;
        GetWindow().MinSize = new Vector2I(1100, 640);
        editor.ShownFromHub();
        if (folder != null) editor.OpenFromHub(folder);
        else if (newLevel) editor.NewLevelFromHub();
    }

    /// <summary>Back from the editor to the app's pages.</summary>
    public void CloseEditor()
    {
        if (editor == null) return;
        editor.StopPlayback();
        editor.Visible = false;
        editor.ProcessMode = ProcessModeEnum.Disabled;
        shell.Visible = true;
        GetWindow().Title = "Sonokinesis";
        GetWindow().MinSize = new Vector2I(980, 640);
        ShowPage(current ?? "create");
    }

    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest) return;
        if (editor == null || editor.IsAutotest) { GetTree().Quit(); return; }
        if (!EditorOpen && editor.HasUnsavedWork) OpenEditor();
        editor.ConfirmDiscardThen(() => GetTree().Quit());
    }

    // ---------------- updates ----------------

    private Control BuildUpdateBanner()
    {
        updateBanner = new PanelContainer { Visible = false };
        var sb = HubUi.Box(new Color("2a2410"), 0, null, 0, 12, 44);
        sb.BorderColor = UiTheme.Gold.Darkened(0.3f);
        sb.BorderWidthBottom = 1;
        updateBanner.AddThemeStyleboxOverride("panel", sb);
        updateText = HubUi.Text("", 15, UiTheme.Gold, HubUi.Semi);
        updateText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        updateProgress = new ProgressBar { CustomMinimumSize = new Vector2(220, 18), Visible = false, ShowPercentage = false, MaxValue = 1, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var notes = HubUi.Link("What's new", () => OS.ShellOpen(NewRelease?.PageUrl ?? Updater.ReleasesPage));
        updateButton = HubUi.Primary("Update now", StartUpdate, UiTheme.Gold, 14);
        updateBanner.AddChild(HubUi.Row(14, updateText, updateProgress, notes, updateButton));
        return updateBanner;
    }

    public void CheckForUpdates(bool quiet)
    {
        UpdateCheckResult = "Checking…";
        UpdateStateChanged?.Invoke();
        Task.Run(async () =>
        {
            try
            {
                var r = await Updater.Latest();
                Post(() =>
                {
                    if (Updater.IsNewer(r.Version, Core.BuildInfo.Version))
                    {
                        NewRelease = r;
                        UpdateCheckResult = $"Sonokinesis {r.Version} is available.";
                        updateText.Text = $"Sonokinesis {r.Version} is available (you have {Core.BuildInfo.Version}).";
                        updateBanner.Visible = true;
                        sidebarUpdate.Visible = true;
                    }
                    else
                    {
                        UpdateCheckResult = $"You have the latest version ({Core.BuildInfo.Version}).";
                        if (!quiet) Toast(UpdateCheckResult);
                    }
                    UpdateStateChanged?.Invoke();
                });
            }
            catch (Exception e)
            {
                Post(() =>
                {
                    UpdateCheckResult = "Could not check for updates: " + Unwrap(e).Message + ".";
                    if (!quiet) Toast(UpdateCheckResult, false);
                    UpdateStateChanged?.Invoke();
                });
            }
        });
    }

    private void StartUpdate()
    {
        if (NewRelease == null) return;
        if (!Updater.InstalledBySetup)
        {
            // A copy that was not installed by the setup (unzipped, or run from source) is updated by hand.
            OS.ShellOpen(NewRelease.PageUrl);
            Toast("Download the new version from the page that just opened.");
            return;
        }
        void Go()
        {
            updateButton.Disabled = true;
            sidebarUpdate.Disabled = true;
            updateProgress.Visible = true;
            updateText.Text = $"Downloading Sonokinesis {NewRelease.Version}…";
            RunAsync(() => Updater.DownloadAndRunSetup(NewRelease, p => Post(() => updateProgress.Value = p)),
                () => { updateText.Text = "Installing the update…"; GetTree().Quit(); },
                e => { updateButton.Disabled = false; sidebarUpdate.Disabled = false; updateProgress.Visible = false; updateText.Text = "The update failed: " + e.Message; });
        }
        if (editor != null && editor.HasUnsavedWork) { OpenEditor(); editor.ConfirmDiscardThen(Go); }
        else Go();
    }

    private static void CleanUpOldUpdates()
    {
        try { var d = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Sonokinesis-update"); if (System.IO.Directory.Exists(d)) System.IO.Directory.Delete(d, true); } catch { }
    }
}

/// <summary>The app's mark: a pink triangle and a cyan diamond in a ring.</summary>
public partial class Logo : Control
{
    public Logo() { CustomMinimumSize = new Vector2(40, 40); }

    public override void _Draw()
    {
        var c = Size / 2;
        DrawArc(c, 18, 0, Mathf.Tau, 48, new Color(UiTheme.Cyan, 0.55f), 2.5f, true);
        DrawColoredPolygon(new[] { c + new Vector2(0, -13), c + new Vector2(12, 9), c + new Vector2(-12, 9) }, UiTheme.Pink);
        DrawColoredPolygon(new[] { c + new Vector2(0, 2), c + new Vector2(6, 8), c + new Vector2(0, 14), c + new Vector2(-6, 8) }, UiTheme.Cyan);
    }
}
