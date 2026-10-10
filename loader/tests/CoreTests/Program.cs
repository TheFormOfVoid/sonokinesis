// Unit tests for the engine-free level core, plus generation of spec/test-vectors.json, which the
// editor's own implementation must reproduce.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Beatform.Core;

internal static class Program
{
    private static int failures;

    private static void Check(bool ok, string what)
    {
        if (!ok) { failures++; Console.WriteLine("FAIL " + what); }
    }

    private static void Near(double a, double b, string what, double eps = 1e-9) =>
        Check(Math.Abs(a - b) <= eps, $"{what}: got {a.ToString("R", CultureInfo.InvariantCulture)} expected {b.ToString("R", CultureInfo.InvariantCulture)}");

    private static int Main(string[] args)
    {
        var repo = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        EaseTests();
        TrackTests();
        GeometryTests();
        var level = LevelData.Parse(File.ReadAllText(Path.Combine(repo, "spec", "examples", "vectors", "level.json")));
        LevelTests(level);
        ImposterTests();
        WriteVectors(level, Path.Combine(repo, "spec", "test-vectors.json"));
        Console.WriteLine(failures == 0 ? "all core tests passed" : $"{failures} failures");
        return failures == 0 ? 0 : 1;
    }

    private static void EaseTests()
    {
        foreach (Ease.Kind k in Enum.GetValues(typeof(Ease.Kind)))
        {
            Near(Ease.Apply(k, 1), 1, $"{k}(1)");
            if (k != Ease.Kind.Step) Near(Ease.Apply(k, 0), 0, $"{k}(0)", 1e-9);
            Check(Ease.Parse(Ease.Name(k)) == k, $"parse round trip {k}");
        }
        Near(Ease.Apply(Ease.Kind.InQuad, 0.5), 0.25, "inQuad(.5)");
        Near(Ease.Apply(Ease.Kind.OutCubic, 0.5), 0.875, "outCubic(.5)");
        Near(Ease.Apply(Ease.Kind.InOutCubic, 0.25), 0.0625, "inOutCubic(.25)");
        Near(Ease.Apply(Ease.Kind.Step, 0.999), 0, "step(.999)");
        Check(Ease.Parse("nonsense") == Ease.Kind.Linear, "unknown ease is linear");
        Near(Ease.Apply(Ease.Kind.OutBounce, 0.5), 0.765625, "outBounce(.5)");
    }

    private static void TrackTests()
    {
        var empty = new Track { Default = 7 };
        Near(empty.Evaluate(123), 7, "empty track default");
        var t = new Track
        {
            Keys = new[]
            {
                new Keyframe { T = 100, V = 10 },
                new Keyframe { T = 200, V = 20, Ease = Ease.Kind.Linear },
                new Keyframe { T = 200, V = 50 },
                new Keyframe { T = 300, V = 0, Ease = Ease.Kind.InQuad },
            },
        };
        Near(t.Evaluate(0), 10, "before first key");
        Near(t.Evaluate(150), 15, "linear mid");
        Near(t.Evaluate(200), 50, "duplicate time: later key wins");
        Near(t.Evaluate(250), 50 - 50 * 0.25, "inQuad uses ease of the later key");
        Near(t.Evaluate(1000), 0, "after last key");
    }

    private static void GeometryTests()
    {
        var circle = ShapeGeometry.For(new ShapeDef { Type = ShapeType.Circle });
        Check(circle.Outline.Length == 48, "circle has 48 segments");
        var tri = ShapeGeometry.For(new ShapeDef { Type = ShapeType.Polygon, Sides = 3 });
        Near(tri.Outline[0].X, 0, "triangle points up (x)", 1e-12);
        Near(tri.Outline[0].Y, -0.5, "triangle points up (y)", 1e-12);
        // Text: one line squeezed into the unit box; "Hh" is 3 + 3 strokes (lowercase uses the uppercase glyph), two triangles each.
        Near(StrokeFont.Width("HI"), (2 * 4 + 1.4) / 6, "text width of two letters");
        Near(StrokeFont.Width(""), 4.0 / 6, "empty text is one letter wide");
        var hi = ShapeGeometry.For(new ShapeDef { Type = ShapeType.Text, Text = "Hh", Weight = 0.1 });
        Check(hi.Indices.Length == 6 * 6 && hi.Vertices.Length == 6 * 4, $"text Hh has 6 strokes (got {hi.Indices.Length / 6})");
        Check(hi.Outline.Length == 4 && hi.Outline[0].X == -0.5 && hi.Outline[2].Y == 0.5, "text collides as its box");
        double minX = double.MaxValue, maxX = double.MinValue;
        foreach (var v in hi.Vertices) { minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X); }
        Near(minX, -0.5 - 0.05 / StrokeFont.Width("HH"), "text starts at the box edge (minus half a stroke)", 1e-9);
        Near(maxX, 0.5 + 0.05 / StrokeFont.Width("HH"), "text ends at the box edge (plus half a stroke)", 1e-9);
        var spaced = ShapeGeometry.For(new ShapeDef { Type = ShapeType.Text, Text = "H I~", Weight = 0.1 });
        Check(spaced.Indices.Length == 6 * 6, "spaces and unknown characters draw nothing");
        var parsed = LevelData.Parse("{\"format\":\"beatform.level\",\"meta\":{\"id\":\"t\",\"audio\":\"a.ogg\"},"
            + "\"objects\":[{\"shape\":{\"type\":\"text\",\"text\":\"OK\",\"weight\":9}}]}");
        Check(parsed.Objects[0].Shape.Type == ShapeType.Text && parsed.Objects[0].Shape.Text == "OK", "text shape parses");
        Near(parsed.Objects[0].Shape.Weight, 0.4, "text weight is clamped");
        var square = new[] { new Vec2(-1, -1), new Vec2(1, -1), new Vec2(1, 1), new Vec2(-1, 1) };
        Check(Collision.CircleOverlapsPolygon(new Vec2(0, 0), 0.1, square, 4), "center inside");
        Check(Collision.CircleOverlapsPolygon(new Vec2(1.5, 0), 0.6, square, 4), "edge touch");
        Check(!Collision.CircleOverlapsPolygon(new Vec2(1.5, 0), 0.4, square, 4), "near miss");
        Check(Collision.CircleInsidePolygon(new Vec2(0, 0), 0.5, square, 4), "fully inside");
        Check(!Collision.CircleInsidePolygon(new Vec2(0.8, 0), 0.5, square, 4), "not fully inside");

        // Clockwise rotation on a y-down screen: +90° turns +x into +y.
        var r = Affine.RotateTranslate(90, 0, 0).Apply(1, 0);
        Near(r.X, 0, "rot90 x", 1e-12);
        Near(r.Y, 1, "rot90 y", 1e-12);
    }

    private static void LevelTests(LevelData level)
    {
        var ev = new LevelEvaluator(level);
        var rect = level.Objects.Find(o => o.Id == "rect-move");
        Check(LevelEvaluator.PhaseAt(rect, 499.9) == Phase.Warning, "rect warning before 500");
        Check(LevelEvaluator.PhaseAt(rect, 500) == Phase.Active, "rect active at 500");
        Check(LevelEvaluator.PhaseAt(rect, 2600) == Phase.Outro, "rect outro at 2600");
        Check(LevelEvaluator.PhaseAt(rect, 3000) == Phase.Absent, "rect gone at end");

        // Parent: the arm rides the spinner, which turns 720° over 5 s from t=1000.
        var arm = level.Objects.Find(o => o.Id == "arm");
        var m = LevelEvaluator.TransformAt(arm, 2750); // spinner local time 1750 → 252°
        var tip = m.Apply(0.5, 0);
        double ang = 252 * Math.PI / 180;
        Near(tip.X, Math.Cos(ang) * (20 + 300), "arm tip x", 1e-6);
        Near(tip.Y, Math.Sin(ang) * (20 + 300), "arm tip y", 1e-6);

        var items = new List<DrawItem>();
        ev.Collect(1600, items);
        Check(items.Count == 3, $"three objects visible at 1600 (got {items.Count})");
        Check(items.Count > 0 && items[items.Count - 1].Object.Id == "arm", "highest layer draws last");

        ev.Collect(1250, items);
        var rc = LevelEvaluator.TransformAt(rect, 1250).Apply(0, 0);
        Check(ev.Hits(items, rc, 8, out var by) && by.Id == "rect-move", "player at rect center is hit");
        Check(!ev.Hits(items, new Vec2(0, 300), 8, out _), "player far away is safe");
        ev.Collect(1100, items);
        Check(!ev.Hits(items, new Vec2(0, 0), 8, out _), "decoration does not hurt");

        // Ring: the hole is safe once the ring is large; the band hurts.
        ev.Collect(3700, items);
        Check(!ev.Hits(items, new Vec2(250, -100), 8, out _), "inside the ring hole is safe");
        var ringObj = level.Objects.Find(o => o.Id == "ring");
        double diameter = ringObj.W.Evaluate(3700 - ringObj.Start);
        double bandRadius = 0.9 * diameter * (0.5 - 0.05);
        Check(ev.Hits(items, new Vec2(250 + bandRadius, -100), 2, out var ringHit) && ringHit.Id == "ring", "ring band hurts");

        var bg = ev.BackgroundAt(1500);
        var expected = Rgba.Lerp(Rgba.Parse("#101018", default), Rgba.Parse("#300018", default), (float)Ease.Apply(Ease.Kind.OutQuad, 0.5));
        Near(bg.R, expected.R, "background mid transition r", 1e-6);
        var flashes = new List<Rgba>();
        ev.Flashes(2750, flashes);
        Check(flashes.Count == 2, "two overlapping flashes");
    }

    private static LevelData Mini(string objects) => LevelData.Parse(
        "{\"format\":\"beatform.level\",\"formatVersion\":2,\"meta\":{\"id\":\"t\",\"audio\":\"a.ogg\"},\"objects\":[" + objects + "]}");

    private static void ImposterTests()
    {
        // A straight drive-by: cruises left at 200 u/s, first dash after 500 ms.
        var level = Mini("{\"id\":\"d\",\"shape\":{\"type\":\"rect\"},\"start\":0,\"warn\":1000,\"end\":9000,"
            + "\"imposter\":{\"mode\":\"straight\",\"angle\":180,\"speed\":200,\"dashSpeed\":667,\"dashEvery\":500,\"dashTime\":450},"
            + "\"tracks\":{\"x\":[[0,600]],\"y\":[[0,-100]],\"w\":[[0,24]],\"h\":[[0,24]]}}");
        var ev = new LevelEvaluator(level);
        var d = level.Objects[0];
        Check(d.Imposter != null && d.Imposter.Mode == ImposterMode.Straight && !d.Imposter.Bounds, "imposter parses (straight, no bounds)");
        var sim = ev.SimAt(d, 500);
        Near(sim.X, 600, "warning: sits at its spawn point");
        Check(sim.Grow > 0 && sim.Grow < 1.2, "warning: grows in");
        sim = ev.SimAt(d, 1000 + 24 * ImposterSim.StepMs);
        Near(sim.X, 600 - 80, "cruise 400 ms at 200 u/s", 1e-6);
        Near(sim.Y, -100, "straight left keeps y", 1e-6);
        double beforeDash = sim.X;
        sim = ev.SimAt(d, 1000 + 37 * ImposterSim.StepMs);
        Check(beforeDash - sim.X > 13 * 200 / 60.0 * 1.5, "a dash moves faster than cruising");
        Check(sim.StretchX > 1.5 && sim.StretchY < 0.7, "dashing stretches the body");
        Check(sim.Pulses.Count == 1, "a dash leaves one ring");
        Check(sim.Trail.Count > 12, "a dash throws a burst of trail copies");
        double at3s = ev.SimAt(d, 3000).X;
        Near(ev.SimAt(d, 1200).X, ev.SimAt(d, 1200).X, "going back in time restarts the run");
        Near(ev.SimAt(d, 3000).X, at3s, "the run is repeatable", 1e-9);

        var items = new List<DrawItem>();
        ev.Collect(3000, items);
        var body = items[items.Count - 1];
        Check(body.Order == 3 && !body.Harmless && items.Take(items.Count - 1).All(i => i.Harmless), "effects are harmless, the body is not");
        var center = body.HitTransform.Apply(0, 0);
        Check(ev.Hits(items, center, 8, out _), "player on the imposter is hit");
        Check(!ev.Hits(items, new Vec2(center.X, center.Y - 60), 8, out _), "player away from it is safe");

        // A chaser heads for the nearest living player and stays on screen.
        var chase = Mini("{\"id\":\"c\",\"shape\":{\"type\":\"rect\"},\"start\":0,\"end\":20000,"
            + "\"imposter\":{\"speed\":300,\"dashEvery\":100000,\"leaveAt\":5000},"
            + "\"tracks\":{\"x\":[[0,0]],\"y\":[[0,0]],\"w\":[[0,22]],\"h\":[[0,22]]}}");
        var cev = new LevelEvaluator(chase);
        var players = new PlayerList();
        players.Players.Add(new PlayerInfo { Number = 1, Position = new Vec2(-2000, 0), Alive = true });
        players.Players.Add(new PlayerInfo { Number = 2, Position = new Vec2(0, 3000), Alive = false });
        cev.Players = players;
        var c = chase.Objects[0];
        var cs = cev.SimAt(c, 1000);
        Check(cs.X < -250 && Math.Abs(cs.Y) < 1e-6, $"chaser goes for the living player (x {cs.X:0.0})");
        cs = cev.SimAt(c, 4000);
        Near(cs.X, -ImposterSim.BoundsX, "chaser stops at the screen edge", 1e-6);
        cev.Collect(6000, items);
        Check(items[items.Count - 1].Harmless, "after leaveAt the imposter is harmless");

        // Objects anchored to a player follow it; with no such player they are not drawn.
        var anchored = Mini("{\"id\":\"b\",\"shape\":{\"type\":\"rect\"},\"start\":0,\"end\":5000,\"parent\":\"@player\",\"color\":\"@player\",\"hitbox\":false,"
            + "\"tracks\":{\"x\":[[0,10]],\"y\":[[0,-20]]}},"
            + "{\"id\":\"b2\",\"shape\":{\"type\":\"rect\"},\"start\":0,\"end\":5000,\"parent\":\"@player3\",\"hitbox\":false}");
        var aev = new LevelEvaluator(anchored);
        Check(anchored.Objects[0].PlayerAnchor == 1 && anchored.Objects[1].PlayerAnchor == 3, "@player and @player3 parse");
        aev.Collect(1000, items);
        Check(items.Count == 0, "no players: anchored objects are hidden");
        var ap = new PlayerList();
        ap.Players.Add(new PlayerInfo { Number = 1, Position = new Vec2(100, 50), Alive = true, Color = new Rgba(1, 1, 0) });
        aev.Players = ap;
        aev.Collect(1000, items);
        Check(items.Count == 1, "only the anchor that has a player is drawn");
        var pc = items[0].Transform.Apply(0, 0);
        Near(pc.X, 110, "anchored x"); Near(pc.Y, 30, "anchored y");
        Near(items[0].Color.G, 1, "@player colour is the player's"); Near(items[0].Color.B, 0, "@player colour is the player's (b)");

        // A child of an imposter rides along with it.
        var ride = Mini("{\"id\":\"i\",\"shape\":{\"type\":\"rect\"},\"start\":0,\"end\":5000,"
            + "\"imposter\":{\"mode\":\"straight\",\"angle\":90,\"speed\":120,\"dashEvery\":100000},\"tracks\":{\"x\":[[0,0]],\"y\":[[0,0]]}},"
            + "{\"id\":\"tag\",\"shape\":{\"type\":\"rect\"},\"start\":0,\"end\":5000,\"parent\":\"i\",\"hitbox\":false,\"tracks\":{\"x\":[[0,30]]}}");
        var rev = new LevelEvaluator(ride);
        Check(rev.TryWorldTransform(ride.Objects[1], 1000, out var rm), "child of an imposter resolves");
        var rs = rev.SimAt(ride.Objects[0], 1000);
        Near(rm.Apply(0, 0).X, rs.X + 30, "child follows the imposter (x)", 1e-6);
        Near(rm.Apply(0, 0).Y, rs.Y, "child follows the imposter (y)", 1e-6);
        Check(rs.Y > 100, "angle 90 moves down");

        // A triangle imposter points its tip where it is going.
        var tri = Mini("{\"id\":\"t\",\"shape\":{\"type\":\"polygon\",\"sides\":3},\"start\":0,\"end\":5000,"
            + "\"imposter\":{\"mode\":\"straight\",\"angle\":180,\"speed\":100,\"dashEvery\":100000,\"effects\":false},\"tracks\":{\"w\":[[0,20]],\"h\":[[0,20]]}}");
        var tev = new LevelEvaluator(tri);
        tev.Collect(2000, items);
        var tm = items[items.Count - 1].HitTransform;
        var tip = tm.Apply(0, -0.5); var tc = tm.Apply(0, 0);
        Check(tip.X < tc.X - 5 && Math.Abs(tip.Y - tc.Y) < 1e-6, "triangle tip leads (heading left)");

        // An imposter whose parent is a player spawns next to that player.
        var near = Mini("{\"id\":\"n\",\"shape\":{\"type\":\"rect\"},\"start\":0,\"warn\":500,\"end\":5000,\"parent\":\"@player\","
            + "\"imposter\":{\"speed\":0,\"dashSpeed\":0,\"dashEvery\":100000},\"tracks\":{\"x\":[[0,100]],\"y\":[[0,-50]]}}");
        var nev = new LevelEvaluator(near);
        var np = new PlayerList();
        np.Players.Add(new PlayerInfo { Number = 1, Position = new Vec2(200, 100), Alive = true });
        nev.Players = np;
        var ns = nev.SimAt(near.Objects[0], 250);
        Near(ns.X, 300, "player-relative spawn x during warning"); Near(ns.Y, 50, "player-relative spawn y during warning");
        np.Players[0] = new PlayerInfo { Number = 1, Position = new Vec2(-200, 0), Alive = true };
        ns = nev.SimAt(near.Objects[0], 1000);
        Near(ns.X, -100, "starts from the player's spot at activation", 1e-6);

        // Sway and sound events.
        var ev2 = LevelData.Parse("{\"format\":\"beatform.level\",\"formatVersion\":2,\"meta\":{\"id\":\"t\",\"audio\":\"a.ogg\"},\"events\":["
            + "{\"t\":0,\"type\":\"sway\",\"rotation\":1,\"x\":5,\"period\":2000,\"periodX\":2400},"
            + "{\"t\":4000,\"type\":\"sway\",\"rotation\":0,\"duration\":1000},"
            + "{\"t\":100,\"type\":\"sound\",\"sound\":\"playerDeath\",\"volume\":0.5}]}");
        var sev = new LevelEvaluator(ev2);
        Near(sev.SwayAt(500).Rotation, 1, "sway rotation peaks a quarter period in", 1e-9);
        Near(sev.SwayAt(600).X, 5 * Math.Sin(2 * Math.PI * 600 / 2400), "sway x uses its own period", 1e-9);
        Near(sev.SwayAt(4500).Rotation, 0.5 * Math.Sin(2 * Math.PI * 4500 / 2000), "a later sway blends in over its duration", 1e-9);
        Near(sev.SwayAt(6500).Rotation, 0, "sway can be turned off", 1e-9);
        Check(ev2.Events.Exists(e => e.Type == EventType.Sound && e.Sound == "playerDeath" && Math.Abs(e.Volume - 0.5) < 1e-9), "sound event parses");
    }

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static void WriteVectors(LevelData level, string path)
    {
        var ev = new LevelEvaluator(level);
        var sb = new StringBuilder();
        sb.Append("{\n  \"level\": \"examples/vectors/level.json\",\n");
        sb.Append("  \"note\": \"Generated by loader/tests/CoreTests from the reference implementation. m maps unit shape points to game space as [a, b, c, d, tx, ty]: x' = a*x + c*y + tx, y' = b*x + d*y + ty.\",\n");

        sb.Append("  \"eases\": [\n");
        var lines = new List<string>();
        foreach (Ease.Kind k in Enum.GetValues(typeof(Ease.Kind)))
        {
            var vals = new List<string>();
            foreach (var p in new[] { 0.1, 0.25, 0.5, 0.75, 0.9 }) vals.Add(F(Ease.Apply(k, p)));
            lines.Add($"    {{ \"name\": \"{Ease.Name(k)}\", \"p\": [0.1, 0.25, 0.5, 0.75, 0.9], \"v\": [{string.Join(", ", vals)}] }}");
        }
        sb.Append(string.Join(",\n", lines)).Append("\n  ],\n");

        sb.Append("  \"frames\": [\n");
        var frames = new List<string>();
        var items = new List<DrawItem>();
        var flashes = new List<Rgba>();
        for (double t = 0; t <= 7600; t += 200)
        {
            ev.Collect(t, items);
            var objs = new List<string>();
            foreach (var it in items)
            {
                var m = it.Transform;
                objs.Add($"{{ \"id\": \"{it.Object.Id}\", \"phase\": \"{it.Phase.ToString().ToLowerInvariant()}\", \"m\": [{F(m.A)}, {F(m.B)}, {F(m.C)}, {F(m.D)}, {F(m.Tx)}, {F(m.Ty)}], \"rgba\": [{F(it.Color.R)}, {F(it.Color.G)}, {F(it.Color.B)}, {F(it.Color.A)}] }}");
            }
            var hits = new List<string>();
            foreach (var probe in new[] { new Vec2(0, 0), new Vec2(-200, 200), new Vec2(250, -100), new Vec2(300, 0), new Vec2(-300, -150) })
            {
                var hit = ev.Hits(items, probe, 8, out var by);
                hits.Add($"{{ \"x\": {F(probe.X)}, \"y\": {F(probe.Y)}, \"r\": 8, \"hit\": {(hit ? $"\"{by.Id}\"" : "null")} }}");
            }
            var bg = ev.BackgroundAt(t);
            ev.Flashes(t, flashes);
            var fl = new List<string>();
            foreach (var f in flashes) fl.Add($"[{F(f.R)}, {F(f.G)}, {F(f.B)}, {F(f.A)}]");
            frames.Add($"    {{ \"t\": {F(t)}, \"background\": [{F(bg.R)}, {F(bg.G)}, {F(bg.B)}], \"flashes\": [{string.Join(", ", fl)}],\n      \"objects\": [{string.Join(",\n        ", objs)}],\n      \"probes\": [{string.Join(", ", hits)}] }}");
        }
        sb.Append(string.Join(",\n", frames)).Append("\n  ]\n}\n");
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine("wrote " + path);
    }
}
