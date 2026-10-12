using System;
using System.Collections.Generic;
using System.Linq;
using Beatform.Editor.Model;
using Godot;

namespace Beatform.Editor.UI;

/// <summary>
/// Things to add: plain shapes, patterns and scene items. Click to add at the playhead in the middle
/// of the stage (patterns open their settings first), or drag onto the stage to place them.
/// </summary>
public partial class PalettePanel : PanelContainer
{
    private static EditorState S => EditorState.I;
    private readonly Dictionary<string, Pattern> patterns = new();

    private static readonly (string id, string label, string tip)[] Shapes =
    {
        ("rect", "■  Square", "Rectangle"),
        ("circle", "●  Circle", "Circle"),
        ("tri", "▲  Triangle", "Three-sided polygon"),
        ("hex", "⬢  Hexagon", "Six-sided polygon"),
        ("star", "★  Star", "Five-pointed star"),
        ("ring", "◯  Ring", "Ring with a safe hole"),
        ("spikes", "✹  Spike ball", "Round spiky ball"),
        ("text", "A  Text", "Words drawn with Beatform's stroke font"),
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

        box.AddChild(new HSeparator());
        box.AddChild(UiTheme.Heading("Patterns"));
        foreach (var make in Pattern.All)
        {
            var p = make();
            patterns[p.Id] = p;
            box.AddChild(Item("pattern:" + p.Id, "✦  " + p.Name, p.Description + " Click to set it up, or drag onto the stage to use the last settings."));
        }

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
            var p = patterns[id[8..]];
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
        if (id.StartsWith("pattern:")) { AddPattern(patterns[id[8..]], x, y); return; }
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
        List<EditObject> objs = null;
        S.Edit("Add " + p.Name, () =>
        {
            objs = p.Build(S.Doc, S.Playhead, Math.Round(x), Math.Round(y));
            S.Doc.Objects.AddRange(objs);
        });
        if (objs != null) S.Select(objs.Select(o => o.Id));
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
    public static void Show(Node parent, Pattern p, Action onAdd)
    {
        var dlg = new ConfirmationDialog { Title = p.Name, OkButtonText = "Add at playhead", MinSize = new Vector2I(420, 0) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(UiTheme.Small(p.Description, UiTheme.Text));
        foreach (var param in p.Params)
        {
            var row = new HBoxContainer();
            var l = new Label { Text = param.Label, CustomMinimumSize = new Vector2(150, 0), TooltipText = param.Tooltip ?? "" };
            row.AddChild(l);
            if (param.Choices != null)
            {
                var ob = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                foreach (var c in param.Choices) ob.AddItem(c);
                ob.Selected = (int)param.Value;
                ob.ItemSelected += i => param.Value = i;
                row.AddChild(ob);
            }
            else
            {
                var sb = new SpinBox { MinValue = param.Min, MaxValue = param.Max, Step = param.Step, Value = param.Value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SelectAllOnFocus = true };
                sb.ValueChanged += v => param.Value = v;
                row.AddChild(sb);
            }
            box.AddChild(row);
        }
        box.AddChild(UiTheme.Small("It becomes dangerous at the playhead. Drag the pattern from the palette onto the stage to place it somewhere else with these settings."));
        dlg.AddChild(box);
        dlg.Confirmed += () => { onAdd(); dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        parent.AddChild(dlg);
        dlg.PopupCentered();
    }
}
