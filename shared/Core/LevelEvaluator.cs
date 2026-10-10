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
    /// <summary>Draw order among items of the same object (imposter effects come before the body).</summary>
    public int Order;
    /// <summary>Decoration that never hurts, even when its object is active (imposter trails, a leaving imposter).</summary>
    public bool Harmless;
    /// <summary>When set, collision uses <see cref="HitTransform"/> instead of the drawn transform.</summary>
    public bool HasHitTransform;
    public Affine HitTransform;
}

/// <summary>
/// Evaluation of a level at a song time: what to draw, what can hurt, scene colors. Keyframed
/// objects are a pure function of song time; imposters and objects anchored to players also depend
/// on <see cref="Players"/>. Shared by the in-game loader and the editor; contains no engine code.
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
    private readonly Dictionary<LevelObject, ImposterSim> sims = new();
    private static readonly ShapeDef pulseRing = new() { Type = ShapeType.Ring, Thickness = ImposterSim.RingThickness };
    /// <summary>Trail copies are a few units wide, so circles use a 10-sided polygon there.</summary>
    private static readonly ShapeDef trailCircle = new() { Type = ShapeType.Polygon, Sides = 10 };

    /// <summary>The players imposters chase and anchored objects follow. Null means none.</summary>
    public IPlayerSource Players;

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
        if (o.ColorFromPlayer && TryPlayer(Math.Max(1, AnchorOf(o)), out var player)) c = player.Color;
        double alpha;
        switch (phase)
        {
            case Phase.Warning:
                alpha = 0.25 + 0.1 * Math.Sin(2 * Math.PI * lt / WarningPulseMs);
                break;
            case Phase.Active:
                alpha = 1;
                double since = t - o.ActiveStart;
                if (since < ActivationFlashMs) c = Rgba.Lerp(Level.Flash, c, (float)(since / ActivationFlashMs));
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

    /// <summary>The player an object (or one of its parents) is anchored to, or 0.</summary>
    public static int AnchorOf(LevelObject o)
    {
        for (int depth = 0; o != null && depth < MaxParentDepth; depth++, o = o.Parent)
            if (o.PlayerAnchor > 0) return o.PlayerAnchor;
        return 0;
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
            if (o.Imposter != null) { AddImposter(o, t, phase, color, output); continue; }
            if (color.A <= 0.001f && !(phase == Phase.Active && o.Hitbox)) continue;
            if (!TryWorldTransform(o, t, out var m)) continue;
            output.Add(new DrawItem
            {
                Object = o,
                Geometry = ShapeGeometry.For(o.Shape),
                Transform = m,
                Color = color,
                Phase = phase,
            });
        }
        output.Sort((a, c) => a.Object.Layer != c.Object.Layer ? a.Object.Layer.CompareTo(c.Object.Layer)
            : a.Object.FileIndex != c.Object.FileIndex ? a.Object.FileIndex.CompareTo(c.Object.FileIndex) : a.Order.CompareTo(c.Order));
    }

    /// <summary>
    /// World transform of an ordinary object, following parents, imposters it rides on and the player
    /// it is anchored to. False when it is anchored to a player who is not there (or is dead).
    /// </summary>
    public bool TryWorldTransform(LevelObject o, double t, out Affine m)
    {
        m = LocalTransform(o, t);
        var cur = o;
        for (int depth = 0; depth < MaxParentDepth; depth++)
        {
            if (cur.PlayerAnchor > 0)
            {
                if (!TryPlayer(cur.PlayerAnchor, out var player) || !player.Alive) return false;
                m = m.Then(Affine.RotateTranslate(0, player.Position.X, player.Position.Y));
                return true;
            }
            var p = cur.Parent;
            if (p == null) return true;
            if (p.Imposter != null)
            {
                // Riding on an imposter: follow its position only (not its heading or stretch).
                var sim = SimAt(p, t);
                m = m.Then(Affine.RotateTranslate(0, sim.X, sim.Y));
                return true;
            }
            double plt = t - p.Start;
            m = m.Then(Affine.RotateTranslate(p.Rot.Evaluate(plt), p.X.Evaluate(plt), p.Y.Evaluate(plt)));
            cur = p;
        }
        return true;
    }

    private static Affine LocalTransform(LevelObject o, double t)
    {
        double lt = t - o.Start;
        double x = o.X.Evaluate(lt), y = o.Y.Evaluate(lt), rot = o.Rot.Evaluate(lt);
        double w = o.W.Evaluate(lt), h = o.H.Evaluate(lt);
        double r = rot * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        double lx = -o.PivotX * w, ly = -o.PivotY * h;
        return new Affine
        {
            A = cos * w,
            B = sin * w,
            C = -sin * h,
            D = cos * h,
            Tx = x + cos * lx - sin * ly,
            Ty = y + sin * lx + cos * ly,
        };
    }

    private bool TryPlayer(int number, out PlayerInfo player)
    {
        player = default;
        var ps = Players;
        if (ps == null) return false;
        for (int i = 0; i < ps.Count; i++)
        {
            var p = ps.Get(i);
            if (p.Number == number) { player = p; return true; }
        }
        return false;
    }

    /// <summary>Forgets every imposter run, so they start over (for example after the players change).</summary>
    public void ResetImposters() => sims.Clear();

    /// <summary>The imposter's simulation advanced to song time t (clamped to its lifetime).</summary>
    public ImposterSim SimAt(LevelObject o, double t)
    {
        if (!sims.TryGetValue(o, out var sim)) sims[o] = sim = new ImposterSim(o);
        sim.AdvanceTo(Math.Clamp(t - o.Start, 0, o.End - o.Start), Players);
        return sim;
    }

    private void AddImposter(LevelObject o, double t, Phase phase, Rgba color, List<DrawItem> output)
    {
        var sim = SimAt(o, t);
        double lt = t - o.Start;
        double w = o.W.Evaluate(lt), h = o.H.Evaluate(lt);
        // Alpha of the whole object (phase and opacity), applied to its effects too.
        float baseA = o.Color.A > 0 ? color.A / o.Color.A : 0;
        var shape = ShapeGeometry.For(o.Shape);
        if (o.Imposter.Effects && phase != Phase.Warning)
        {
            var ring = ShapeGeometry.For(pulseRing);
            var copy = o.Shape.Type == ShapeType.Circle ? ShapeGeometry.For(trailCircle) : shape;
            foreach (var p in sim.Pulses)
            {
                double d = 2.8 * p.Scale * w;
                var c = o.Color; c.A *= (float)(p.Alpha * 0.8) * baseA;
                output.Add(new DrawItem { Object = o, Geometry = ring, Transform = Scaled(p.X, p.Y, 0, d, d), Color = c, Phase = phase, Order = 0, Harmless = true });
            }
            foreach (var p in sim.Trail)
            {
                double s = p.Scale * p.Life;
                var c = o.Color; c.A *= (float)(Math.Min(1, p.Life) * 0.7) * baseA;
                output.Add(new DrawItem { Object = o, Geometry = copy, Transform = Scaled(p.X, p.Y, p.Angle, w * s, h * s), Color = c, Phase = phase, Order = 1, Harmless = true });
            }
            var shadow = new Rgba(0, 0, 0, 0.3f * baseA);
            output.Add(new DrawItem
            {
                Object = o, Geometry = shape, Color = shadow, Phase = phase, Order = 2, Harmless = true,
                Transform = Scaled(sim.X + ImposterSim.ShadowOffset, sim.Y + ImposterSim.ShadowOffset, sim.DrawAngle, w * sim.StretchX, h * sim.StretchY),
            });
        }
        output.Add(new DrawItem
        {
            Object = o, Geometry = shape, Color = color, Phase = phase, Order = 3, Harmless = !sim.Harmful,
            Transform = Scaled(sim.X, sim.Y, sim.DrawAngle, w * sim.StretchX * sim.Grow, h * sim.StretchY * sim.Grow),
            HasHitTransform = true,
            HitTransform = Scaled(sim.X, sim.Y, sim.DrawAngle, w, h),
        });
    }

    /// <summary>Unit square to world: scale to w x h, rotate by deg, move to (x, y).</summary>
    private static Affine Scaled(double x, double y, double deg, double w, double h)
    {
        double r = deg * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        return new Affine { A = cos * w, B = sin * w, C = -sin * h, D = cos * h, Tx = x, Ty = y };
    }

    private Vec2[] outlineBuf = new Vec2[64], holeBuf = new Vec2[64];

    /// <summary>True if a player circle overlaps any active hitbox in <paramref name="items"/>.</summary>
    public bool Hits(List<DrawItem> items, Vec2 center, double radius, out LevelObject hitBy)
    {
        bool hit = FirstHit(items, center, radius, out DrawItem item);
        hitBy = hit ? item.Object : null;
        return hit;
    }

    /// <summary>Like <see cref="Hits"/>, but returns the item that hit (its hit transform gives where it is).</summary>
    public bool FirstHit(List<DrawItem> items, Vec2 center, double radius, out DrawItem hitItem)
    {
        hitItem = default;
        foreach (var it in items)
        {
            if (it.Phase != Phase.Active || !it.Object.Hitbox || it.Harmless) continue;
            double s = Level.HitboxScale * it.Object.HitboxScale;
            if (s <= 0) continue;
            var g = it.Geometry;
            // Broad phase: the shape lies within the unit square's circumcircle around its center.
            var m = it.HasHitTransform ? it.HitTransform : it.Transform;
            double cx = m.Tx, cy = m.Ty;
            double reach = 0.7072 * s * (Math.Sqrt(m.A * m.A + m.B * m.B) + Math.Sqrt(m.C * m.C + m.D * m.D));
            double dx = center.X - cx, dy = center.Y - cy;
            if (dx * dx + dy * dy > (reach + radius) * (reach + radius)) continue;
            var outline = ToWorld(g.Outline, m, s, ref outlineBuf);
            if (!Collision.CircleOverlapsPolygon(center, radius, outline, g.Outline.Length)) continue;
            if (g.Hole != null)
            {
                var hole = ToWorld(g.Hole, m, s, ref holeBuf);
                if (Collision.CircleInsidePolygon(center, radius, hole, g.Hole.Length)) continue;
            }
            hitItem = it;
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
