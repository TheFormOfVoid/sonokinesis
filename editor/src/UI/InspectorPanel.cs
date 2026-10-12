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
        if (o != null) return $"o:{o.Id}:{o.Shape.Type}:{string.Join(",", EditObject.TrackNames.Select(n => o.Track(n).Count))}:{S.Doc.Objects.Count}:{o.Color}"
            + $":{o.Imposter?.Mode}:{o.Imposter?.StopAt >= 0}";
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

    /// <summary>
    /// Keeps a text object's letters in proportion. With the previous text, the width is scaled by the
    /// change in text length (keeping any stretch the maker chose); without it, the width is set from
    /// the height so the letters are undistorted.
    /// </summary>
    private static void FitTextWidth(EditObject o, string previous)
    {
        var w = o.Track("w");
        if (previous != null)
        {
            double r = Beatform.Core.TextFont.Width(o.Shape.Text, o.Shape.Bold) / Beatform.Core.TextFont.Width(previous, o.Shape.Bold);
            if (w.Count == 0) w.Add(new EditKey { T = 0, V = EditObject.DefaultValue("w") });
            foreach (var k in w) k.V = Math.Round(k.V * r, 2);
            return;
        }
        var h = o.Track("h");
        double hv = h.Count > 0 ? h[0].V : EditObject.DefaultValue("h");
        w.Clear();
        w.Add(new EditKey { T = 0, V = Math.Round(hv * Beatform.Core.TextFont.Width(o.Shape.Text, o.Shape.Bold), 2) });
    }

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
        Row("Type", Options(ShapeSpec.Types, () => o.Shape.Type, v =>
            {
                bool toText = v == "text" && o.Shape.Type != "text";
                o.Shape.Type = v;
                if (toText) FitTextWidth(o, null);
            }, "Change shape",
            new[] { "Rectangle", "Circle", "Polygon", "Star", "Ring", "Spike ball", "Text" }));
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
            case "text":
                Row("Text", Text(() => o.Shape.Text, v => { var old = o.Shape.Text; o.Shape.Text = v; FitTextWidth(o, old); }, "Edit text"),
                    "One line of letters, digits and punctuation. The height is the height of a capital letter; the width follows the text.");
                Row("Bold", Check("bold letters", () => o.Shape.Bold, v =>
                    {
                        string t = o.Shape.Text;
                        double before = Beatform.Core.TextFont.Width(t, o.Shape.Bold);
                        o.Shape.Bold = v;
                        double r = Beatform.Core.TextFont.Width(t, v) / before;
                        foreach (var k in o.Track("w")) k.V = Math.Round(k.V * r, 2);
                    }, "Toggle bold"),
                    "Montserrat Bold or Medium. The width follows the letters.");
                Buttons(Btn("Fit width to text", () => S.Edit("Fit text", () => FitTextWidth(o, null)),
                    "Makes the box exactly as wide as the letters need at the current height."));
                break;
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
        var colorKind = new OptionButton();
        colorKind.AddItem("theme", 0);
        colorKind.AddItem("player", 1);
        colorKind.AddItem("custom", 2);
        colorKind.Selected = o.Color == null ? 0 : o.Color == "@player" ? 1 : 2;
        colorKind.TooltipText = "Theme: the level's hazard colour. Player: the colour of the player it follows (or player 1).";
        colorKind.ItemSelected += i => S.Edit("Color", () => o.Color = i == 0 ? null : i == 1 ? "@player" : S.Doc.Hazard);
        colorRow.AddChild(colorKind);
        if (o.Color != null && o.Color != "@player")
        {
            var cp = ColorField(() => o.Color, v => o.Color = v, "Color", S.Doc.Hazard);
            cp.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            colorRow.AddChild(cp);
        }
        Row("Color", colorRow);
        Row("Layer", Num(() => o.Layer, v => o.Layer = (int)v, "Layer", 1, -100, 100), "Higher layers draw on top.");

        BuildImposter(o);
        BuildRandom(o);

        Section("Parent");
        var others = S.Doc.Objects.Where(x => x.Id != o.Id && !IsDescendant(x, o)).ToList();
        var ids = new[] { "", "@player", "@player2", "@player3", "@player4" }.Concat(others.Select(x => x.Id)).ToArray();
        var names = new[] { "none", "Player 1", "Player 2", "Player 3", "Player 4" }
            .Concat(others.Select(x => string.IsNullOrEmpty(x.Name) ? $"{x.Id} ({x.Shape.Type})" : $"{x.Name} ({x.Id})")).ToArray();
        Row("Follows", Options(ids, () => o.Parent ?? "", v => o.Parent = string.IsNullOrEmpty(v) ? null : v, "Set parent", names),
            "A child moves and turns with its parent; its position becomes relative to it. Following a player keeps it next to that player (in the editor, next to the stand-in), and hides it when that player is not playing.");
        Row("Pivot X", Num(() => o.PivotX, v => o.PivotX = v, "Pivot", 0.05, -2, 2), "-0.5 is the left edge, 0 the center, 0.5 the right edge.");
        Row("Pivot Y", Num(() => o.PivotY, v => o.PivotY = v, "Pivot", 0.05, -2, 2), "-0.5 is the top edge.");

        Buttons(Btn("Repeat…", () => ToolDialogs.ShowRepeat(App.Instance), "Add copies shifted in time, position, rotation or around a point."),
            Btn("Duplicate", () => App.Instance.DuplicateSelection()));

        Section("Animation at the playhead");
        box.AddChild(UiTheme.Small("Values at the playhead. Dragging on the stage or typing here writes a key at the playhead once a track has two or more keys; with one key the whole object changes. Hold R while the song plays to record a path with the mouse."));
        foreach (var track in EditObject.TrackNames) BuildTrack(o, track);
    }

    /// <summary>A [min, max] range that is off (null) while both ends are 0.</summary>
    private void RangeRow(string label, Func<double[]> get, Action<double[]> set, string undo, double step, string suffix, string tooltip)
    {
        var pair = new HBoxContainer();
        double Lo() => get()?[0] ?? 0;
        double Hi() => get()?[1] ?? 0;
        void Put(double lo, double hi) => set(lo == 0 && hi == 0 ? null : new[] { lo, hi });
        var a = Num(Lo, v => Put(v, Hi()), undo, step, -100000, 100000, suffix);
        var b = Num(Hi, v => Put(Lo(), v), undo, step, -100000, 100000, suffix);
        a.SizeFlagsHorizontal = b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pair.AddChild(a);
        pair.AddChild(b);
        Row(label, pair, tooltip);
    }

    private static string IntList(List<int> l) => string.Join(", ", l);

    private static List<int> ParseInts(string s)
    {
        var list = new List<int>();
        foreach (var part in (s ?? "").Split(',', ' ', ';'))
            if (int.TryParse(part, out int v)) list.Add(v);
        return list;
    }

    private void BuildRandom(EditObject o)
    {
        Section("Random each play");
        box.AddChild(UiTheme.Small("Picked again on every attempt in the game. The stage shows one roll; Roll again shows another."));
        RangeRow("Offset x", () => o.RandomX, v => o.RandomX = v, "Random x", 1, "", "Added to every x key: a random amount between these two (both 0: off).");
        RangeRow("Offset y", () => o.RandomY, v => o.RandomY = v, "Random y", 1, "", "Added to every y key: a random amount between these two (both 0: off).");
        RangeRow("Turn", () => o.RandomRot, v => o.RandomRot = v, "Random rotation", 1, "°", "Added to every rotation key. On a parent it turns its children around it.");
        Row("Same as", Text(() => o.RandomSeed, v => o.RandomSeed = string.IsNullOrWhiteSpace(v) ? null : v.Trim(), "Random seed"),
            "Objects with the same name here get the same random amounts (empty: this object's own).");
        for (int i = 0; i < o.Choices.Count; i++)
        {
            var c = o.Choices[i];
            int index = i;
            Row("Only when", Text(() => c.Group, v => c.Group = v, "Choice group"), "A group rolls one number from 0 to count - 1 per play; every object of the group sees the same roll.");
            Row("  of", Num(() => c.Count, v => c.Count = (int)Math.Clamp(v, 1, 1000), "Choice count", 1, 1, 1000, " rolls"));
            Row("  rolled", Text(() => IntList(c.Show), v => c.Show = ParseInts(v), "Choice show"), "Exists only on these rolls (empty: any).");
            Row("  not", Text(() => IntList(c.Hide), v => c.Hide = ParseInts(v), "Choice hide"), "Never exists on these rolls.");
            Buttons(Btn("Remove condition", () => S.Edit("Remove condition", () => o.Choices.RemoveAt(index))));
        }
        Buttons(Btn("Add condition", () => S.Edit("Add condition", () => o.Choices.Add(new ChoiceSpec())),
                "Make the object exist only on some rolls of a group, for example one of four shapes."),
            Btn("Roll again", () => S.Reroll(), "Preview another random roll of the whole level."));
    }

    private void BuildImposter(EditObject o)
    {
        Section("Moves like a player");
        Row("Imposter", Check("cruises, dashes and leaves a trail", () => o.Imposter != null,
            v => o.Imposter = v ? new ImposterSpec() : null, "Toggle imposter"),
            "Instead of following its x/y keys, it starts at its position and moves like a player. Chasers hunt the nearest player (in the editor, the stand-in).");
        var m = o.Imposter;
        if (m == null) return;
        Row("Mode", Options(new[] { "chase", "straight" }, () => m.Mode, v => { m.Mode = v; m.Bounds = v == "chase"; }, "Imposter mode",
            new[] { "Chase the nearest player", "Straight line" }));
        if (m.Mode == "straight")
            Row("Heading", Num(() => m.Angle, v => m.Angle = v, "Heading", 1, -360, 360, "°"), "0 is right, 90 down, 180 left.");
        Row("Speed", Num(() => m.Speed, v => m.Speed = Math.Max(0, v), "Speed", 5, 0, 5000, " /s"), "Cruising speed. A player moves at about 380.");
        Row("Dash speed", Num(() => m.DashSpeed, v => m.DashSpeed = Math.Max(0, v), "Dash speed", 10, 0, 10000, " /s"));
        Row("Dash on", Options(new[] { "time", "beat" }, () => m.DashBeats > 0 ? "beat" : "time",
            v => m.DashBeats = v == "beat" ? (m.DashBeats > 0 ? m.DashBeats : 2) : 0, "Dash timing",
            new[] { "A timer", "The beat" }), "On the beat, dashes land on the level's beats (BPM and offset in the level settings).");
        if (m.DashBeats > 0)
            Row("Dash every", Num(() => m.DashBeats, v => m.DashBeats = Math.Clamp(v, 0.25, 64), "Dash beats", 0.25, 0.25, 64, " beats"),
                $"One dash every this many beats, at {S.Doc.Bpm:0.##} BPM.");
        else
            Row("Dash every", Num(() => m.DashEvery, v => m.DashEvery = Math.Max(16, v), "Dash interval", 10, 16, 100000, " ms"), "Cruising time between dashes.");
        Row("Dash length", Num(() => m.DashTime, v => m.DashTime = Math.Max(0, v), "Dash length", 10, 0, 10000, " ms"));
        Row("First dash", Num(() => m.FirstDash, v => m.FirstDash = v, "First dash", 10, -1, 100000, " ms"), m.DashBeats > 0 ? "The first dash is on the first beat after this long (-1 or 0: the first beat after it appears)."
            : "Cruising time before the first dash. 0 dashes right away, -1 uses Dash every.");
        Row("Stop at", Num(() => m.StopAt, v => m.StopAt = v, "Stop at", 10, -1, 1000000, " ms"), "After this long it brakes and waits (-1: never).");
        if (m.StopAt >= 0) Row("Stop for", Num(() => m.StopFor, v => m.StopFor = Math.Max(0, v), "Stop for", 10, 0, 100000, " ms"));
        Row("Leave at", Num(() => m.LeaveAt, v => m.LeaveAt = v, "Leave at", 10, -1, 1000000, " ms"), "After this long it dashes away and stops hurting (-1: never).");
        Row("Stay on screen", Check("bounce off the edges", () => m.Bounds, v => m.Bounds = v, "Stay on screen"));
        Row("Stretch", Num(() => m.StretchMax, v => m.StretchMax = Math.Clamp(v, 0, 2), "Stretch", 0.05, 0, 2), "How much speed stretches it.");
        Row("Effects", Check("trail, dash rings, shadow", () => m.Effects, v => m.Effects = v, "Imposter effects"));
        Row("Dash mark", Options(new[] { "ring", "disk" }, () => m.DashPulse, v => m.DashPulse = v, "Dash mark",
            new[] { "Spreading ring", "Disk that hollows" }));
        box.AddChild(UiTheme.Small("Times count from the end of its warning. Drag the dashed \"player\" square on the stage to preview chasing."));
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
        Row("Type", Options(EditEvent.Types, () => e.Type, v => e.Type = v, "Event type",
            new[] { "Camera shake", "Screen flash", "Background color", "Camera sway", "Game sound", "Player speed", "Title card" }));
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
            case "sway":
                box.AddChild(UiTheme.Small("From this time on the whole scene sways gently until the next sway event. Duration blends from the previous sway. 0 everywhere turns it off."));
                Row("Rotation", Num(() => e.SwayRotation, v => e.SwayRotation = v, "Sway rotation", 0.05, -45, 45, "°"));
                Row("Sideways", Num(() => e.SwayX, v => e.SwayX = v, "Sway x", 0.5, -200, 200));
                Row("Up/down", Num(() => e.SwayY, v => e.SwayY = v, "Sway y", 0.5, -200, 200));
                Row("Period", Num(() => e.Period, v => e.Period = Math.Max(100, v), "Sway period", 10, 100, 100000, " ms"), "Time for one full swing of the rotation.");
                Row("Period x", Num(() => e.PeriodX, v => e.PeriodX = Math.Max(100, v), "Sway period x", 10, 100, 100000, " ms"));
                Row("Period y", Num(() => e.PeriodY, v => e.PeriodY = Math.Max(100, v), "Sway period y", 10, 100, 100000, " ms"));
                Row("Ease", Options(Eases, () => e.Ease, v => e.Ease = v, "Ease"));
                break;
            case "sound":
                box.AddChild(UiTheme.Small("Plays one of the game's own sounds in the game. The editor cannot play them."));
                Row("Sound", Options(EditEvent.Sounds, () => e.Sound, v => e.Sound = v, "Sound",
                    new[] { "Player death", "Player hit", "Player respawn", "Explosion" }));
                Row("Volume", Num(() => e.Volume, v => e.Volume = Math.Clamp(v, 0, 1), "Sound volume", 0.05, 0, 1));
                break;
            case "credits":
                box.AddChild(UiTheme.Small("The game's own title card (the song's title and artist from the level settings) shows at this time instead of at the start. Only in the game."));
                break;
            case "playerSpeed":
                box.AddChild(UiTheme.Small("From this time on the players move at this part of their normal speed (dashes too), until the next player speed event. Near 0 they are rooted but still turn in place. Duration blends from the previous speed. Only in the game."));
                Row("Speed", Num(() => e.Speed, v => e.Speed = Math.Clamp(v, 0, 2), "Player speed", 0.01, 0, 2), "1 is normal, 0.5 half speed, 0 rooted.");
                Row("Turning", Num(() => e.Turn, v => e.Turn = Math.Clamp(v, 0.001, 1), "Player turning", 0.05, 0.001, 1), "The part of the way to their direction the players turn each frame. 1 is at once.");
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
