// Unit tests for the engine-free level core, plus generation of spec/test-vectors.json, which the
// editor's own implementation must reproduce.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
