using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Beatform.Core;

/// <summary>A player as the level sees it: where it is, its colour, and whether it can be chased.</summary>
public struct PlayerInfo
{
    /// <summary>1 to 4.</summary>
    public int Number;
    public Vec2 Position;
    public Rgba Color;
    public bool Alive;
}

/// <summary>Where the players are right now. The loader fills it from the game; the editor uses a stand-in.</summary>
public interface IPlayerSource
{
    int Count { get; }
    PlayerInfo Get(int index);
}

public sealed class PlayerList : IPlayerSource
{
    public readonly List<PlayerInfo> Players = new();
    public int Count => Players.Count;
    public PlayerInfo Get(int index) => Players[index];

    public bool TryGetNumber(int number, out PlayerInfo player)
    {
        foreach (var p in Players)
            if (p.Number == number) { player = p; return true; }
        player = default;
        return false;
    }
}

public enum ImposterMode { Straight, Chase }

/// <summary>How a dash is marked: a thin ring that spreads out, or a disk that hollows into a ring.</summary>
public enum DashPulse { Ring, Disk }

/// <summary>
/// An object that moves like a player: it cruises, dashes and slows back down, stretches with its
/// speed and leaves a trail of copies of itself. See spec/level-format.md, "Imposters".
/// All distances are in level units, speeds in units per second and times in ms.
/// </summary>
public sealed class ImposterBehavior
{
    public ImposterMode Mode = ImposterMode.Chase;
    /// <summary>Heading in degrees (0 = right, 90 = down). Chasers use it until they see a player.</summary>
    public double Angle = 180;
    public double Speed = 383;
    public double DashSpeed = 1467;
    /// <summary>Velocity is multiplied by this every 1/60 s while it is faster than <see cref="Speed"/>.</summary>
    public double DashDrag = 0.9;
    public double DashTime = 450;
    public double DashEvery = 550;
    /// <summary>Cruising time before the first dash; negative means <see cref="DashEvery"/>.</summary>
    public double FirstDash = -1;
    /// <summary>Time after activation to brake and stand still; negative means never.</summary>
    public double StopAt = -1;
    public double StopFor = 0;
    /// <summary>Time after activation to dash away in a straight line, no longer harmful; negative means never.</summary>
    public double LeaveAt = -1;
    /// <summary>Keep inside the screen while hunting.</summary>
    public bool Bounds = true;
    public double StretchSpeed = 2000;
    public double StretchMax = 0.4;
    /// <summary>Trail, dash bursts, dash rings and the drop shadow.</summary>
    public bool Effects = true;
    public DashPulse Pulse = DashPulse.Ring;

    public static ImposterBehavior Parse(JsonElement e)
    {
        var b = new ImposterBehavior();
        if (Str(e, "mode") == "straight") b.Mode = ImposterMode.Straight;
        b.Angle = Num(e, "angle", b.Angle);
        b.Speed = Math.Max(0, Num(e, "speed", b.Speed));
        b.DashSpeed = Math.Max(0, Num(e, "dashSpeed", b.DashSpeed));
        b.DashDrag = Math.Clamp(Num(e, "dashDrag", b.DashDrag), 0.5, 1);
        b.DashTime = Math.Max(0, Num(e, "dashTime", b.DashTime));
        b.DashEvery = Math.Max(16, Num(e, "dashEvery", b.DashEvery));
        b.FirstDash = Num(e, "firstDash", b.FirstDash);
        b.StopAt = Num(e, "stopAt", b.StopAt);
        b.StopFor = Math.Max(0, Num(e, "stopFor", b.StopFor));
        b.LeaveAt = Num(e, "leaveAt", b.LeaveAt);
        b.Bounds = Bool(e, "bounds", b.Mode == ImposterMode.Chase);
        b.StretchSpeed = Math.Max(1, Num(e, "stretchSpeed", b.StretchSpeed));
        b.StretchMax = Math.Clamp(Num(e, "stretchMax", b.StretchMax), 0, 2);
        b.Effects = Bool(e, "effects", true);
        if (Str(e, "dashPulse") == "disk") b.Pulse = DashPulse.Disk;
        return b;
    }

    private static string Str(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double Num(JsonElement e, string n, double f) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : f;
    private static bool Bool(JsonElement e, string n, bool f) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : f;
}

/// <summary>
/// Runs one imposter at a fixed 60 steps per second from its activation. Going back in time restarts
/// the run, so seeks and rewinds work; chasers then follow wherever the players are now.
/// </summary>
public sealed class ImposterSim
{
    public const double StepMs = 1000.0 / 60;
    public const double BoundsX = 620, BoundsY = 340;
    public const double ShadowOffset = 4;
    public const double RingThickness = 0.155;

    private enum State { Cruise, Dash, Stop, Leave }

    public struct Particle
    {
        public double X, Y, Life, Angle, Scale, Vx, Vy;
    }

    public struct Pulse
    {
        public double X, Y, Scale, Alpha;
    }

    private readonly LevelObject obj;
    private readonly ImposterBehavior b;

    // Simulation state after `steps` steps (local time steps * StepMs since activation, -1 = not started).
    private long steps = -1;
    private double x, y, vx, vy, heading, drawAngle, stretch, warp, lifeT, stateT, trailT, dashAngle;
    private State state;
    private bool firstDashDone, stopped, entered;
    public readonly List<Particle> Trail = new();
    public readonly List<Pulse> Pulses = new();

    public ImposterSim(LevelObject o)
    {
        obj = o;
        b = o.Imposter;
    }

    /// <summary>The current frame: the last step, moved on by its velocity for the time since.</summary>
    public double X, Y, DrawAngle, StretchX = 1, StretchY = 1;
    public bool Harmful;
    /// <summary>Warning-phase scale (0..1 with overshoot), 1 once active.</summary>
    public double Grow = 1;

    public void AdvanceTo(double localT, IPlayerSource players)
    {
        double warn = obj.Warn;
        if (localT < warn)
        {
            // Warning: sit at the spawn point and grow in; chasers already face the nearest player.
            steps = -1;
            var a = Anchor(players);
            X = a.X + obj.X.Evaluate(localT);
            Y = a.Y + obj.Y.Evaluate(localT);
            KeepOnScreen(ref X, ref Y);
            double h = b.Angle;
            if (b.Mode == ImposterMode.Chase && Nearest(players, X, Y, out var px, out var py)) h = Deg(Math.Atan2(py - Y, px - X));
            DrawAngle = h;
            StretchX = StretchY = 1;
            Grow = warn > 0 ? Ease.Apply(Ease.Kind.OutBack, localT / warn) : 1;
            Harmful = false;
            Trail.Clear();
            Pulses.Clear();
            return;
        }
        Grow = 1;
        double t = localT - warn;
        long target = (long)Math.Floor(t / StepMs + 1e-9);
        if (steps < 0 || target < steps) Reset(players);
        while (steps < target && steps < 2_000_000) Step(players);
        double since = Math.Clamp(t - steps * StepMs, 0, StepMs);
        X = x + vx * since / 1000;
        Y = y + vy * since / 1000;
        DrawAngle = drawAngle;
        double total = stretch + warp;
        StretchX = 1 + total;
        StretchY = Math.Max(0.25, 1 - total * 0.6);
        Harmful = state != State.Leave;
    }

    private void Reset(IPlayerSource players)
    {
        steps = 0;
        var anchor = Anchor(players);
        x = anchor.X + obj.X.Evaluate(obj.Warn);
        y = anchor.Y + obj.Y.Evaluate(obj.Warn);
        KeepOnScreen(ref x, ref y);
        heading = b.Angle;
        if (b.Mode == ImposterMode.Chase && Nearest(players, x, y, out var px, out var py)) heading = Deg(Math.Atan2(py - y, px - x));
        drawAngle = heading;
        vx = vy = 0;
        stretch = 0;
        warp = 0;
        lifeT = stateT = trailT = 0;
        state = State.Cruise;
        firstDashDone = stopped = false;
        entered = Math.Abs(x) <= BoundsX && Math.Abs(y) <= BoundsY;
        Trail.Clear();
        Pulses.Clear();
        Burst();
        if (b.FirstDash == 0) StartDash();
    }

    private void Step(IPlayerSource players)
    {
        double dt = StepMs;
        lifeT += dt;
        stateT += dt;

        if (b.LeaveAt >= 0 && lifeT >= b.LeaveAt && state != State.Leave)
        {
            state = State.Leave;
            stateT = 0;
            dashAngle = heading;
            warp = 0.8;
            Pulses.Add(new Pulse { X = x, Y = y, Scale = 0.2, Alpha = 1 });
            Burst();
        }
        if (b.StopAt >= 0 && !stopped && lifeT >= b.StopAt && state != State.Leave)
        {
            stopped = true;
            state = State.Stop;
            stateT = 0;
        }

        double px = 0, py = 0;
        bool aim = b.Mode == ImposterMode.Chase && Nearest(players, x, y, out px, out py);
        switch (state)
        {
            case State.Cruise:
            {
                if (aim) heading = Deg(Math.Atan2(py - y, px - x));
                SetVelocity(b.Speed, heading);
                double wait = !firstDashDone && b.FirstDash > 0 ? b.FirstDash : b.DashEvery;
                if (stateT >= wait) StartDash();
                break;
            }
            case State.Dash:
            {
                double speed = Math.Sqrt(vx * vx + vy * vy);
                if (aim)
                {
                    heading = Deg(Math.Atan2(py - y, px - x));
                    SetVelocity(speed, heading);
                }
                if (speed > b.Speed) { vx *= b.DashDrag; vy *= b.DashDrag; }
                else SetVelocity(b.Speed, heading);
                if (stateT >= b.DashTime) { state = State.Cruise; stateT = 0; }
                break;
            }
            case State.Stop:
                vx *= 0.8; vy *= 0.8;
                if (stateT >= b.StopFor) StartDash();
                break;
            case State.Leave:
                SetVelocity(b.DashSpeed, dashAngle);
                break;
        }

        x += vx * dt / 1000;
        y += vy * dt / 1000;
        // An imposter that starts off screen comes in first; only then does it stay on screen.
        if (!entered && Math.Abs(x) <= BoundsX && Math.Abs(y) <= BoundsY) entered = true;
        if (b.Bounds && entered && state != State.Leave)
        {
            if (x < -BoundsX) { x = -BoundsX; vx *= -0.5; }
            if (x > BoundsX) { x = BoundsX; vx *= -0.5; }
            if (y < -BoundsY) { y = -BoundsY; vy *= -0.5; }
            if (y > BoundsY) { y = BoundsY; vy *= -0.5; }
        }

        double target = state == State.Stop ? Math.Round(heading / 90) * 90 : heading;
        drawAngle += AngleDiff(target, drawAngle) * 0.2;
        double v = Math.Sqrt(vx * vx + vy * vy);
        stretch += (Math.Clamp(v / b.StretchSpeed, 0, b.StretchMax) - stretch) * 0.3;
        warp += (0 - warp) * 0.04;

        if (b.Effects) StepEffects(dt, v);
        steps++;
    }

    private void StartDash()
    {
        firstDashDone = true;
        state = State.Dash;
        stateT = 0;
        dashAngle = heading;
        SetVelocity(b.DashSpeed, heading);
        warp = 0.8;
        Pulses.Add(new Pulse { X = x, Y = y, Scale = 0.2, Alpha = 1 });
        Burst();
    }

    /// <summary>Twelve small copies thrown backwards, as when a player dashes.</summary>
    private void Burst()
    {
        if (!b.Effects) return;
        for (int c = 0; c < 12; c++)
        {
            double r1 = Math.Sin(lifeT * 113 + c * 45), r2 = Math.Cos(lifeT * 277 + c * 83), r3 = Math.Sin(lifeT * 341 + c * 67);
            double dir = (heading + 180 + r1 * 55) * Math.PI / 180;
            double spd = 133 + Math.Abs(r2) * 533;
            Trail.Add(new Particle
            {
                X = x, Y = y, Life = 1.2, Angle = heading + 180 + r1 * 55,
                Scale = 0.08 + Math.Abs(r3) * 0.12, Vx = Math.Cos(dir) * spd, Vy = Math.Sin(dir) * spd,
            });
        }
    }

    private void StepEffects(double dt, double speed)
    {
        for (int i = Trail.Count - 1; i >= 0; i--)
        {
            var p = Trail[i];
            p.Life -= dt / 1000 * 2.5;
            p.X += p.Vx * dt / 1000;
            p.Y += p.Vy * dt / 1000;
            p.Vx *= 0.88; p.Vy *= 0.88;
            if (p.Life <= 0) Trail.RemoveAt(i); else Trail[i] = p;
        }
        for (int i = Pulses.Count - 1; i >= 0; i--)
        {
            var p = Pulses[i];
            p.Scale += dt / 1000 * 8;
            p.Alpha -= dt / 1000 * 3.5;
            if (p.Alpha <= 0) Pulses.RemoveAt(i); else Pulses[i] = p;
        }
        if (speed > 67)
        {
            trailT += dt;
            while (trailT > 35)
            {
                trailT -= 35;
                Trail.Add(new Particle
                {
                    X = x + Math.Sin(lifeT * 45) * 4, Y = y + Math.Cos(lifeT * 32) * 4, Life = 1,
                    Angle = heading + Math.Sin(lifeT * 60) * 90, Scale = 0.25 + Math.Sin(lifeT * 80) * 0.1,
                });
            }
        }
    }

    /// <summary>
    /// An imposter whose parent is a player spawns relative to that player (its x/y tracks are the
    /// offset). Without such a player the offset is taken from the centre of the screen.
    /// </summary>
    private Vec2 Anchor(IPlayerSource players)
    {
        if (obj.PlayerAnchor <= 0 || players == null) return new Vec2(0, 0);
        for (int i = 0; i < players.Count; i++)
        {
            var p = players.Get(i);
            if (p.Number == obj.PlayerAnchor && p.Alive) return p.Position;
        }
        return new Vec2(0, 0);
    }

    private void KeepOnScreen(ref double px, ref double py)
    {
        if (obj.PlayerAnchor <= 0 || !b.Bounds) return;
        px = Math.Clamp(px, -BoundsX, BoundsX);
        py = Math.Clamp(py, -BoundsY, BoundsY);
    }

    private void SetVelocity(double speed, double angleDeg)
    {
        double a = angleDeg * Math.PI / 180;
        vx = Math.Cos(a) * speed;
        vy = Math.Sin(a) * speed;
    }

    private static bool Nearest(IPlayerSource players, double x, double y, out double px, out double py)
    {
        px = py = 0;
        if (players == null) return false;
        double best = double.MaxValue;
        bool found = false;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players.Get(i);
            if (!p.Alive) continue;
            double d = (p.Position.X - x) * (p.Position.X - x) + (p.Position.Y - y) * (p.Position.Y - y);
            if (d < best) { best = d; px = p.Position.X; py = p.Position.Y; found = true; }
        }
        return found;
    }

    private static double Deg(double rad) => rad * 180 / Math.PI;

    /// <summary>Signed shortest difference a - b in degrees.</summary>
    public static double AngleDiff(double a, double b)
    {
        double d = (a - b) % 360;
        if (d > 180) d -= 360;
        if (d < -180) d += 360;
        return d;
    }
}
