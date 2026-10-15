using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sonokinesis.Core;
using Sonokinesis.Editor.Scripts;
using Godot;

namespace Sonokinesis.Editor.UI;

/// <summary>
/// The code editor for scripts: JavaScript with highlighting, completion of the scripting API, a live
/// syntax check while typing, and a console with the preview's log() lines and errors. Library scripts
/// open read-only; "Copy into level" makes an editable copy in the level's scripts folder.
/// </summary>
public partial class ScriptEditorWindow : Window
{
    private static ScriptEditorWindow instance;
    private static EditorState S => EditorState.I;

    private OptionButton picker;
    private readonly List<string> pickerNames = new();
    private CodeEdit code;
    private RichTextLabel console;
    private Label info, check;
    private Button saveButton, revertButton, copyButton, placeButton;
    private CheckBox onlyThis;

    private string current;
    private string loadedText = "";
    private bool dirty;
    private double checkAt = -1, consoleAt;
    private string consoleSignature = "";
    private readonly List<int> markedLines = new();
    private string liveError;
    private int liveErrorLine;

    /// <summary>The open script editor, if any.</summary>
    public static ScriptEditorWindow Current => instance != null && IsInstanceValid(instance) && instance.Visible ? instance : null;

    /// <summary>Replaces the text as if typed (tests, tools).</summary>
    public void SetText(string text)
    {
        code.Text = text;
        OnTextChanged();
    }

    public void SaveNow() => Save();

    /// <summary>Unsaved script edits in the open code editor.</summary>
    public static bool HasUnsaved => instance != null && IsInstanceValid(instance) && instance.dirty;

    /// <summary>Before the editor closes: asks about unsaved script edits, then runs <paramref name="then"/>.</summary>
    public static void ConfirmQuit(Action then)
    {
        if (!HasUnsaved) { then(); return; }
        instance.Show();
        instance.ConfirmUnsaved(then);
    }

    /// <summary>Opens the editor on a script (null: keep the current one, or the level's first script).</summary>
    public static void Open(string name)
    {
        if (instance == null || !IsInstanceValid(instance))
        {
            instance = new ScriptEditorWindow();
            App.Instance.AddChild(instance);
        }
        instance.Show();
        instance.GrabFocus();
        if (name != null && !string.Equals(name, instance.current, StringComparison.OrdinalIgnoreCase)) instance.ConfirmUnsaved(() => instance.Load(name));
        else if (instance.current == null)
        {
            var first = ScriptsService.I.All.FirstOrDefault(t => ScriptsService.I.IsInLevel(t.Name)) ?? ScriptsService.I.All.FirstOrDefault();
            if (first != null) instance.Load(first.Name);
        }
    }

    /// <summary>Asks for a name and a kind, creates the script in the level and opens it.</summary>
    public static void ShowNew(Node parent)
    {
        if (S.Doc.FolderPath == null) { App.Instance.Status("Save the level first: scripts are kept in its folder.", true); return; }
        var dlg = new ConfirmationDialog { Title = "New script", OkButtonText = "Create" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(new Label { Text = "Name (letters, digits, _ and -):" });
        var name = new LineEdit { PlaceholderText = "my_attack" };
        box.AddChild(name);
        box.AddChild(new Label { Text = "Kind:" });
        var kind = new OptionButton();
        foreach (var l in ScriptTemplates.KindLabels) kind.AddItem(l);
        box.AddChild(kind);
        box.AddChild(UiTheme.Small("It starts from a small working example. Attacks and effects run live in the game; patterns build ordinary objects when placed."));
        dlg.AddChild(box);
        dlg.RegisterTextEnter(name);
        dlg.Confirmed += () =>
        {
            string n = name.Text.Trim();
            var path = ScriptsService.I.Create(n, ScriptTemplates.Kinds[kind.Selected], out var err);
            if (path == null) { App.Instance.Status("Could not create the script: " + err, true); return; }
            App.Instance.Status($"Created scripts/{n}.js");
            Open(n);
        };
        dlg.VisibilityChanged += () => { if (!dlg.Visible) dlg.QueueFree(); };
        parent.AddChild(dlg);
        UiTheme.PopupFitted(dlg);
        name.GrabFocus();
    }

    public ScriptEditorWindow()
    {
        // A window of its own, so it can sit next to the stage or on another screen.
        Visible = false;
        ForceNative = true;
    }

    public override void _Ready()
    {
        Title = "Scripts";
        Size = new Vector2I(1040, 760);
        MinSize = new Vector2I(640, 420);
        Transient = false;
        Exclusive = false;
        WrapControls = false;
        CloseRequested += () => ConfirmUnsaved(Hide);
        Theme = App.Instance.Theme;

        var bg = new PanelContainer();
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        bg.AddChild(root);

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 6);
        picker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, TooltipText = "The level's scripts, then the built-in library." };
        picker.ItemSelected += i =>
        {
            var target = pickerNames[(int)i];
            ConfirmUnsaved(() => Load(target), () => SelectInPicker(current));
        };
        bar.AddChild(picker);
        bar.AddChild(Btn("+ New", () => ShowNew(this), "Create a new script in this level."));
        saveButton = Btn("Save", Save, "Save the script (Ctrl+S). The preview and the game pick it up right away.");
        saveButton.Shortcut = new Shortcut { Events = new Godot.Collections.Array { new InputEventKey { Keycode = Key.S, CtrlPressed = true } } };
        bar.AddChild(saveButton);
        revertButton = Btn("Revert", () => { if (current != null) Load(current); }, "Throw away unsaved changes.");
        bar.AddChild(revertButton);
        copyButton = Btn("Copy into level", CopyIntoLevel, "Library scripts are shared by every level and open read-only. This copies it into the level's scripts folder so you can change it.");
        bar.AddChild(copyButton);
        placeButton = Btn("Place", Place, "Add this script to the level at the playhead.");
        bar.AddChild(placeButton);
        bar.AddChild(Btn("Guide", OpenGuide, "Open the scripting guide (every function you can call)."));
        root.AddChild(bar);

        info = UiTheme.Small("");
        root.AddChild(info);

        var split = new VSplitContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddChild(split);
        code = new CodeEdit
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            GuttersDrawLineNumbers = true,
            GuttersDrawFoldGutter = true,
            LineFolding = true,
            AutoBraceCompletionEnabled = true,
            AutoBraceCompletionHighlightMatching = true,
            IndentUseSpaces = true,
            IndentSize = 2,
            IndentAutomatic = true,
            CodeCompletionEnabled = true,
            CodeCompletionPrefixes = new Godot.Collections.Array<string> { "." },
            HighlightCurrentLine = true,
            HighlightAllOccurrences = true,
            ScrollPastEndOfFile = true,
            MinimapDraw = true,
            MinimapWidth = 70,
            CaretBlink = true,
            SyntaxHighlighter = BuildHighlighter(),
            CustomMinimumSize = new Vector2(0, 200),
        };
        code.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "Courier New", "monospace" } });
        code.AddThemeFontSizeOverride("font_size", 15);
        code.AddThemeColorOverride("background_color", new Color("0b0d12"));
        code.AddThemeColorOverride("current_line_color", new Color(1, 1, 1, 0.04f));
        code.AddThemeColorOverride("line_number_color", UiTheme.Dim.Darkened(0.3f));
        code.AddThemeColorOverride("caret_color", UiTheme.Cyan);
        code.AddThemeColorOverride("selection_color", new Color(UiTheme.Cyan, 0.25f));
        code.AddThemeColorOverride("word_highlighted_color", new Color(UiTheme.Cyan, 0.12f));
        code.AddThemeColorOverride("brace_mismatch_color", UiTheme.Bad);
        code.ClearCommentDelimiters();
        code.ClearStringDelimiters();
        code.AddCommentDelimiter("//", "", true);
        code.AddCommentDelimiter("/*", "*/");
        code.AddStringDelimiter("'", "'");
        code.AddStringDelimiter("\"", "\"");
        code.AddStringDelimiter("`", "`");
        code.TextChanged += OnTextChanged;
        code.CodeCompletionRequested += OfferCompletion;
        code.GuiInput += OnCodeInput;
        split.AddChild(code);

        var bottom = new VBoxContainer { CustomMinimumSize = new Vector2(0, 150) };
        var cbar = new HBoxContainer();
        check = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
        check.AddThemeFontSizeOverride("font_size", 13);
        cbar.AddChild(check);
        onlyThis = new CheckBox { Text = "only this script", ButtonPressed = true, TooltipText = "Show log() lines and errors of this script only." };
        onlyThis.Toggled += _ => consoleSignature = "";
        cbar.AddChild(onlyThis);
        cbar.AddChild(Btn("Clear", () => { S.Evaluator?.Scripts?.Logs.Clear(); consoleSignature = ""; }, "Clear the log."));
        bottom.AddChild(cbar);
        console = new RichTextLabel { SizeFlagsVertical = Control.SizeFlags.ExpandFill, ScrollFollowing = true, SelectionEnabled = true, BbcodeEnabled = false };
        console.AddThemeFontOverride("normal_font", new SystemFont { FontNames = new[] { "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "monospace" } });
        console.AddThemeFontSizeOverride("normal_font_size", 13);
        console.AddThemeColorOverride("default_color", UiTheme.Dim.Lightened(0.2f));
        var cbg = new StyleBoxFlat { BgColor = new Color("0b0d12"), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6 };
        console.AddThemeStyleboxOverride("normal", cbg);
        bottom.AddChild(console);
        split.AddChild(bottom);
        split.SplitOffsets = new[] { 420 };

        S.ScriptsReloaded += OnScriptsReloaded;
        RefreshPicker();
        UpdateButtons();
    }

    public override void _ExitTree()
    {
        S.ScriptsReloaded -= OnScriptsReloaded;
        if (instance == this) instance = null;
    }

    private static Button Btn(string text, Action onPress, string tip)
    {
        var b = new Button { Text = text, TooltipText = tip, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += onPress;
        return b;
    }

    // ---------------- files ----------------

    private bool Editable => current != null && ScriptsService.I.IsInLevel(current);

    private void Load(string name)
    {
        var path = ScriptsService.I.PathOf(name);
        string text;
        try { text = path != null ? File.ReadAllText(path) : null; }
        catch (Exception e) { App.Instance.Status($"Could not read {name}.js: {e.Message}", true); return; }
        if (text == null) { App.Instance.Status($"No script named '{name}'.", true); return; }
        current = name;
        loadedText = text;
        code.Text = text;
        code.ClearUndoHistory();
        code.SetCaretLine(0);
        code.Editable = Editable;
        dirty = false;
        liveError = null;
        checkAt = Time.GetTicksMsec() / 1000.0;
        consoleSignature = "";
        SelectInPicker(name);
        UpdateButtons();
    }

    private void Save()
    {
        if (current == null || !Editable || !dirty) return;
        try
        {
            File.WriteAllText(Path.Combine(ScriptsService.I.LevelScriptsDir, current + ".js"), code.Text);
            loadedText = code.Text;
            dirty = false;
            checkAt = Time.GetTicksMsec() / 1000.0;
            ScriptsService.I.ReloadLevelScripts();
            var t = ScriptsService.I.FindInLevel(current);
            App.Instance.Status(t?.CompileError != null ? "Saved, but the script has an error: " + t.CompileError : $"Saved scripts/{current}.js");
        }
        catch (Exception e) { App.Instance.Status("Could not save the script: " + e.Message, true); }
        UpdateButtons();
    }

    private void CopyIntoLevel()
    {
        if (current == null) return;
        if (!ScriptsService.I.EnsureInLevel(current, out var err)) { App.Instance.Status(err, true); return; }
        Load(current);
        App.Instance.Status($"Copied {current}.js into the level's scripts folder: edit away.");
    }

    private void Place()
    {
        if (current == null) return;
        if (dirty) Save();
        App.Instance.Palette.Place(current);
    }

    private static void OpenGuide()
    {
        foreach (var dir in new[] { Path.GetDirectoryName(OS.GetExecutablePath()), ProjectSettings.GlobalizePath("res://") })
        {
            if (string.IsNullOrEmpty(dir)) continue;
            foreach (var rel in new[] { "docs/scripting.md", "../docs/scripting.md" })
            {
                var p = Path.GetFullPath(Path.Combine(dir, rel));
                if (File.Exists(p)) { OS.ShellOpen(p); return; }
            }
        }
        App.Instance.Status("The scripting guide (docs/scripting.md) was not found next to the editor.", true);
    }

    /// <summary>Runs <paramref name="then"/> once unsaved edits are saved or thrown away (or right away).</summary>
    private void ConfirmUnsaved(Action then, Action cancelled = null)
    {
        if (!dirty) { then(); return; }
        var dlg = new ConfirmationDialog { Title = "Unsaved script", DialogText = $"{current}.js has unsaved changes.", OkButtonText = "Save" };
        dlg.AddButton("Discard", true, "discard");
        bool handled = false;
        dlg.Confirmed += () => { handled = true; Save(); if (!dirty) then(); else cancelled?.Invoke(); };
        dlg.CustomAction += a => { if (a == "discard") { handled = true; dirty = false; dlg.Hide(); then(); } };
        // Hiding comes before Confirmed, so decide once both have happened.
        dlg.VisibilityChanged += () =>
        {
            if (dlg.Visible) return;
            Callable.From(() => { if (!handled) cancelled?.Invoke(); dlg.QueueFree(); }).CallDeferred();
        };
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void OnScriptsReloaded()
    {
        RefreshPicker();
        if (current == null) return;
        var path = ScriptsService.I.PathOf(current);
        if (path == null)
        {
            if (dirty) { info.Text = $"{current}.js no longer exists: your edits are only in this window."; return; }
            // Deleted, or another level was opened: nothing to show.
            current = null;
            loadedText = "";
            code.Text = "";
            code.Editable = false;
            liveError = null;
            consoleSignature = "";
            UpdateButtons();
            return;
        }
        string disk;
        try { disk = File.ReadAllText(path); } catch { return; }
        if (disk == loadedText) { UpdateButtons(); return; }
        if (!dirty)
        {
            // Changed outside the editor (a text editor, an AI): follow it, keeping the caret.
            int line = code.GetCaretLine(), col = code.GetCaretColumn();
            double scroll = code.ScrollVertical;
            loadedText = disk;
            code.Text = disk;
            code.SetCaretLine(Math.Min(line, code.GetLineCount() - 1));
            code.SetCaretColumn(col);
            code.ScrollVertical = scroll;
            code.Editable = Editable;
            dirty = false;
            checkAt = Time.GetTicksMsec() / 1000.0;
        }
        UpdateButtons(disk != loadedText ? "  ·  changed on disk; saving overwrites that" : "");
    }

    private void RefreshPicker()
    {
        if (picker == null) return;
        picker.Clear();
        pickerNames.Clear();
        foreach (var t in ScriptsService.I.All.OrderBy(t => ScriptsService.I.IsInLevel(t.Name) ? 0 : 1).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            bool mine = ScriptsService.I.IsInLevel(t.Name);
            string kind = ScriptsService.I.KindName(t);
            picker.AddItem($"{t.Name}.js   ·   {kind}{(mine ? "" : "   ·   library")}{(t.CompileError != null ? "   ⚠" : "")}");
            pickerNames.Add(t.Name);
        }
        SelectInPicker(current);
    }

    private void SelectInPicker(string name)
    {
        int i = pickerNames.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) picker.Select(i);
    }

    private void UpdateButtons(string extra = "")
    {
        bool has = current != null;
        saveButton.Disabled = !has || !Editable || !dirty;
        revertButton.Disabled = !has || !dirty;
        copyButton.Visible = has && !Editable;
        placeButton.Disabled = !has;
        Title = has ? $"{current}.js{(dirty ? " *" : "")} — Scripts" : "Scripts";
        if (!has) { info.Text = "No script open. Use + New, or pick one above."; return; }
        var path = ScriptsService.I.PathOf(current);
        info.Text = Editable
            ? $"{path}{extra}"
            : S.Doc.FolderPath == null
                ? "Library script (read-only). Save the level, then Copy into level to change it."
                : "Library script (read-only): Copy into level makes this level's own copy to edit.";
    }

    // ---------------- editing ----------------

    private void OnTextChanged()
    {
        bool was = dirty;
        dirty = code.Text != loadedText;
        if (dirty != was) UpdateButtons();
        checkAt = Time.GetTicksMsec() / 1000.0 + 0.5;
        // Offer completions while typing a name.
        string word = WordBeforeCaret();
        if (word.Length >= 2 && code.Editable) code.RequestCodeCompletion();
    }

    private string WordBeforeCaret()
    {
        var line = code.GetLine(code.GetCaretLine());
        int c = Math.Min(code.GetCaretColumn(), line.Length), s = c;
        while (s > 0 && (char.IsLetterOrDigit(line[s - 1]) || line[s - 1] == '_')) s--;
        return line[s..c];
    }

    private void OnCodeInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } k && k.CtrlPressed && (k.Keycode == Key.Slash || k.PhysicalKeycode == Key.Slash) && code.Editable)
        {
            ToggleComment();
            code.AcceptEvent();
        }
    }

    private void ToggleComment()
    {
        int from = code.GetCaretLine(), to = from;
        if (code.HasSelection()) { from = code.GetSelectionFromLine(); to = code.GetSelectionToLine(); if (code.GetSelectionToColumn() == 0 && to > from) to--; }
        bool allCommented = true;
        for (int i = from; i <= to; i++)
        {
            var l = code.GetLine(i).TrimStart();
            if (l.Length > 0 && !l.StartsWith("//")) allCommented = false;
        }
        code.BeginComplexOperation();
        for (int i = from; i <= to; i++)
        {
            var l = code.GetLine(i);
            int indent = l.Length - l.TrimStart().Length;
            if (allCommented)
            {
                var rest = l[indent..];
                if (rest.StartsWith("// ")) rest = rest[3..];
                else if (rest.StartsWith("//")) rest = rest[2..];
                code.SetLine(i, l[..indent] + rest);
            }
            else if (l.Trim().Length > 0) code.SetLine(i, l[..indent] + "// " + l[indent..]);
        }
        code.EndComplexOperation();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (checkAt >= 0 && now >= checkAt) { checkAt = -1; LiveCheck(); }
        if (now >= consoleAt) { consoleAt = now + 0.25; UpdateConsole(); }
    }

    /// <summary>Compiles the text as typed (without saving) to point at syntax errors early.</summary>
    private void LiveCheck()
    {
        if (current == null) return;
        liveError = null;
        liveErrorLine = 0;
        string summary;
        try
        {
            var lib = new ScriptLibrary(new Dictionary<string, string> { [current] = code.Text });
            var t = lib.Get(current);
            if (t?.CompileError != null) { liveError = t.CompileError.ToString(); liveErrorLine = t.CompileError.Line; summary = "✕ " + liveError; }
            else if (t == null) summary = "";
            else if (t.Kind == ScriptKind.Pattern)
            {
                var values = t.Params.ToDictionary(p => p.Key, p => p.Default);
                values["hit"] = 4000.0;
                values["x"] = 0.0;
                values["y"] = 0.0;
                int n = 0;
                var built = ScriptHost.BuildPattern(t, values, S.Doc.Bpm, () => "o" + (++n), null, out var err);
                if (err != null) { liveError = err.ToString(); liveErrorLine = err.Line; summary = "✕ " + liveError; }
                else summary = $"✓ pattern  ·  builds {built.Count} object{(built.Count == 1 ? "" : "s")} with the default settings";
            }
            else
            {
                string fns = string.Join(", ", new[] { t.StartFn != null ? "start" : null, t.HasUpdate ? "update" : null, t.DrawFn != null ? "draw" : null }.Where(x => x != null));
                summary = $"✓ {(t.Category is "Camera" or "Screen" or "Effects" ? "effect" : "attack")}  ·  {(fns.Length > 0 ? fns : "no start/update/draw yet")}  ·  {t.Params.Count} setting{(t.Params.Count == 1 ? "" : "s")}";
            }
        }
        catch (Exception e) { summary = "✕ " + e.Message; liveError = e.Message; }
        check.Text = summary + (dirty ? "   (unsaved)" : "");
        check.AddThemeColorOverride("font_color", liveError != null ? UiTheme.Bad : UiTheme.Good);
        MarkLines();
        consoleSignature = "";
    }

    private void MarkLines()
    {
        foreach (var l in markedLines) if (l < code.GetLineCount()) code.SetLineBackgroundColor(l, new Color(0, 0, 0, 0));
        markedLines.Clear();
        void Mark(int line1, Color c)
        {
            int l = line1 - 1;
            if (l < 0 || l >= code.GetLineCount()) return;
            code.SetLineBackgroundColor(l, c);
            markedLines.Add(l);
        }
        if (liveErrorLine > 0) Mark(liveErrorLine, new Color(UiTheme.Bad, 0.25f));
        var host = S.Evaluator?.Scripts;
        if (host != null && current != null && !dirty)
            foreach (var e in host.Errors)
                if (string.Equals(e.Script, current, StringComparison.OrdinalIgnoreCase)) Mark(e.Line, new Color(UiTheme.Pink, 0.2f));
    }

    private void UpdateConsole()
    {
        var host = S.Evaluator?.Scripts;
        var errors = host?.Errors ?? new List<ScriptError>();
        var logs = host?.Logs ?? new List<string>();
        bool only = onlyThis.ButtonPressed && current != null;
        string sig = $"{current}|{errors.Count}|{logs.Count}|{(logs.Count > 0 ? logs[^1] : "")}|{only}|{liveError}";
        if (sig == consoleSignature) return;
        consoleSignature = sig;
        console.Clear();
        if (liveError != null)
        {
            console.PushColor(UiTheme.Bad);
            console.AddText("✕ " + liveError + "\n");
            console.Pop();
        }
        foreach (var e in errors)
        {
            if (only && !string.Equals(e.Script, current, StringComparison.OrdinalIgnoreCase)) continue;
            console.PushColor(UiTheme.Pink);
            console.AddText("✕ " + e + "\n");
            console.Pop();
        }
        string prefix = "[" + current + " ";
        foreach (var l in logs.Skip(Math.Max(0, logs.Count - 300)))
            if (!only || l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) console.AddText(l + "\n");
        if (host == null && liveError == null)
            console.AddText("log(...) lines and errors appear here while the preview runs this script (place it in the level and scrub or play).\n");
        MarkLines();
    }

    // ---------------- highlighting and completion ----------------

    private static readonly string[] Keywords =
    {
        "var", "let", "const", "function", "return", "if", "else", "for", "while", "do", "break", "continue", "switch", "case", "default",
        "new", "this", "typeof", "instanceof", "in", "of", "true", "false", "null", "undefined", "try", "catch", "finally", "throw", "delete",
    };

    /// <summary>The scripting API: name, what to insert, a short signature shown in the list.</summary>
    private static readonly (string Name, string Insert, string Hint)[] Api =
    {
        ("meta", "meta = {\n  name: '',\n  description: '',\n  length: 8,\n  params: {},\n};", "the script's name, kind, length and settings"),
        ("start", "function start(p) {\n  \n}", "runs once when it spawns"),
        ("update", "function update(dt) {\n  \n}", "every 1/60 s; dt in ms"),
        ("draw", "function draw() {\n  \n}", "every frame: draw shapes here"),
        ("build", "function build(p) {\n  \n}", "patterns: add() objects"),
        ("rect", "rect(", "rect(x, y, w, h, opts)"), ("square", "square(", "square(x, y, size, opts)"),
        ("ellipse", "ellipse(", "ellipse(x, y, w, h, opts)"), ("circle", "circle(", "circle(x, y, size, opts)"),
        ("polygon", "polygon(", "polygon(x, y, size, sides, opts)"), ("triangle", "triangle(", "triangle(x, y, size, opts)"),
        ("hexagon", "hexagon(", "hexagon(x, y, size, opts)"), ("star", "star(", "star(x, y, size, points, inner, opts)"),
        ("ring", "ring(", "ring(x, y, size, thickness, opts)"), ("spikes", "spikes(", "spikes(x, y, size, count, inner, opts)"),
        ("text", "text(", "text(str, x, y, size, opts)"), ("line", "line(", "line(x1, y1, x2, y2, width, opts)"),
        ("shape", "shape(", "shape([[x, y], ...], opts)"), ("textWidth", "textWidth(", "textWidth(str, size, bold)"),
        ("players", "players", "the players: [{x, y, index, alive}]"), ("nearestPlayer", "nearestPlayer(", "nearestPlayer(x, y)"),
        ("hitPlayer", "hitPlayer(", "hitPlayer(player)"), ("movePlayer", "movePlayer(", "movePlayer(player, x, y)"),
        ("setPlayerSpeed", "setPlayerSpeed(", "setPlayerSpeed(speed, turn) this frame"),
        ("camera", "camera", "camera.move / rotate / zoom / kick / kickRotate / kickZoom / shake"),
        ("screen", "screen", "screen.flash / background / width / height / left / right / top / bottom"),
        ("time", "time", "song time (ms)"), ("dt", "dt", "step length (ms)"), ("bpm", "bpm", "the level's BPM"),
        ("beatMs", "beatMs", "one beat in ms"), ("beat", "beat(", "beat(n): n beats in ms"), ("beatAt", "beatAt(", "beatAt(t): beats at time t"),
        ("spawn", "spawn(", "spawn(name, params, x, y)"), ("kill", "kill()", "remove this instance"),
        ("random", "random(", "random(a, b)"), ("randomInt", "randomInt(", "randomInt(a, b)"), ("pick", "pick(", "pick(array)"),
        ("chance", "chance(", "chance(p)"), ("lerp", "lerp(", "lerp(a, b, t)"), ("clamp", "clamp(", "clamp(v, a, b)"),
        ("ease", "ease(", "ease(name, t)"), ("dist", "dist(", "dist(x1, y1, x2, y2)"), ("angleTo", "angleTo(", "angleTo(x1, y1, x2, y2) in degrees"),
        ("rad", "rad(", "degrees to radians"), ("deg", "deg(", "radians to degrees"), ("vec", "vec(", "vec(angle, length) → {x, y}"),
        ("rgb", "rgb(", "rgb(r, g, b, a) → '#rrggbb'"), ("sound", "sound(", "sound(name, volume)"), ("log", "log(", "log(...) to the console"),
        ("add", "add(", "patterns: add({ shape, start, end, x, y, ... })"),
    };

    private static readonly (string Name, string Hint)[] CameraApi =
    {
        ("move", "move(x, y) this frame"), ("rotate", "rotate(deg) this frame"), ("zoom", "zoom(scale, x, y) this frame"),
        ("kick", "kick(x, y, ms, ease)"), ("kickRotate", "kickRotate(deg, ms, ease)"), ("kickZoom", "kickZoom(scale, ms, ease)"), ("shake", "shake(intensity, ms)"),
    };

    private static readonly (string Name, string Hint)[] ScreenApi =
    {
        ("flash", "flash(color, alpha, ms, hold)"), ("background", "background(color) this frame"), ("width", "1280"), ("height", "720"),
        ("left", "-640"), ("right", "640"), ("top", "-360"), ("bottom", "360"),
    };

    private static readonly (string Name, string Hint)[] ThisApi =
    {
        ("x", "where it spawned"), ("y", "where it spawned"), ("rot", "its rotation (degrees)"), ("age", "ms since it spawned"), ("params", "its settings"),
    };

    private static readonly string[] OptionKeys = { "rot", "color", "alpha", "hurts", "layer", "front", "warning", "align", "bold" };

    private static CodeHighlighter BuildHighlighter()
    {
        var h = new CodeHighlighter
        {
            NumberColor = new Color("ffd23f"),
            SymbolColor = new Color("8a91a3"),
            FunctionColor = new Color("33e5ff"),
            MemberVariableColor = new Color("c7d0e0"),
        };
        var kw = new Color("ff2a6d");
        foreach (var k in Keywords) h.AddKeywordColor(k, kw);
        var api = new Color("7cf0c0");
        foreach (var a in Api) if (!Keywords.Contains(a.Name)) h.AddKeywordColor(a.Name, api);
        h.AddKeywordColor("Math", api);
        h.AddColorRegion("//", "", new Color("5d6577"), true);
        h.AddColorRegion("/*", "*/", new Color("5d6577"));
        var str = new Color("b3f27a");
        h.AddColorRegion("'", "'", str);
        h.AddColorRegion("\"", "\"", str);
        h.AddColorRegion("`", "`", str);
        return h;
    }

    private void OfferCompletion()
    {
        var line = code.GetLine(code.GetCaretLine());
        int c = Math.Min(code.GetCaretColumn(), line.Length), s = c;
        while (s > 0 && (char.IsLetterOrDigit(line[s - 1]) || line[s - 1] == '_')) s--;
        string before = s > 0 ? line[..s] : "";
        var fn = new Color("33e5ff");
        var member = new Color("c7d0e0");
        if (before.EndsWith("camera.")) foreach (var m in CameraApi) code.AddCodeCompletionOption(CodeEdit.CodeCompletionKind.Function, m.Name + "   " + m.Hint, m.Name + "(", fn);
        else if (before.EndsWith("screen.")) foreach (var m in ScreenApi) code.AddCodeCompletionOption(m.Name is "flash" or "background" ? CodeEdit.CodeCompletionKind.Function : CodeEdit.CodeCompletionKind.Member, m.Name + "   " + m.Hint, m.Name is "flash" or "background" ? m.Name + "(" : m.Name, member);
        else if (before.EndsWith("this.")) foreach (var m in ThisApi) code.AddCodeCompletionOption(CodeEdit.CodeCompletionKind.Member, m.Name + "   " + m.Hint, m.Name, member);
        else if (before.EndsWith("Math.")) foreach (var m in new[] { "sin", "cos", "tan", "atan2", "sqrt", "abs", "min", "max", "floor", "ceil", "round", "pow", "PI", "sign", "hypot", "exp", "log" })
            code.AddCodeCompletionOption(m == "PI" ? CodeEdit.CodeCompletionKind.Constant : CodeEdit.CodeCompletionKind.Function, m, m == "PI" ? m : m + "(", member);
        else if (before.EndsWith(".")) { code.UpdateCodeCompletionOptions(false); return; }
        else
        {
            foreach (var a in Api) code.AddCodeCompletionOption(a.Insert.EndsWith("(") ? CodeEdit.CodeCompletionKind.Function : CodeEdit.CodeCompletionKind.PlainText, a.Name + "   " + a.Hint, a.Insert, fn);
            foreach (var k in Keywords) code.AddCodeCompletionOption(CodeEdit.CodeCompletionKind.PlainText, k, k, new Color("ff2a6d"));
            foreach (var k in OptionKeys) code.AddCodeCompletionOption(CodeEdit.CodeCompletionKind.Member, k + ":   draw option", k + ": ", member);
        }
        code.UpdateCodeCompletionOptions(false);
    }
}
