using System;
using System.Collections.Generic;
using System.Linq;
using Beatform.Editor.Model;
using Godot;

namespace Beatform.Editor.UI;

/// <summary>Properties of the selection: one object, several objects, an event, a checkpoint, or the level.</summary>
public partial class InspectorPanel : PanelContainer
{
    private static EditorState S => EditorState.I;
    private static readonly string[] Eases =
    {
        "linear", "step", "inSine", "outSine", "inOutSine", "inQuad", "outQuad", "inOutQuad", "inCubic", "outCubic", "inOutCubic",
        "inQuart", "outQuart", "inOutQuart", "inQuint", "outQuint", "inOutQuint", "inExpo", "outExpo", "inOutExpo",
        "inCirc", "outCirc", "inOutCirc", "inBack", "outBack", "inOutBack", "inElastic", "outElastic", "inOutElastic",
        "inBounce", "outBounce", "inOutBounce",
    };

    private VBoxContainer box;
    private readonly List<Action> refreshers = new();
    private string signature = "";

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(376, 0);
        SizeFlagsHorizontal = SizeFlags.Fill;
        // Content never widens the panel; it scrolls (without a visible bar) if it ever must.
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever };
        AddChild(scroll);
        box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(box);
        S.SelectionChanged += Rebuild;
        S.DocReplaced += Rebuild;
        S.DocChanged += OnDocChanged;
        Rebuild();
    }

    private string Signature()
    {
        var o = S.Primary;
        if (o != null) return $"o:{o.Id}:{o.Shape.Type}:{string.Join(",", EditObject.TrackNames.Select(n => o.Track(n).Count))}:{S.Doc.Objects.Count}:{o.Color == null}";
        if (S.SelectedEvent >= 0 && S.SelectedEvent < S.Doc.Events.Count) return $"e:{S.SelectedEvent}:{S.Doc.Events[S.SelectedEvent].Type}";
        if (S.SelectedCheckpoint >= 0) return $"c:{S.SelectedCheckpoint}:{S.Doc.Checkpoints.Count}";
        return $"m:{S.Selected.Count}:{S.Doc.Objects.Count}";
    }

    private void OnDocChanged()
    {
        if (Signature() != signature) Rebuild();
        else Refresh();
    }

    public void Refresh()
    {
        foreach (var r in refreshers) r();
    }

    public void Rebuild()
    {
        foreach (var c in box.GetChildren()) c.QueueFree();
        refreshers.Clear();
        signature = Signature();
        if (S.Primary != null) BuildObject(S.Primary);
        else if (S.Selected.Count > 1) BuildMulti();
        else if (S.SelectedEvent >= 0 && S.SelectedEvent < S.Doc.Events.Count) BuildEvent(S.Doc.Events[S.SelectedEvent]);
        else if (S.SelectedCheckpoint >= 0 && S.SelectedCheckpoint < S.Doc.Checkpoints.Count) BuildCheckpoint(S.SelectedCheckpoint);
        else BuildLevel();
    }

    // ---------------- building blocks ----------------

    private void Section(string title)
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 10);
        box.AddChild(sep);
        box.AddChild(UiTheme.Heading(title));
    }

    private HBoxContainer Row(string label, Control field, string tooltip = null)
    {
        var row = new HBoxContainer();
        var l = new Label { Text = label, CustomMinimumSize = new Vector2(92, 0), TooltipText = tooltip ?? "", ClipText = true };
        l.AddThemeColorOverride("font_color", UiTheme.Dim);
        l.MouseFilter = MouseFilterEnum.Pass;
        row.AddChild(l);
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (tooltip != null) field.TooltipText = tooltip;
        row.AddChild(field);
        box.AddChild(row);
        return row;
    }

    private SpinBox Num(Func<double> get, Action<double> set, string undoLabel, double step = 1, double min = -100000, double max = 100000, string suffix = "")
    {
        var sb = new SpinBox { Step = step, MinValue = min, MaxValue = max, AllowGreater = true, AllowLesser = true, Suffix = suffix, SelectAllOnFocus = true };
        sb.SetValueNoSignal(get());
        sb.ValueChanged += v => S.Edit(undoLabel, () => set(v));
        refreshers.Add(() => { if (!sb.GetLineEdit().HasFocus()) sb.SetValueNoSignal(get()); });
        return sb;
    }

    private LineEdit Text(Func<string> get, Action<string> set, string undoLabel)
    {
        var le = new LineEdit { Text = get() ?? "" };
        le.TextSubmitted += v => { S.Edit(undoLabel, () => set(v)); le.ReleaseFocus(); };
        le.FocusExited += () => { if (le.Text != (get() ?? "")) S.Edit(undoLabel, () => set(le.Text)); };
        refreshers.Add(() => { if (!le.HasFocus()) le.Text = get() ?? ""; });
        return le;
    }

    private CheckBox Check(string text, Func<bool> get, Action<bool> set, string undoLabel)
    {
        var cb = new CheckBox { Text = text };
        cb.SetPressedNoSignal(get());
        cb.Toggled += v => S.Edit(undoLabel, () => set(v));
        refreshers.Add(() => cb.SetPressedNoSignal(get()));
        return cb;
    }

    private OptionButton Options(string[] values, Func<string> get, Action<string> set, string undoLabel, string[] labels = null)
    {
        var ob = new OptionButton();
        for (int i = 0; i < values.Length; i++) ob.AddItem(labels?[i] ?? values[i], i);
        ob.Selected = Math.Max(0, Array.IndexOf(values, get()));
        ob.ItemSelected += i => S.Edit(undoLabel, () => set(values[i]));
        return ob;
    }

    private ColorPickerButton ColorField(Func<string> get, Action<string> set, string undoLabel, string fallback)
    {
        var cp = new ColorPickerButton { Color = UiTheme.ParseHex(get(), UiTheme.ParseHex(fallback, UiTheme.Pink)), CustomMinimumSize = new Vector2(0, 26), EditAlpha = false };
        cp.PopupClosed += () =>
        {
            var hex = UiTheme.ToHex(cp.Color);
            if (hex != get()) S.Edit(undoLabel, () => set(hex));
        };
        return cp;
    }

    private Button Btn(string text, Action onPress, string tooltip = null)
    {
        var b = new Button { Text = text, TooltipText = tooltip ?? "" };
        b.Pressed += onPress;
        return b;
    }

    private void Buttons(params Button[] buttons)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        foreach (var b in buttons) { b.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(b); }
        box.AddChild(row);
    }

    // ---------------- object ----------------

    private void BuildObject(EditObject o)
    {
        var title = new HBoxContainer();
        title.AddChild(UiTheme.Heading("Object", 12, UiTheme.Pink));
        var id = UiTheme.Small("  " + o.Id + (o.Group != null ? "  ·  pattern " + o.Group : ""));
        id.AutowrapMode = TextServer.AutowrapMode.Off;
        title.AddChild(id);
        box.AddChild(title);
        Row("Name", Text(() => o.Name, v => o.Name = v, "Rename"));

        Section("Shape");
        Row("Type", Options(ShapeSpec.Types, () => o.Shape.Type, v => o.Shape.Type = v, "Change shape",
            new[] { "Rectangle", "Circle", "Polygon", "Star", "Ring", "Spike ball" }));
        switch (o.Shape.Type)
        {
            case "polygon": Row("Sides", Num(() => o.Shape.Sides, v => o.Shape.Sides = (int)Math.Clamp(v, 3, 12), "Sides", 1, 3, 12)); break;
            case "star":
                Row("Points", Num(() => o.Shape.Points, v => o.Shape.Points = (int)Math.Clamp(v, 3, 12), "Points", 1, 3, 12));
                Row("Inner", Num(() => o.Shape.Inner, v => o.Shape.Inner = Math.Clamp(v, 0.1, 0.95), "Inner radius", 0.01, 0.1, 0.95));
                break;
            case "spikes":
                Row("Spikes", Num(() => o.Shape.Spikes, v => o.Shape.Spikes = (int)Math.Clamp(v, 4, 24), "Spikes", 1, 4, 24));
                Row("Inner", Num(() => o.Shape.Inner, v => o.Shape.Inner = Math.Clamp(v, 0.4, 0.95), "Inner radius", 0.01, 0.4, 0.95));
                break;
            case "ring": Row("Thickness", Num(() => o.Shape.Thickness, v => o.Shape.Thickness = Math.Clamp(v, 0.02, 0.5), "Ring thickness", 0.01, 0.02, 0.5)); break;
        }

        Section("Timing");
        Row("Appears", Num(() => o.Start, v => { double d = o.End - o.Start; o.Start = v; o.End = v + d; }, "Move in time", 1, -100000, 10000000, " ms"),
            "Song time the object appears (its warning starts). Changing it keeps the length.");
        Row("Warning", Num(() => o.Warn, v => o.Warn = Math.Max(0, v), "Warning length", 1, 0, 100000, " ms"), "Harmless preview before it becomes dangerous.");
        Row("Gone at", Num(() => o.End, v => o.End = Math.Max(o.Start + 1, v), "End time", 1, -100000, 10000000, " ms"));
        Row("Fade out", Num(() => o.Outro, v => o.Outro = Math.Max(0, v), "Fade out", 1, 0, 100000, " ms"), "Harmless fade at the end.");
        var beats = UiTheme.Small("");
        void UpdateBeats() => beats.Text = $"dangerous {S.FormatBeats(o.Start + o.Warn)} → {S.FormatBeats(o.End - o.Outro)}   ·   {o.Duration / S.Doc.BeatMs:0.##} beats long";
        UpdateBeats();
        refreshers.Add(UpdateBeats);
        box.AddChild(beats);
        Buttons(
            Btn("Appear here", () => S.Edit("Start at playhead", () => { double d = o.End - o.Start; o.Start = S.Playhead; o.End = S.Playhead + d; })),
            Btn("Hit here", () => S.Edit("Danger at playhead", () => { double d = o.End - o.Start; o.Start = S.Playhead - o.Warn; o.End = o.Start + d; }), "Moves the object so it becomes dangerous exactly at the playhead."));

        Section("Collision and look");
        Row("Hitbox", Check("hurts players", () => o.Hitbox, v => o.Hitbox = v, "Toggle hitbox"));
        if (o.Hitbox) Row("Hitbox size", Num(() => o.HitboxScale, v => o.HitboxScale = Math.Max(0, v), "Hitbox size", 0.05, 0, 3, " ×"), "Multiplies the level's hitbox scale.");
        var colorRow = new HBoxContainer();
        var useTheme = new CheckBox { Text = "theme" };
        useTheme.SetPressedNoSignal(o.Color == null);
        useTheme.Toggled += v => S.Edit("Color", () => o.Color = v ? null : S.Doc.Hazard);
        colorRow.AddChild(useTheme);
        if (o.Color != null)
        {
            var cp = ColorField(() => o.Color, v => o.Color = v, "Color", S.Doc.Hazard);
            cp.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            colorRow.AddChild(cp);
        }
        Row("Color", colorRow);
        Row("Layer", Num(() => o.Layer, v => o.Layer = (int)v, "Layer", 1, -100, 100), "Higher layers draw on top.");

        Section("Parent");
        var others = S.Doc.Objects.Where(x => x.Id != o.Id && !IsDescendant(x, o)).ToList();
        var ids = new[] { "" }.Concat(others.Select(x => x.Id)).ToArray();
        var names = new[] { "none" }.Concat(others.Select(x => string.IsNullOrEmpty(x.Name) ? $"{x.Id} ({x.Shape.Type})" : $"{x.Name} ({x.Id})")).ToArray();
        Row("Follows", Options(ids, () => o.Parent ?? "", v => o.Parent = string.IsNullOrEmpty(v) ? null : v, "Set parent", names),
            "A child moves and turns with its parent. Its position becomes relative to the parent.");
        Row("Pivot X", Num(() => o.PivotX, v => o.PivotX = v, "Pivot", 0.05, -2, 2), "-0.5 is the left edge, 0 the center, 0.5 the right edge.");
        Row("Pivot Y", Num(() => o.PivotY, v => o.PivotY = v, "Pivot", 0.05, -2, 2), "-0.5 is the top edge.");

        Buttons(Btn("Repeat…", () => ToolDialogs.ShowRepeat(App.Instance), "Add copies shifted in time, position, rotation or around a point."),
            Btn("Duplicate", () => App.Instance.DuplicateSelection()));

        Section("Animation at the playhead");
        box.AddChild(UiTheme.Small("Values at the playhead. Dragging on the stage or typing here writes a key at the playhead once a track has two or more keys; with one key the whole object changes. Hold R while the song plays to record a path with the mouse."));
        foreach (var track in EditObject.TrackNames) BuildTrack(o, track);
    }

    private static bool IsDescendant(EditObject maybeChild, EditObject of)
    {
        var p = maybeChild;
        for (int i = 0; i < 32 && p != null; i++)
        {
            if (p.Parent == of.Id) return true;
            p = EditorState.I.Doc.Find(p.Parent);
        }
        return false;
    }

    private static readonly Dictionary<string, string> TrackLabels = new()
    {
        ["x"] = "X", ["y"] = "Y", ["rot"] = "Rotation", ["w"] = "Width", ["h"] = "Height", ["opacity"] = "Opacity",
    };

    private void BuildTrack(EditObject o, string track)
    {
        var keys = o.Track(track);
        double step = track == "opacity" ? 0.05 : 1;
        var header = new HBoxContainer();
        var name = new Label { Text = TrackLabels[track], CustomMinimumSize = new Vector2(96, 0) };
        name.AddThemeColorOverride("font_color", keys.Count > 1 ? UiTheme.Cyan : UiTheme.Text);
        header.AddChild(name);
        var value = new SpinBox { Step = step, MinValue = -100000, MaxValue = 100000, AllowGreater = true, AllowLesser = true, SelectAllOnFocus = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        value.SetValueNoSignal(CanvasView.Eval(o, track, S.Playhead - o.Start));
        value.ValueChanged += v => S.Edit("Set " + TrackLabels[track], () => CanvasView.SetAt(o, track, S.Playhead - o.Start, v));
        refreshers.Add(() => { if (!value.GetLineEdit().HasFocus()) value.SetValueNoSignal(CanvasView.Eval(o, track, S.Playhead - o.Start)); });
        header.AddChild(value);
        header.AddChild(Btn("◆", () => S.Edit("Add key", () => CanvasView.AddKeyAt(o, track, S.Playhead - o.Start)), "Add a key at the playhead (makes the track animated)."));
        if (keys.Count > 1)
            header.AddChild(Btn("✕", () => S.Edit("Remove animation", () => { var v = CanvasView.Eval(o, track, S.Playhead - o.Start); keys.Clear(); keys.Add(new EditKey { T = 0, V = v }); }), "Remove the animation, keep the current value."));
        box.AddChild(header);

        if (keys.Count <= 1) return;
        foreach (var k in keys.OrderBy(k => k.T).ToList())
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            var spacer = new Control { CustomMinimumSize = new Vector2(14, 0) };
            row.AddChild(spacer);
            var kt = new SpinBox { Step = 1, MinValue = -100000, MaxValue = 10000000, AllowGreater = true, Suffix = "ms", CustomMinimumSize = new Vector2(92, 0), SelectAllOnFocus = true, TooltipText = "Time from when the object appears" };
            kt.SetValueNoSignal(k.T);
            kt.ValueChanged += v => S.Edit("Move key", () => { k.T = v; keys.Sort((a, b) => a.T.CompareTo(b.T)); });
            row.AddChild(kt);
            var kv = new SpinBox { Step = step, MinValue = -100000, MaxValue = 100000, AllowGreater = true, AllowLesser = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SelectAllOnFocus = true };
            kv.SetValueNoSignal(k.V);
            kv.ValueChanged += v => S.Edit("Key value", () => k.V = v);
            refreshers.Add(() => { if (!kv.GetLineEdit().HasFocus()) kv.SetValueNoSignal(k.V); if (!kt.GetLineEdit().HasFocus()) kt.SetValueNoSignal(k.T); });
            row.AddChild(kv);
            var ease = new OptionButton { CustomMinimumSize = new Vector2(96, 0), TooltipText = "Ease into this key from the previous one" };
            foreach (var e in Eases) ease.AddItem(e);
            ease.Selected = Math.Max(0, Array.IndexOf(Eases, k.Ease));
            ease.ItemSelected += i => S.Edit("Ease", () => k.Ease = Eases[i]);
            row.AddChild(ease);
            var go = Btn("⏵", () => { S.Playhead = o.Start + k.T; App.Instance.PlayheadMoved(); }, "Jump to this key");
            row.AddChild(go);
            row.AddChild(Btn("×", () => S.Edit("Delete key", () => keys.Remove(k)), "Delete this key"));
            box.AddChild(row);
        }
    }

    // ---------------- other selections ----------------

    private void BuildMulti()
    {
        var list = S.SelectedObjects.ToList();
        box.AddChild(UiTheme.Heading($"{list.Count} objects", 12, UiTheme.Pink));
        box.AddChild(UiTheme.Small($"from {S.FormatTime(list.Min(o => o.Start))} to {S.FormatTime(list.Max(o => o.End))}"));
        Section("Change all");
        double shift = 0;
        var shiftBox = new SpinBox { Step = 1, MinValue = -100000, MaxValue = 100000, Suffix = " ms", AllowGreater = true, AllowLesser = true };
        shiftBox.ValueChanged += v => shift = v;
        Row("Shift time", shiftBox);
        Buttons(
            Btn("Apply shift", () => S.Edit("Shift time", () => { foreach (var o in S.SelectedObjects) { o.Start += shift; o.End += shift; } })),
            Btn("Shift by 1 beat", () => S.Edit("Shift time", () => { foreach (var o in S.SelectedObjects) { o.Start += S.Doc.BeatMs; o.End += S.Doc.BeatMs; } })));
        Row("Layer", Num(() => list[0].Layer, v => { foreach (var o in S.SelectedObjects) o.Layer = (int)v; }, "Layer", 1, -100, 100));
        Row("Color", ColorField(() => list[0].Color ?? S.Doc.Hazard, v => { foreach (var o in S.SelectedObjects) o.Color = v; }, "Color", S.Doc.Hazard));
        Row("Hitbox", Check("hurts players", () => list.All(o => o.Hitbox), v => { foreach (var o in S.SelectedObjects) o.Hitbox = v; }, "Toggle hitbox"));
        Buttons(Btn("Use theme color", () => S.Edit("Color", () => { foreach (var o in S.SelectedObjects) o.Color = null; })));
        Section("Actions");
        Buttons(Btn("Duplicate (Ctrl+D)", () => App.Instance.DuplicateSelection()), Btn("Delete (Del)", () => App.Instance.DeleteSelection()));
        Buttons(Btn("Mirror horizontally", () => App.Instance.Mirror(true)), Btn("Mirror vertically", () => App.Instance.Mirror(false)));
        Buttons(Btn("Repeat…", () => ToolDialogs.ShowRepeat(App.Instance)), Btn("Stretch time…", () => ToolDialogs.ShowStretch(App.Instance)));
    }

    private void BuildEvent(EditEvent e)
    {
        box.AddChild(UiTheme.Heading("Scene event", 12, UiTheme.Pink));
        Row("Type", Options(EditEvent.Types, () => e.Type, v => e.Type = v, "Event type", new[] { "Camera shake", "Screen flash", "Background color" }));
        Row("Time", Num(() => e.T, v => e.T = Math.Max(0, v), "Event time", 1, 0, 10000000, " ms"));
        Row("Duration", Num(() => e.Duration, v => e.Duration = Math.Max(0, v), "Event duration", 1, 0, 100000, " ms"));
        switch (e.Type)
        {
            case "shake":
                Row("Strength", Num(() => e.Intensity, v => e.Intensity = Math.Max(0, v), "Shake strength", 1, 0, 100));
                break;
            case "flash":
                Row("Color", ColorField(() => e.Color, v => e.Color = v, "Flash color", "#ffffff"));
                Row("Opacity", Num(() => e.Opacity, v => e.Opacity = Math.Clamp(v, 0, 1), "Flash opacity", 0.05, 0, 1));
                break;
            case "background":
                Row("Color", ColorField(() => e.Color, v => e.Color = v, "Background color", "#000000"));
                Row("Ease", Options(Eases, () => e.Ease, v => e.Ease = v, "Ease"));
                break;
        }
        Buttons(Btn("Move to playhead", () => S.Edit("Move event", () => e.T = S.Playhead)), Btn("Delete", () => App.Instance.DeleteSelection()));
    }

    private void BuildCheckpoint(int index)
    {
        box.AddChild(UiTheme.Heading("Checkpoint", 12, UiTheme.Good));
        if (index == 0) { box.AddChild(UiTheme.Small("The level always starts with a checkpoint at 0.")); return; }
        Row("Time", Num(() => S.Doc.Checkpoints[index], v => { S.Doc.Checkpoints[index] = Math.Max(1, v); }, "Move checkpoint", 1, 1, 10000000, " ms"));
        box.AddChild(UiTheme.Small("When every player dies, the game rewinds to the last checkpoint reached."));
        Buttons(Btn("Move to playhead", () => S.Edit("Move checkpoint", () => S.Doc.Checkpoints[index] = Math.Max(1, S.Playhead))), Btn("Delete", () => App.Instance.DeleteSelection()));
    }

    private void BuildLevel()
    {
        var d = S.Doc;
        box.AddChild(UiTheme.Heading("Level", 12, UiTheme.Pink));
        var title = new Label { Text = d.Title };
        title.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(title);
        box.AddChild(UiTheme.Small($"{d.Artist}{(string.IsNullOrEmpty(d.Author) ? "" : "  ·  level by " + d.Author)}", UiTheme.Cyan));
        box.AddChild(UiTheme.Small($"{d.Objects.Count} objects · {d.Events.Count} events · {d.Checkpoints.Count} checkpoints · {d.Bpm:0.##} BPM"));
        Buttons(Btn("Level settings…", () => App.Instance.ShowLevelSettings()));
        Section("Getting started");
        foreach (var tip in new[]
        {
            "Drag a shape or pattern from the left onto the stage, or click it to add one at the playhead.",
            "Click and drag objects to move them. Gold handle rotates, white corners resize. Shift snaps.",
            "Moving an object with two or more keys writes a key at the playhead: scrub, move, repeat.",
            "Timeline: drag bars to move in time, drag edges to change length. Ctrl+drag duplicates.",
            "Double-click the checkpoint row to add a checkpoint.",
            "Space previews from the playhead. To play the level, use Test in game.",
            "F5 tests the level in the real game when the Beatform loader is installed.",
        }) box.AddChild(UiTheme.Small("• " + tip, UiTheme.Text));
        Section("Shortcuts");
        box.AddChild(UiTheme.Small("Ctrl+N new · Ctrl+O open · Ctrl+S save · Ctrl+Z / Ctrl+Y undo / redo · Ctrl+C / Ctrl+V copy / paste at playhead · Ctrl+D duplicate · Del delete · K key all tracks · Arrows nudge (Shift ×10) · , and . step one grid · Home / End · Ctrl+wheel zoom · Shift+wheel scroll lanes"));
    }
}
