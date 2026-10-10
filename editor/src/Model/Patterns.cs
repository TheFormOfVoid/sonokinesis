using System;
using System.Collections.Generic;
using System.Linq;

namespace Beatform.Editor.Model;

/// <summary>A pattern parameter shown in the pattern dialog.</summary>
public sealed class PatternParam
{
    public string Key, Label, Tooltip;
    public double Value, Min, Max, Step = 1;
    public string[] Choices; // when set, Value is an index into Choices
}

/// <summary>
/// Pattern generators: each turns a few parameters into ordinary objects. Times are built around the
/// "hit" time (when the pattern becomes dangerous), which is the playhead when the pattern is added.
/// </summary>
public abstract class Pattern
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public readonly List<PatternParam> Params = new();

    protected double P(string key) => Params.First(p => p.Key == key).Value;
    protected PatternParam Add(string key, string label, double value, double min, double max, double step = 1, string tooltip = null)
    {
        var p = new PatternParam { Key = key, Label = label, Value = value, Min = min, Max = max, Step = step, Tooltip = tooltip };
        Params.Add(p);
        return p;
    }
    protected PatternParam Choice(string key, string label, int value, params string[] choices)
    {
        var p = new PatternParam { Key = key, Label = label, Value = value, Choices = choices, Min = 0, Max = choices.Length - 1 };
        Params.Add(p);
        return p;
    }

    /// <summary>Builds the objects. <paramref name="hit"/> is the song time the pattern becomes dangerous.</summary>
    public abstract List<EditObject> Build(LevelDocument doc, double hit, double x, double y);

    protected static EditObject Obj(LevelDocument doc, string group, string name, string shape, double start, double end, double warn = 0, double outro = 0)
    {
        return new EditObject
        {
            Id = doc.NewId(),
            Name = name,
            Group = group,
            Shape = new ShapeSpec { Type = shape },
            Start = Math.Round(start, 3),
            End = Math.Round(end, 3),
            Warn = Math.Round(warn, 3),
            Outro = Math.Round(outro, 3),
        };
    }

    protected static void K(EditObject o, string track, params (double t, double v, string ease)[] keys)
    {
        var list = o.Track(track);
        list.Clear();
        foreach (var (t, v, e) in keys) list.Add(new EditKey { T = Math.Round(t, 3), V = Math.Round(v, 3), Ease = e ?? "linear" });
    }

    protected static void K(EditObject o, string track, double v) => K(o, track, (0, v, null));

    public static string NewGroupId(LevelDocument doc, string prefix)
    {
        int n = 1;
        while (doc.Objects.Any(o => o.Group == prefix + n)) n++;
        return prefix + n;
    }

    public static readonly Func<Pattern>[] All =
    {
        () => new BurstPattern(), () => new ShockwavePattern(), () => new WallPattern(), () => new LaserPattern(),
        () => new SpinnerPattern(), () => new RainPattern(), () => new SpiralPattern(), () => new BouncerPattern(), () => new PulsePattern(),
    };
}

public sealed class BurstPattern : Pattern
{
    public override string Id => "burst";
    public override string Name => "Burst";
    public override string Description => "A bomb grows during its warning, then splits into projectiles flying outward.";

    public BurstPattern()
    {
        Add("count", "Projectiles", 10, 2, 64);
        Add("speed", "Speed", 520, 50, 2000, 10, "Units per second");
        Add("size", "Projectile size", 26, 4, 200);
        Add("bomb", "Bomb size", 70, 10, 400);
        Add("warnBeats", "Warning (beats)", 1, 0, 8, 0.25);
        Add("travel", "Flight time (ms)", 1600, 200, 8000, 50);
        Add("spin", "Angle offset (°)", 0, -360, 360, 1);
    }

    public override List<EditObject> Build(LevelDocument doc, double hit, double x, double y)
    {
        var g = NewGroupId(doc, "burst");
        var res = new List<EditObject>();
        double warn = P("warnBeats") * doc.BeatMs, bomb = P("bomb");
        var b = Obj(doc, g, "burst bomb", "circle", hit - warn, hit + 120, warn, 120);
        K(b, "x", x); K(b, "y", y);
        K(b, "w", (0, bomb * 0.4, null), (warn, bomb, "inBack"));
        K(b, "h", (0, bomb * 0.4, null), (warn, bomb, "inBack"));
        res.Add(b);
        int n = (int)P("count");
        double travel = P("travel"), dist = P("speed") * travel / 1000, size = P("size");
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n + P("spin") * Math.PI / 180;
            var s = Obj(doc, g, "burst shot", "circle", hit, hit + travel, 0, 200);
            K(s, "x", (0, x, null), (travel, x + Math.Cos(a) * dist, "outSine"));
            K(s, "y", (0, y, null), (travel, y + Math.Sin(a) * dist, "outSine"));
            K(s, "w", size); K(s, "h", size);
            res.Add(s);
        }
        return res;
    }
}

public sealed class ShockwavePattern : Pattern
{
    public override string Id => "shockwave";
    public override string Name => "Shockwave";
    public override string Description => "A ring that expands from a point. Its hole is safe.";

    public ShockwavePattern()
    {
        Add("max", "Final size", 900, 50, 3000, 10);
        Add("duration", "Expand time (ms)", 1400, 100, 10000, 50);
        Add("thickness", "Thickness", 0.06, 0.02, 0.5, 0.01);
        Add("warnBeats", "Warning (beats)", 1, 0, 8, 0.25);
    }

    public override List<EditObject> Build(LevelDocument doc, double hit, double x, double y)
    {
        var g = NewGroupId(doc, "wave");
        double warn = P("warnBeats") * doc.BeatMs, dur = P("duration"), max = P("max");
        var r = Obj(doc, g, "shockwave", "ring", hit - warn, hit + dur, warn, 250);
        r.Shape.Thickness = P("thickness");
        K(r, "x", x); K(r, "y", y);
        K(r, "w", (0, 60, null), (warn, 60, null), (warn + dur, max, "outQuad"));
        K(r, "h", (0, 60, null), (warn, 60, null), (warn + dur, max, "outQuad"));
        return new List<EditObject> { r };
    }
}

public sealed class WallPattern : Pattern
{
    public override string Id => "wall";
    public override string Name => "Wall with gap";
    public override string Description => "A wall sweeps across the screen. Slip through the gap.";

    public WallPattern()
    {
        Choice("dir", "Direction", 0, "left → right", "right → left", "top → bottom", "bottom → top");
        Add("gap", "Gap position", 0, -300, 300, 5, "Across the wall, from the center");
        Add("gapSize", "Gap size", 170, 40, 600, 5);
        Add("thickness", "Thickness", 60, 10, 400);
        Add("beats", "Sweep (beats)", 4, 0.5, 32, 0.25);
        Add("warnBeats", "Warning (beats)", 2, 0, 8, 0.25);
    }

    public override List<EditObject> Build(LevelDocument doc, double hit, double x, double y)
    {
        var g = NewGroupId(doc, "wall");
        int dir = (int)P("dir");
        bool horizontalMove = dir < 2;
        double warn = P("warnBeats") * doc.BeatMs, dur = P("beats") * doc.BeatMs, gap = P("gap"), gapSize = P("gapSize"), th = P("thickness");
        double extent = horizontalMove ? 360 : 640;
        double from = horizontalMove ? (dir == 0 ? -700 : 700) : (dir == 2 ? -420 : 420);
        double edge = from * 0.92;
        string move = horizontalMove ? "x" : "y", across = horizontalMove ? "y" : "x";
        string thick = horizontalMove ? "w" : "h", length = horizontalMove ? "h" : "w";
        var res = new List<EditObject>();
        // One piece from the near edge to the gap, one from the gap to the far edge.
        foreach (var (lo, hi) in new[] { (-extent, gap - gapSize / 2), (gap + gapSize / 2, extent) })
        {
            if (hi - lo <= 1) continue;
            var o = Obj(doc, g, "wall", "rect", hit - warn, hit + dur, warn, 100);
            K(o, move, (0, edge, null), (warn, edge, null), (warn + dur, -from, "inOutSine"));
            K(o, across, (lo + hi) / 2);
            K(o, thick, th);
            K(o, length, hi - lo);
            res.Add(o);
        }
        return res;
    }
}

public sealed class LaserPattern : Pattern
{
    public override string Id => "laser";
    public override string Name => "Laser";
    public override string Description => "A thin warning line, then a full-width beam through the drop point.";

    public LaserPattern()
    {
        Choice("dir", "Orientation", 0, "horizontal", "vertical", "diagonal ↘", "diagonal ↗");
        Add("thickness", "Thickness", 70, 6, 600);
        Add("beats", "Beam time (beats)", 2, 0.25, 32, 0.25);
        Add("warnBeats", "Warning (beats)", 2, 0, 8, 0.25);
    }

    public override List<EditObject> Build(LevelDocument doc, double hit, double x, double y)
    {
        var g = NewGroupId(doc, "laser");
        int dir = (int)P("dir");
        double warn = P("warnBeats") * doc.BeatMs, dur = P("beats") * doc.BeatMs, th = P("thickness");
        var o = Obj(doc, g, "laser", "rect", hit - warn, hit + dur, warn, 180);
        double rot = dir switch { 0 => 0, 1 => 90, 2 => 30, _ => -30 };
        K(o, "x", x); K(o, "y", y); K(o, "rot", rot);
        K(o, "w", 2800);
        K(o, "h", (0, 4, null), (warn, 4, null), (warn + 90, th, "outExpo"));
        return new List<EditObject> { o };
    }
}

public sealed class SpinnerPattern : Pattern
{
    public override string Id => "spinner";
    public override string Name => "Spinner";
    public override string Description => "A spiked hub with arms that turn around it. Arms are children of the hub.";

    public SpinnerPattern()
    {
        Add("arms", "Arms", 3, 1, 12);
        Add("length", "Arm length", 330, 20, 1500, 5);
        Add("speed", "Speed (°/s)", 150, -1440, 1440, 5);
        Add("beats", "Lasts (beats)", 16, 1, 256, 1);
        Add("warnBeats", "Warning (beats)", 2, 0, 8, 0.25);
    }

    public override List<EditObject> Build(LevelDocument doc, double hit, double x, double y)
    {
        var g = NewGroupId(doc, "spinner");
        double warn = P("warnBeats") * doc.BeatMs, dur = P("beats") * doc.BeatMs, speed = P("speed"), len = P("length");
        var res = new List<EditObject>();
        var hub = Obj(doc, g, "spinner hub", "spikes", hit - warn, hit + dur, warn, 300);
        hub.Shape.Spikes = 10; hub.Shape.Inner = 0.7; hub.Layer = 2;
        K(hub, "x", x); K(hub, "y", y); K(hub, "w", 70); K(hub, "h", 70);
        K(hub, "rot", (0, 0, null), (warn + dur, speed * (warn + dur) / 1000, null));
        res.Add(hub);
        int arms = (int)P("arms");
        double beat = doc.BeatMs;
        for (int i = 0; i < arms; i++)
        {
            var a = Obj(doc, g, "spinner arm", "rect", hit - Math.Min(beat, warn), hit + dur, Math.Min(beat, warn), 300);
            a.Parent = hub.Id; a.PivotX = -0.5; a.Layer = 1;
            K(a, "rot", 360.0 * i / arms);
            K(a, "w", (0, 0, null), (Math.Max(1, Math.Min(beat, warn)), len, "outBack"));
            K(a, "h", 22);
            res.Add(a);
        }
        return res;
    }
}

public sealed class RainPattern : Pattern
{
    public override string Id => "rain";
    public override string Name => "Rain";
    public override string Description => "Shapes fall from the top on the beat, at random positions.";

    public RainPattern()
    {
        Add("count", "Drops", 8, 1, 128);
        Add("interval", "Every (beats)", 0.5, 0.0625, 8, 0.0625);
        Choice("shape", "Shape", 0, "square", "star", "triangle", "circle", "spike ball");
        Add("size", "Size", 46, 6, 300);
        Add("fall", "Fall time (ms)", 1500, 200, 8000, 50);
        Add("seed", "Random seed", 1, 0, 9999);
    }

    public override List<EditObject> Build(LevelDocument doc, double hit, double x, double y)
    {
        var g = NewGroupId(doc, "rain");
        var rnd = new Random((int)P("seed"));
        var res = new List<EditObject>();
        double beat = doc.BeatMs, fall = P("fall"), size = P("size");
        string[] shapes = { "rect", "star", "polygon", "circle", "spikes" };
        for (int i = 0; i < (int)P("count"); i++)
        {
            double t = hit + i * P("interval") * beat;
            double px = rnd.NextDouble() * 1160 - 580;
            var o = Obj(doc, g, "rain drop", shapes[(int)P("shape")], t - beat, t + fall, beat, 150);
            if (o.Shape.Type == "star") o.Shape.Inner = 0.45;
            K(o, "x", (0, px, null), (beat + fall, px + rnd.NextDouble() * 160 - 80, null));
            K(o, "y", (0, -330, null), (beat, -330, null), (beat + fall, 430, "inQuad"));
            K(o, "rot", (beat, 0, null), (beat + fall, rnd.Next(2) == 0 ? -270 : 270, null));
            K(o, "w", size); K(o, "h", size);
            res.Add(o);
        }
        return res;
    }
}

public sealed class SpiralPattern : Pattern
{
    public override string Id => "spiral";
    public override string Name => "Spiral";
    public override string Description => "A turret fires projectiles one after another while turning.";

    public SpiralPattern()
    {
        Add("count", "Shots", 24, 2, 256);
        Add("interval", "Every (ms)", 80, 10, 2000, 5);
        Add("turn", "Turn per shot (°)", 23, -180, 180, 1);
        Add("arms", "Streams", 2, 1, 8);
        Add("speed", "Speed", 420, 50, 2000, 10);
        Add("size", "Shot size", 22, 4, 200);
    }

    public override List<EditObject> Build(LevelDocument doc, double hit, double x, double y)
    {
        var g = NewGroupId(doc, "spiral");
        var res = new List<EditObject>();
        int count = (int)P("count"), arms = (int)P("arms");
        double interval = P("interval"), travel = 2200, dist = P("speed") * travel / 1000, size = P("size");
        var core = Obj(doc, g, "spiral core", "polygon", hit - doc.BeatMs, hit + count * interval + 200, doc.BeatMs, 200);
        core.Shape.Sides = 6; core.Hitbox = true;
        K(core, "x", x); K(core, "y", y); K(core, "w", 54); K(core, "h", 54);
        K(core, "rot", (0, 0, null), (doc.BeatMs + count * interval, P("turn") * count, null));
        res.Add(core);
        for (int i = 0; i < count; i++)
            for (int a = 0; a < arms; a++)
            {
                double ang = (P("turn") * i + 360.0 * a / arms) * Math.PI / 180;
                var s = Obj(doc, g, "spiral shot", "circle", hit + i * interval, hit + i * interval + travel, 0, 200);
                K(s, "x", (0, x, null), (travel, x + Math.Cos(ang) * dist, null));
                K(s, "y", (0, y, null), (travel, y + Math.Sin(ang) * dist, null));
                K(s, "w", size); K(s, "h", size);
                res.Add(s);
            }
        return res;
    }
}

public sealed class BouncerPattern : Pattern
{
    public override string Id => "bouncer";
    public override string Name => "Bouncer";
    public override string Description => "A spike ball bounces across the screen on the beat.";

    public BouncerPattern()
    {
        Choice("dir", "From", 0, "left", "right");
        Add("bounces", "Bounces", 4, 1, 32);
        Add("beatsPer", "Beats per bounce", 1, 0.25, 8, 0.25);
        Add("height", "Bounce height", 370, 20, 700, 5);
        Add("size", "Size", 90, 10, 400);
    }

    public override List<EditObject> Build(LevelDocument doc, double hit, double x, double y)
    {
        var g = NewGroupId(doc, "bouncer");
        double beat = doc.BeatMs, per = P("beatsPer") * beat, n = P("bounces"), dur = per * n, h = P("height");
        double side = P("dir") == 0 ? -1 : 1;
        var o = Obj(doc, g, "bouncer", "spikes", hit - beat, hit + dur, beat, 200);
        o.Shape.Spikes = 12; o.Shape.Inner = 0.72; o.Layer = 1;
        K(o, "x", (0, side * 700, null), (beat, side * 700, null), (beat + dur, -side * 700, null));
        var ys = new List<(double, double, string)> { (0, 250, null), (beat, 250, null) };
        for (int i = 0; i < n; i++)
        {
            ys.Add((beat + per * (i + 0.5), 250 - h, "outQuad"));
            ys.Add((beat + per * (i + 1), 250, "inQuad"));
        }
        K(o, "y", ys.ToArray());
        K(o, "rot", (0, 0, null), (beat + dur, -side * 180 * n, null));
        K(o, "w", P("size")); K(o, "h", P("size"));
        return new List<EditObject> { o };
    }
}

public sealed class PulsePattern : Pattern
{
    public override string Id => "pulse";
    public override string Name => "Beat pulse";
    public override string Description => "Decorative rings that pulse on every beat. They do not hurt.";

    public PulsePattern()
    {
        Add("beats", "Beats", 8, 1, 256);
        Add("size", "Size", 260, 20, 1500, 5);
        Add("accent", "Accent every", 4, 1, 16, 1, "Bigger pulse every N beats");
    }

    public override List<EditObject> Build(LevelDocument doc, double hit, double x, double y)
    {
        var g = NewGroupId(doc, "pulse");
        var res = new List<EditObject>();
        double beat = doc.BeatMs, size = P("size");
        for (int i = 0; i < (int)P("beats"); i++)
        {
            double t = hit + i * beat;
            double s = i % (int)P("accent") == 0 ? size * 1.4 : size;
            var o = Obj(doc, g, "pulse", "ring", t, t + beat, 0, beat * 0.8);
            o.Shape.Thickness = 0.04; o.Hitbox = false; o.Color = "#33e5ff";
            K(o, "x", x); K(o, "y", y);
            K(o, "w", (0, s * 0.6, null), (beat, s, "outCubic"));
            K(o, "h", (0, s * 0.6, null), (beat, s, "outCubic"));
            K(o, "opacity", 0.5);
            res.Add(o);
        }
        return res;
    }
}
