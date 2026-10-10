using System;
using System.Collections.Generic;

namespace Beatform.Core;

public enum Phase { Absent, Warning, Active, Outro }

/// <summary>One object as it should be drawn at a given song time.</summary>
public struct DrawItem
{
    public LevelObject Object;
    public ShapeGeometry Geometry;
    /// <summary>Maps unit-space shape points to world (game) space.</summary>
    public Affine Transform;
    public Rgba Color;
    public Phase Phase;
}

/// <summary>
/// Pure evaluation of a level at a song time: what to draw, what can hurt, scene colors.
/// Shared by the in-game loader and the editor reference tests; contains no engine code.
/// </summary>
public sealed class LevelEvaluator
{
    public const double BinMs = 1000;
    public const double ActivationFlashMs = 120;
    public const double WarningPulseMs = 240;
    public const int MaxParentDepth = 16;

    public readonly LevelData Level;
    private readonly List<int>[] bins;
    private readonly double binOrigin;
    private readonly Rgba[] backgroundStartColors;
    private readonly List<LevelEvent> backgroundEvents = new();

    public LevelEvaluator(LevelData level)
    {
        Level = level;
        double minStart = 0, maxEnd = 0;
        foreach (var o in level.Objects) { minStart = Math.Min(minStart, o.Start); maxEnd = Math.Max(maxEnd, o.End); }
        binOrigin = Math.Floor(minStart / BinMs) * BinMs;
        int count = Math.Max(1, (int)Math.Ceiling((maxEnd - binOrigin) / BinMs) + 1);
        bins = new List<int>[count];
        for (int i = 0; i < level.Objects.Count; i++)
        {
            var o = level.Objects[i];
            int b0 = BinOf(o.Start), b1 = BinOf(o.End);
            for (int b = b0; b <= b1 && b < count; b++) (bins[b] ??= new List<int>()).Add(i);
        }

        foreach (var e in level.Events) if (e.Type == EventType.Background) backgroundEvents.Add(e);
        backgroundStartColors = new Rgba[backgroundEvents.Count];
        var cur = level.Background;
        for (int i = 0; i < backgroundEvents.Count; i++)
        {
            if (i > 0) cur = BackgroundTransition(backgroundStartColors[i - 1], backgroundEvents[i - 1], backgroundEvents[i].T);
            backgroundStartColors[i] = cur;
        }
    }

    private int BinOf(double t) => Math.Max(0, (int)Math.Floor((t - binOrigin) / BinMs));

    public static Phase PhaseAt(LevelObject o, double t)
    {
        if (t < o.Start || t >= o.End) return Phase.Absent;
        if (t < o.ActiveStart) return Phase.Warning;
        if (t < o.ActiveEnd) return Phase.Active;
        return Phase.Outro;
    }

    /// <summary>Unit-to-world transform of an object at song time t, including its parents.</summary>
    public static Affine TransformAt(LevelObject o, double t)
    {
        double lt = t - o.Start;
        double x = o.X.Evaluate(lt), y = o.Y.Evaluate(lt), rot = o.Rot.Evaluate(lt);
        double w = o.W.Evaluate(lt), h = o.H.Evaluate(lt);
        double r = rot * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        double lx = -o.PivotX * w, ly = -o.PivotY * h;
        var m = new Affine
        {
            A = cos * w,
            B = sin * w,
            C = -sin * h,
            D = cos * h,
            Tx = x + cos * lx - sin * ly,
            Ty = y + sin * lx + cos * ly,
        };
        var p = o.Parent;
        for (int depth = 0; p != null && depth < MaxParentDepth; depth++, p = p.Parent)
        {
            double plt = t - p.Start;
            m = m.Then(Affine.RotateTranslate(p.Rot.Evaluate(plt), p.X.Evaluate(plt), p.Y.Evaluate(plt)));
        }
        return m;
    }

    public Rgba ColorAt(LevelObject o, double t, Phase phase)
    {
        double lt = t - o.Start;
        double opacity = Math.Clamp(o.Opacity.Evaluate(lt), 0, 1);
        Rgba c = o.Color;
        double alpha;
        switch (phase)
        {
            case Phase.Warning:
                alpha = 0.25 + 0.1 * Math.Sin(2 * Math.PI * lt / WarningPulseMs);
                break;
            case Phase.Active:
                alpha = 1;
                double since = t - o.ActiveStart;
                if (since < ActivationFlashMs) c = Rgba.Lerp(Level.Flash, o.Color, (float)(since / ActivationFlashMs));
                break;
            case Phase.Outro:
                alpha = o.Outro > 0 ? 1 - (t - o.ActiveEnd) / o.Outro : 0;
                break;
            default:
                alpha = 0;
                break;
        }
        c.A = (float)(c.A * Math.Clamp(alpha, 0, 1) * opacity);
        return c;
    }

    /// <summary>Fills <paramref name="output"/> with everything visible at song time t, in draw order.</summary>
    public void Collect(double t, List<DrawItem> output)
    {
        output.Clear();
        int b = BinOf(t);
        if (b >= bins.Length || bins[b] == null) return;
        foreach (var i in bins[b])
        {
            var o = Level.Objects[i];
            var phase = PhaseAt(o, t);
            if (phase == Phase.Absent) continue;
            var color = ColorAt(o, t, phase);
            if (color.A <= 0.001f && !(phase == Phase.Active && o.Hitbox)) continue;
            output.Add(new DrawItem
            {
                Object = o,
                Geometry = ShapeGeometry.For(o.Shape),
                Transform = TransformAt(o, t),
                Color = color,
                Phase = phase,
            });
        }
        output.Sort((a, c) => a.Object.Layer != c.Object.Layer ? a.Object.Layer.CompareTo(c.Object.Layer) : a.Object.FileIndex.CompareTo(c.Object.FileIndex));
    }

    private Vec2[] outlineBuf = new Vec2[64], holeBuf = new Vec2[64];

    /// <summary>True if a player circle overlaps any active hitbox in <paramref name="items"/>.</summary>
    public bool Hits(List<DrawItem> items, Vec2 center, double radius, out LevelObject hitBy)
    {
        hitBy = null;
        foreach (var it in items)
        {
            if (it.Phase != Phase.Active || !it.Object.Hitbox) continue;
            double s = Level.HitboxScale * it.Object.HitboxScale;
            if (s <= 0) continue;
            var g = it.Geometry;
            // Broad phase: the shape lies within the unit square's circumcircle around its center.
            var m = it.Transform;
            double cx = m.Tx, cy = m.Ty;
            double reach = 0.7072 * s * (Math.Sqrt(m.A * m.A + m.B * m.B) + Math.Sqrt(m.C * m.C + m.D * m.D));
            double dx = center.X - cx, dy = center.Y - cy;
            if (dx * dx + dy * dy > (reach + radius) * (reach + radius)) continue;
            var outline = ToWorld(g.Outline, it.Transform, s, ref outlineBuf);
            if (!Collision.CircleOverlapsPolygon(center, radius, outline, g.Outline.Length)) continue;
            if (g.Hole != null)
            {
                var hole = ToWorld(g.Hole, it.Transform, s, ref holeBuf);
                if (Collision.CircleInsidePolygon(center, radius, hole, g.Hole.Length)) continue;
            }
            hitBy = it.Object;
            return true;
        }
        return false;
    }

    private static Vec2[] ToWorld(Vec2[] unit, Affine m, double scale, ref Vec2[] buffer)
    {
        if (buffer.Length < unit.Length) buffer = new Vec2[unit.Length];
        for (int i = 0; i < unit.Length; i++) buffer[i] = m.Apply(unit[i].X * scale, unit[i].Y * scale);
        return buffer;
    }

    // ----- scene events -----

    public Rgba BackgroundAt(double t)
    {
        int i = LastEventAtOrBefore(backgroundEvents, t);
        if (i < 0) return Level.Background;
        return BackgroundTransition(backgroundStartColors[i], backgroundEvents[i], t);
    }

    private static Rgba BackgroundTransition(Rgba from, LevelEvent e, double t)
    {
        double p = e.Duration > 0 ? Math.Clamp((t - e.T) / e.Duration, 0, 1) : 1;
        return Rgba.Lerp(from, e.Color, (float)Ease.Apply(e.Ease, p));
    }

    /// <summary>Full-screen flashes visible at time t, oldest first, with their current alpha.</summary>
    public void Flashes(double t, List<Rgba> output)
    {
        output.Clear();
        foreach (var e in Level.Events)
        {
            if (e.T > t) break;
            if (e.Type != EventType.Flash || e.Duration <= 0 || t >= e.T + e.Duration) continue;
            var c = e.Color;
            c.A = (float)(c.A * e.Opacity * (1 - (t - e.T) / e.Duration));
            output.Add(c);
        }
    }

    private static int LastEventAtOrBefore(List<LevelEvent> list, double t)
    {
        int lo = 0, hi = list.Count - 1, res = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (list[mid].T <= t) { res = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return res;
    }
}
