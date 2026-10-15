using System;
using System.Collections.Generic;
using System.Linq;
using Sonokinesis.Editor.Model;
using Godot;

namespace Sonokinesis.Editor.UI;

/// <summary>Repeat (array copies with steps) and Stretch (re-time) dialogs for the selection.</summary>
public static class ToolDialogs
{
    private static EditorState S => EditorState.I;

    private sealed class Field
    {
        public string Label, Tooltip;
        public double Value, Min, Max, Step;
    }

    private static Dictionary<string, Field> repeatFields;

    public static void ShowRepeat(App app)
    {
        var sel = S.SelectedObjects.ToList();
        if (sel.Count == 0) { app.Status("Select objects to repeat first.", true); return; }
        repeatFields ??= new Dictionary<string, Field>
        {
            ["count"] = new() { Label = "Copies", Value = 4, Min = 1, Max = 256, Step = 1, Tooltip = "How many copies to add" },
            ["beats"] = new() { Label = "Time step (beats)", Value = 1, Min = -64, Max = 64, Step = 0.0625 },
            ["dx"] = new() { Label = "Move X each", Value = 0, Min = -2000, Max = 2000, Step = 1 },
            ["dy"] = new() { Label = "Move Y each", Value = 0, Min = -2000, Max = 2000, Step = 1 },
            ["rot"] = new() { Label = "Turn each (°)", Value = 0, Min = -360, Max = 360, Step = 1, Tooltip = "Added to the copies' own rotation" },
            ["orbit"] = new() { Label = "Orbit each (°)", Value = 0, Min = -360, Max = 360, Step = 1, Tooltip = "Rotates copies around the orbit center: makes circles and spirals" },
            ["cx"] = new() { Label = "Orbit center X", Value = 0, Min = -2000, Max = 2000, Step = 1 },
            ["cy"] = new() { Label = "Orbit center Y", Value = 0, Min = -2000, Max = 2000, Step = 1 },
            ["scale"] = new() { Label = "Scale each (×)", Value = 1, Min = 0.05, Max = 5, Step = 0.01 },
        };
        var dlg = new ConfirmationDialog { Title = $"Repeat {sel.Count} object(s)", OkButtonText = "Add copies", MinSize = new Vector2I(420, 0) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(UiTheme.Small("Adds copies of the selection, each one shifted in time and changed by the steps below. Copy n gets n × each step.", UiTheme.Text));
        foreach (var (key, f) in repeatFields)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = f.Label, CustomMinimumSize = new Vector2(160, 0), TooltipText = f.Tooltip ?? "" });
            var sb = new SpinBox { MinValue = f.Min, MaxValue = f.Max, Step = f.Step, Value = f.Value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SelectAllOnFocus = true };
            sb.ValueChanged += v => f.Value = v;
            row.AddChild(sb);
            box.AddChild(row);
        }
        dlg.AddChild(box);
        dlg.Confirmed += () => { Repeat(app, sel); dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        app.AddChild(dlg);
        UiTheme.PopupFitted(dlg);
    }

    private static void Repeat(App app, List<EditObject> sel)
    {
        double V(string k) => repeatFields[k].Value;
        int count = (int)V("count");
        var added = new List<string>();
        S.Edit("Repeat", () =>
        {
            for (int n = 1; n <= count; n++)
            {
                var map = app.DuplicateObjects(sel, n * V("beats") * S.Doc.BeatMs);
                foreach (var id in map.Values)
                {
                    var o = S.Doc.Find(id);
                    added.Add(id);
                    bool child = o.Parent != null && map.ContainsValue(o.Parent);
                    if (child) continue; // children follow their copied parent
                    foreach (var k in o.Track("x")) k.V += n * V("dx");
                    foreach (var k in o.Track("y")) k.V += n * V("dy");
                    if (o.Track("rot").Count == 0) o.Track("rot").Add(new EditKey { T = 0, V = 0 });
                    foreach (var k in o.Track("rot")) k.V += n * (V("rot") + V("orbit"));
                    double s = Math.Pow(V("scale"), n);
                    foreach (var name in new[] { "w", "h" })
                    {
                        if (o.Track(name).Count == 0) o.Track(name).Add(new EditKey { T = 0, V = 100 });
                        foreach (var k in o.Track(name)) k.V *= s;
                    }
                    if (Math.Abs(V("orbit")) > 1e-9) Orbit(o, n * V("orbit"), V("cx"), V("cy"));
                }
            }
        });
        S.Select(sel.Select(o => o.Id).Concat(added));
        app.Status($"Added {count} cop{(count == 1 ? "y" : "ies")}.");
    }

    /// <summary>Rotates an object's whole path around a point (both keyframe tracks must line up in time).</summary>
    private static void Orbit(EditObject o, double deg, double cx, double cy)
    {
        var times = o.Track("x").Select(k => k.T).Concat(o.Track("y").Select(k => k.T)).Distinct().OrderBy(t => t).ToList();
        if (times.Count == 0) times.Add(0);
        var pts = times.Select(t => (t, x: CanvasView.Eval(o, "x", t), y: CanvasView.Eval(o, "y", t))).ToList();
        var easeX = times.Select(t => o.Track("x").FirstOrDefault(k => Math.Abs(k.T - t) < 0.5)?.Ease ?? "linear").ToList();
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        o.Track("x").Clear();
        o.Track("y").Clear();
        for (int i = 0; i < pts.Count; i++)
        {
            double dx = pts[i].x - cx, dy = pts[i].y - cy;
            o.Track("x").Add(new EditKey { T = pts[i].t, V = Math.Round(cx + c * dx - s * dy, 3), Ease = easeX[i] });
            o.Track("y").Add(new EditKey { T = pts[i].t, V = Math.Round(cy + s * dx + c * dy, 3), Ease = easeX[i] });
        }
    }

    public static void ShowStretch(App app)
    {
        var sel = S.SelectedObjects.ToList();
        if (sel.Count == 0) { app.Status("Select objects to stretch first.", true); return; }
        double factor = 1;
        var dlg = new ConfirmationDialog { Title = "Stretch time", OkButtonText = "Stretch", MinSize = new Vector2I(380, 0) };
        var box = new VBoxContainer();
        box.AddChild(UiTheme.Small("Speeds the selection up or slows it down. Everything is scaled around the earliest start: times, warnings, fades and keyframes.", UiTheme.Text));
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Factor", CustomMinimumSize = new Vector2(120, 0) });
        var sb = new SpinBox { MinValue = 0.05, MaxValue = 20, Step = 0.05, Value = 1, Suffix = "×", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        sb.ValueChanged += v => factor = v;
        row.AddChild(sb);
        box.AddChild(row);
        box.AddChild(UiTheme.Small("2× doubles every length; 0.5× halves them."));
        dlg.AddChild(box);
        dlg.Confirmed += () =>
        {
            double origin = sel.Min(o => o.Start);
            S.Edit("Stretch time", () =>
            {
                foreach (var o in sel)
                {
                    o.Start = origin + (o.Start - origin) * factor;
                    o.End = origin + (o.End - origin) * factor;
                    o.Warn *= factor;
                    o.Outro *= factor;
                    foreach (var keys in o.Tracks.Values) foreach (var k in keys) k.T *= factor;
                }
            });
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        app.AddChild(dlg);
        UiTheme.PopupFitted(dlg);
    }
}
