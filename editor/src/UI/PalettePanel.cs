using System;
using System.Collections.Generic;
using System.Linq;
using Beatform.Core;
using Beatform.Editor.Model;
using Beatform.Editor.Scripts;
using Godot;

namespace Beatform.Editor.UI;

/// <summary>
/// Things to add: plain shapes, scripted attacks and effects, patterns and scene items. Click to add at
/// the playhead in the middle of the stage (patterns open their settings first), or drag onto the stage.
/// Attacks, effects and patterns are scripts: the level's own (scripts/) and the built-in library.
/// </summary>
public partial class PalettePanel : PanelContainer
{
    private static EditorState S => EditorState.I;
    private readonly Dictionary<string, Pattern> patterns = new();
    private VBoxContainer scriptBox;

    private static readonly (string id, string label, string tip)[] Shapes =
    {
        ("rect", "■  Square", "Rectangle"),
        ("circle", "●  Circle", "Circle"),
        ("tri", "▲  Triangle", "Three-sided polygon"),
        ("hex", "⬢  Hexagon", "Six-sided polygon"),
        ("star", "★  Star", "Five-pointed star"),
        ("ring", "◯  Ring", "Ring with a safe hole"),
        ("spikes", "✹  Spike ball", "Round spiky ball"),
        ("text", "A  Text", "Words in Beatform's font"),
        ("imposter", "◆  Imposter", "A copy of a player that chases the nearest one, dashing like a player"),
    };

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(200, 0);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever };
        AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(box);

        box.AddChild(UiTheme.Heading("Shapes"));
        foreach (var (id, label, tip) in Shapes) box.AddChild(Item("shape:" + id, label, tip + ". Click to add at the playhead, or drag onto the stage."));

        scriptBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scriptBox.AddThemeConstantOverride("separation", 4);
        box.AddChild(scriptBox);
        RefreshScripts();

        box.AddChild(new HSeparator());
        box.AddChild(UiTheme.Heading("Scene"));
        box.AddChild(Item("scene:checkpoint", "⚑  Checkpoint", "Players rewind here when everyone dies."));
        box.AddChild(Item("scene:shake", "≋  Camera shake", "Shakes the camera."));
        box.AddChild(Item("scene:flash", "☼  Screen flash", "A full-screen flash that fades out."));
        box.AddChild(Item("scene:background", "◐  Background color", "Fades the background to a new color."));
        box.AddChild(Item("scene:sway", "∿  Camera sway", "The whole scene sways gently from here on."));
        box.AddChild(Item("scene:sound", "♪  Game sound", "Plays one of the game's own sounds, like a player's death."));
        box.AddChild(Item("scene:playerSpeed", "‖  Player speed", "Slows the players down (or roots them) from here on, until the next player speed event."));
        box.AddChild(Item("scene:credits", "T  Title card", "Shows the game's title card (song and artist) here instead of at the start."));
        box.AddChild(Item("scene:zoom", "+  Camera zoom", "Zooms the camera in or out from here on, on a point or on the player."));
    }

    /// <summary>Rebuilds the scripted sections (attacks, effects, patterns) from the level and library scripts.</summary>
    public void RefreshScripts()
    {
        if (scriptBox == null) return;
        foreach (var c in scriptBox.GetChildren()) c.QueueFree();
        var all = ScriptsService.I.All.ToList();
        var lastSettings = new Dictionary<string, Pattern>(patterns);
        patterns.Clear();

        void Section(string title, IEnumerable<ScriptType> list, string prefix, string icon, string hint)
        {
            var items = list.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ToList();
            if (items.Count == 0) return;
            scriptBox.AddChild(new HSeparator());
            scriptBox.AddChild(UiTheme.Heading(title));
            foreach (var t in items)
            {
                string mine = ScriptsService.I.IsInLevel(t.Name) ? " (this level)" : "";
                string err = t.CompileError != null ? "  ⚠" : "";
                scriptBox.AddChild(Item(prefix + t.Name, $"{icon}  {t.Title}{err}", (t.Description ?? "") + mine + hint
                    + (t.CompileError != null ? "\n\nError: " + t.CompileError : "")));
            }
        }

        bool IsEffect(ScriptType t) => ScriptsService.I.IsEffect(t);
        Section("Attacks", all.Where(t => t.Kind == ScriptKind.Attack && !IsEffect(t)), "script:", "⚡",
            " Runs live in the game. Click to add at the playhead, or drag onto the stage.");
        Section("Effects", all.Where(IsEffect), "script:", "✺",
            " Runs live in the game. Click to add at the playhead.");
        foreach (var t in all.Where(t => t.Kind == ScriptKind.Pattern && t.CompileError == null))
            patterns[t.Name] = lastSettings.TryGetValue(t.Name, out var old) && old.Script == t ? old : new Pattern(t);
        Section("Patterns", all.Where(t => t.Kind == ScriptKind.Pattern), "pattern:", "✦",
            " Builds ordinary objects you can edit. Click to set it up, or drag onto the stage to use the last settings.");

        var buttons = new HBoxContainer();
        var add = new Button { Text = "+ New script", TooltipText = "Write your own attack, effect or pattern (JavaScript).", SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        add.Pressed += () => ScriptEditorWindow.ShowNew(this);
        buttons.AddChild(add);
        var code = new Button { Text = "{ } Code", TooltipText = "Open the script editor.", FocusMode = FocusModeEnum.None };
        code.Pressed += () => ScriptEditorWindow.Open(null);
        buttons.AddChild(code);
        scriptBox.AddChild(new HSeparator());
        scriptBox.AddChild(buttons);
    }

    private Control Item(string id, string label, string tip)
    {
        var b = new PaletteButton { Text = label, TooltipText = tip, Alignment = HorizontalAlignment.Left, PayloadId = id };
        b.Pressed += () => Activate(id);
        return b;
    }

    /// <summary>Adds an item by clicking: at the playhead, in the middle of the stage.</summary>
    private void Activate(string id)
    {
        if (id.StartsWith("pattern:"))
        {
            if (!patterns.TryGetValue(id[8..], out var p)) { ScriptEditorWindow.Open(id[8..]); return; }
            PatternDialog.Show(this, p, () => AddPattern(p, 0, 0));
            return;
        }
        DropAt(id, 0, 0);
    }

    /// <summary>Adds an item at a stage position (game units), using the last settings for patterns.</summary>
    public void DropAt(string id, double x, double y)
    {
        double t = S.Playhead;
        double beat = S.Doc.BeatMs;
        if (id.StartsWith("pattern:")) { if (patterns.TryGetValue(id[8..], out var pat)) AddPattern(pat, x, y); return; }
        if (id.StartsWith("script:")) { AddScript(id[7..], x, y); return; }
        if (id.StartsWith("scene:"))
        {
            switch (id[6..])
            {
                case "checkpoint":
                    if (S.Doc.Checkpoints.Any(c => Math.Abs(c - t) < 1)) return;
                    S.Edit("Add checkpoint", () => { S.Doc.Checkpoints.Add(Math.Max(0, t)); S.Doc.Checkpoints.Sort(); });
                    S.SelectCheckpoint(S.Doc.Checkpoints.IndexOf(Math.Max(0, t)));
                    return;
                case "shake":
                    AddEvent(new EditEvent { T = t, Type = "shake", Duration = 350, Intensity = 12 });
                    return;
                case "flash":
                    AddEvent(new EditEvent { T = t, Type = "flash", Duration = 300, Color = "#ffffff", Opacity = 0.6 });
                    return;
                case "background":
                    AddEvent(new EditEvent { T = t, Type = "background", Duration = beat * 4, Color = "#1a0a24", Ease = "inOutSine" });
                    return;
                case "sway":
                    AddEvent(new EditEvent { T = t, Type = "sway", Duration = beat * 2, Ease = "inOutSine" });
                    return;
                case "sound":
                    AddEvent(new EditEvent { T = t, Type = "sound", Duration = 0, Sound = "playerDeath" });
                    return;
                case "playerSpeed":
                    AddEvent(new EditEvent { T = t, Type = "playerSpeed", Duration = 150, Speed = 0.05 });
                    return;
                case "zoom":
                    AddEvent(new EditEvent { T = t, Type = "zoom", Duration = beat * 4, Scale = 1.3, Focus = "@player", Ease = "inOutSine", SwayX = 0, SwayY = 0 });
                    return;
                case "credits":
                    if (S.Doc.Events.Any(e => e.Type == "credits")) return;
                    AddEvent(new EditEvent { T = t, Type = "credits", Duration = 0 });
                    return;
            }
        }
        if (id.StartsWith("shape:"))
        {
            var o = new EditObject
            {
                Id = S.Doc.NewId(),
                Start = Math.Round(t - beat, 3),
                Warn = Math.Round(beat, 3),
                End = Math.Round(t + beat * 3, 3),
                Outro = 200,
            };
            switch (id[6..])
            {
                case "tri": o.Shape.Type = "polygon"; o.Shape.Sides = 3; break;
                case "hex": o.Shape.Type = "polygon"; o.Shape.Sides = 6; break;
                case "star": o.Shape.Type = "star"; o.Shape.Points = 5; o.Shape.Inner = 0.45; break;
                case "spikes": o.Shape.Type = "spikes"; o.Shape.Spikes = 12; o.Shape.Inner = 0.75; break;
                case "ring": o.Shape.Type = "ring"; break;
                default: o.Shape.Type = id[6..]; break;
            }
            o.Track("x").Add(new EditKey { T = 0, V = Math.Round(x) });
            o.Track("y").Add(new EditKey { T = 0, V = Math.Round(y) });
            o.Track("w").Add(new EditKey { T = 0, V = 80 });
            o.Track("h").Add(new EditKey { T = 0, V = 80 });
            if (o.Shape.Type == "imposter")
            {
                // A player-sized square that hunts players for eight seconds, then leaves.
                o.Shape.Type = "rect";
                o.Name = "imposter";
                o.Warn = 1000;
                o.End = Math.Round(o.Start + o.Warn + 9000, 3);
                o.Outro = 0;
                o.Imposter = new ImposterSpec { LeaveAt = 8000 };
                o.Track("w")[0].V = 22;
                o.Track("h")[0].V = 22;
            }
            if (o.Shape.Type == "text")
            {
                // Words are usually captions, so they start harmless and at a readable size.
                o.Shape.Text = "TEXT";
                o.Hitbox = false;
                o.Warn = 0;
                o.Track("h")[0].V = 48;
                o.Track("w")[0].V = Math.Round(48 * Beatform.Core.TextFont.Width(o.Shape.Text, o.Shape.Bold), 2);
            }
            S.Edit("Add " + o.Shape.Type, () => S.Doc.Objects.Add(o));
            S.Select(new[] { o.Id });
        }
    }

    private void AddEvent(EditEvent e)
    {
        S.Edit("Add " + e.Type, () => S.Doc.Events.Add(e));
        S.SelectEvent(S.Doc.Events.IndexOf(e));
    }

    private void AddPattern(Pattern p, double x, double y)
    {
        var objs = p.Build(S.Doc, S.Playhead, Math.Round(x), Math.Round(y), out var error);
        if (error != null) { App.Instance.Status("Pattern failed: " + error, true); ScriptEditorWindow.Open(p.Id); return; }
        if (objs.Count == 0) { App.Instance.Status($"{p.Name} added nothing.", true); return; }
        S.Edit("Add " + p.Name, () => S.Doc.Objects.AddRange(objs));
        S.Select(objs.Select(o => o.Id));
    }

    /// <summary>Places a script from its name: patterns open their settings first.</summary>
    public void Place(string name)
    {
        if (patterns.TryGetValue(name, out var p)) { PatternDialog.Show(this, p, () => AddPattern(p, 0, 0)); return; }
        var t = ScriptsService.I.Find(name);
        if (t == null) { App.Instance.Status($"No script named '{name}'.", true); return; }
        if (t.CompileError != null) { App.Instance.Status("Fix the script first: " + t.CompileError, true); return; }
        if (t.Kind == ScriptKind.Pattern) { App.Instance.Status($"{t.Title} could not be loaded.", true); return; }
        AddScript(name, 0, 0);
    }

    /// <summary>Places a scripted attack or effect: an object that runs the script, starting at the playhead.</summary>
    public void AddScript(string name, double x, double y)
    {
        if (!ScriptsService.I.EnsureInLevel(name, out var error)) { App.Instance.Status(error, true); return; }
        var type = ScriptsService.I.FindInLevel(name);
        double beats = type?.LengthBeats > 0 ? type.LengthBeats : 8;
        var o = new EditObject
        {
            Id = S.Doc.NewId(),
            Name = type?.Title ?? name,
            Start = Math.Round(S.Playhead, 3),
            End = Math.Round(S.Playhead + beats * S.Doc.BeatMs, 3),
            Script = new ScriptSpec { Name = name },
            Hitbox = true,
        };
        o.Track("x").Add(new EditKey { T = 0, V = Math.Round(x) });
        o.Track("y").Add(new EditKey { T = 0, V = Math.Round(y) });
        o.Track("w").Add(new EditKey { T = 0, V = 40 });
        o.Track("h").Add(new EditKey { T = 0, V = 40 });
        S.Edit("Add " + o.Name, () => S.Doc.Objects.Add(o));
        S.Select(new[] { o.Id });
    }

    private partial class PaletteButton : Button
    {
        public string PayloadId;

        public override Variant _GetDragData(Vector2 atPosition)
        {
            var preview = new Label { Text = Text };
            preview.AddThemeColorOverride("font_color", UiTheme.Pink);
            SetDragPreview(preview);
            return new Godot.Collections.Dictionary { { "beatform", PayloadId } };
        }
    }
}

/// <summary>Settings dialog for a pattern, built from its parameter list.</summary>
public static class PatternDialog
{
    /// <summary>An input for one script parameter, writing into it as it changes.</summary>
    public static Control ParamField(PatternParam param)
    {
        switch (param.Kind)
        {
            case "bool":
            {
                var cb = new CheckBox { ButtonPressed = param.Flag, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                cb.Toggled += v => param.Flag = v;
                return cb;
            }
            case "text":
            {
                var le = new LineEdit { Text = param.Text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                le.TextChanged += v => param.Text = v;
                return le;
            }
            case "color":
            {
                var cp = new ColorPickerButton { Color = Color.FromString(string.IsNullOrEmpty(param.Text) ? "#ffffff" : param.Text, Colors.White), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 26) };
                cp.ColorChanged += c => param.Text = "#" + c.ToHtml(false);
                return cp;
            }
            case "choice" or "ease" or "shape":
            {
                var ob = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                var opts = param.Choices ?? Array.Empty<string>();
                foreach (var c in opts) ob.AddItem(c);
                int sel = Array.IndexOf(opts, param.Text);
                ob.Selected = sel >= 0 ? sel : 0;
                ob.ItemSelected += i => param.Text = opts[(int)i];
                return ob;
            }
            default:
            {
                var sb = new SpinBox { MinValue = param.Min, MaxValue = param.Max, Step = param.Kind == "int" ? Math.Max(1, param.Step) : param.Step, Value = param.Value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SelectAllOnFocus = true, AllowGreater = true, AllowLesser = true };
                if (param.Kind == "beats") sb.Suffix = "beats";
                sb.ValueChanged += v => param.Value = v;
                return sb;
            }
        }
    }

    public static void Show(Node parent, Pattern p, Action onAdd)
    {
        var dlg = new ConfirmationDialog { Title = p.Name, OkButtonText = "Add at playhead" };
        // A fixed width, so the wrapping texts know their height (otherwise the dialog fills the screen).
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(UiTheme.Small(p.Description, UiTheme.Text));
        foreach (var param in p.Params)
        {
            var row = new HBoxContainer();
            var l = new Label { Text = param.Label, CustomMinimumSize = new Vector2(150, 0), TooltipText = param.Tooltip ?? "" };
            row.AddChild(l);
            row.AddChild(ParamField(param));
            box.AddChild(row);
        }
        box.AddChild(UiTheme.Small("It becomes dangerous at the playhead. Drag the pattern from the palette onto the stage to place it somewhere else with these settings."));
        dlg.AddChild(box);
        dlg.Confirmed += () => { onAdd(); dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        parent.AddChild(dlg);
        UiTheme.PopupFitted(dlg);
    }
}
