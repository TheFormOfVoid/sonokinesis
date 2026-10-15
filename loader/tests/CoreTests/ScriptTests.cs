using System;
using System.Collections.Generic;
using System.Linq;
using Beatform.Core;

/// <summary>Tests of the JavaScript attack and pattern scripts (shared/Scripting).</summary>
internal static class ScriptTests
{
    private static LevelData Level(string objects, Dictionary<string, string> scripts, string events = "")
    {
        var level = LevelData.Parse("{\"format\":\"beatform.level\",\"formatVersion\":2,\"meta\":{\"id\":\"t\",\"audio\":\"a.ogg\",\"bpm\":120},\"objects\":["
            + objects + "],\"events\":[" + events + "]}");
        foreach (var kv in scripts) level.ScriptSources[kv.Key] = kv.Value;
        return level;
    }

    public static void Run(Action<bool, string> check, Action<double, double, string, double> near)
    {
        // A bar that slides right at 100 units/s from where it was placed, computed from this.age.
        var slide = @"
meta = { name: 'Slide', params: { speed: { type: 'number', default: 100 }, size: 40 } };
function draw() {
  rect(this.x + this.age / 1000 * this.speed, this.y, this.size, this.size, { rot: 0 });
  circle(this.x, this.y, 10, { hurts: false, front: true, color: '#ffffff' });
}";
        var level = Level("{\"id\":\"a\",\"start\":1000,\"end\":5000,\"script\":{\"name\":\"slide\",\"params\":{\"speed\":200}},\"tracks\":{\"x\":[[0,-100]],\"y\":[[0,50]]}}",
            new() { ["slide"] = slide });
        var ev = new LevelEvaluator(level);
        var items = new List<DrawItem>();
        ev.Collect(500, items);
        check(items.Count == 0, "a script draws nothing before it starts");
        ev.Collect(2000, items);
        check(items.Count == 2, $"a script draws what it asks for ({items.Count})");
        var bar = items.First(i => i.Hits);
        near(bar.Transform.Tx, -100 + 200, "params reach the script and age is the time since it started", 1e-6);
        near(bar.Transform.Ty, 50, "the script starts where the object is placed", 1e-6);
        check(items.Any(i => i.Front && !i.Hits), "front shapes never hurt");
        check(ev.FirstHit(items, new Vec2(100, 50), 8, out var hit) && hit.Object.Id == "a", "a script shape hurts players");
        check(!ev.FirstHit(items, new Vec2(100, 150), 8, out _), "and only where it is");
        var sp = new ScriptParamCheck(level.ScriptSources["slide"]);
        check(sp.Ok, "meta.params are read: " + sp.Info);

        // Calls in the wrong function do nothing, and say so once.
        var misplaced = @"
function update(dt) { rect(0, 0, 10, 10); camera.move(5, 0); }
function draw() { camera.kick(10, 0, 200); camera.kick(10, 0, 200); square(0, 0, 10); }";
        level = Level("{\"id\":\"m\",\"start\":0,\"end\":5000,\"script\":{\"name\":\"misplaced\"}}", new() { ["misplaced"] = misplaced });
        ev = new LevelEvaluator(level);
        ev.Collect(500, items);
        ev.Collect(1000, items);
        var warnings = ev.Scripts.Logs.Where(l => l.Contains("warning:")).ToList();
        check(items.Count == 1, $"only the shape drawn in draw() shows ({items.Count})");
        check(warnings.Count == 3 && warnings.Any(w => w.Contains("draw()")) && warnings.Any(w => w.Contains("camera.move")) && warnings.Any(w => w.Contains("camera.kick")),
            "misplaced calls warn once each: " + string.Join(" | ", warnings));
        near(ev.CameraAt(1000).X, 0, "a kick asked for in draw() does nothing", 1e-9);

        // update(): state, spawning, killing, random, and a rewind repeating it exactly.
        var spawner = @"
meta = { name: 'Spawner' };
function start(p) { this.n = 0; }
function update(dt) {
  if (this.age >= this.n * 500) { spawn('dot', { vx: random(-100, 100) }); this.n++; }
  if (this.n > 3) kill();
}";
        var dot = @"
function start(p) { this.vx = p.vx; this.px = this.x; }
function update(dt) { this.px += this.vx * dt / 1000; if (this.age > 1200) kill(); }
function draw() { square(this.px, this.y, 12); }";
        level = Level("{\"id\":\"s\",\"start\":0,\"end\":10000,\"script\":{\"name\":\"spawner\"}}", new() { ["spawner"] = spawner, ["dot"] = dot });
        ev = new LevelEvaluator(level);
        ev.Collect(1600, items);
        int atFirst = items.Count;
        var xs = items.Select(i => Math.Round(i.Transform.Tx, 6)).OrderBy(v => v).ToArray();
        check(atFirst == 3, $"spawned instances run and kill themselves (at 1.6 s: {atFirst})");
        ev.Collect(3000, items);
        ev.Collect(1600, items);
        var xs2 = items.Select(i => Math.Round(i.Transform.Tx, 6)).OrderBy(v => v).ToArray();
        check(xs.SequenceEqual(xs2), "going back in time repeats the same run (random included)");
        ev.Seed = 12345;
        ev.Collect(1600, items);
        var xs3 = items.Select(i => Math.Round(i.Transform.Tx, 6)).OrderBy(v => v).ToArray();
        check(!xs.SequenceEqual(xs3), "a new play seed gives a different run");

        // Camera, screen, players and sounds.
        var fx = @"
function start() { camera.kick(0, -20, 400); screen.flash('#000000', 1, 200, 100); sound('playerDeath'); }
function draw() { camera.zoom(2, 100, 0); camera.rotate(5); setPlayerSpeed(0.5, 0.25); screen.background('#102030'); }";
        level = Level("{\"id\":\"f\",\"start\":1000,\"end\":3000,\"script\":{\"name\":\"fx\"}}", new() { ["fx"] = fx });
        ev = new LevelEvaluator(level);
        ev.Collect(1000, items);
        var cam = ev.CameraAt(1000);
        near(cam.Scale, 2, "scripts zoom the camera", 1e-9);
        near(cam.Rotation, 5, "scripts turn the camera", 1e-9);
        near(cam.X, 100 * (1 - 2), "a zoom on a point keeps the point in place", 1e-9);
        near(cam.Y, -20, "a kick starts at full strength", 1e-6);
        near(ev.CameraAt(1200).Y, -20 * (1 - Ease.Apply(Ease.Kind.OutQuad, 0.5)), "and eases back", 1e-6);
        var fl = new List<Rgba>();
        ev.Flashes(1050, fl);
        check(fl.Count == 1 && Math.Abs(fl[0].A - 1) < 1e-6, "a flash holds");
        ev.Flashes(1200, fl);
        check(fl.Count == 1 && Math.Abs(fl[0].A - 0.5) < 1e-6, "then fades");
        near(ev.PlayerSpeedAt(1500), 0.5, "scripts slow the players", 1e-9);
        near(ev.PlayerTurnAt(1500), 0.25, "and their turning", 1e-9);
        check(Math.Abs(ev.BackgroundAt(1500).B - 0x30 / 255f) < 1e-3, "scripts set the background");
        var sig = new List<ScriptSignal>();
        ev.ScriptSignals(900, 1100, sig);
        check(sig.Count == 1 && sig[0].Kind == "sound" && sig[0].Name == "playerDeath", "sounds are signalled at their time");
        ev.ScriptSignals(1100, 2000, sig);
        check(sig.Count == 0, "and only once");
        near(ev.PlayerSpeedAt(3500), 1, "nothing is asked once the script is over", 1e-9);

        // Players are visible to scripts.
        var chase = @"function draw() { var p = nearestPlayer(this.x, this.y); if (p) rect(p.x, p.y, 10, 10); }";
        level = Level("{\"id\":\"c\",\"start\":0,\"end\":3000,\"script\":{\"name\":\"chase\"}}", new() { ["chase"] = chase });
        ev = new LevelEvaluator(level);
        var pl = new PlayerList();
        pl.Players.Add(new PlayerInfo { Number = 1, Position = new Vec2(33, -44), Alive = true, Color = new Rgba(0, 1, 1) });
        ev.Players = pl;
        ev.Collect(100, items);
        check(items.Count == 1 && Math.Abs(items[0].Transform.Tx - 33) < 1e-9 && Math.Abs(items[0].Transform.Ty + 44) < 1e-9, "scripts see the players");

        // Custom polygons and text.
        var shapes = @"function draw() { shape([[0,0],[100,0],[100,100],[50,40],[0,100]]); text('HI', 0, 200, 30, { hurts: true }); }";
        level = Level("{\"id\":\"p\",\"start\":0,\"end\":3000,\"script\":{\"name\":\"shapes\"}}", new() { ["shapes"] = shapes });
        ev = new LevelEvaluator(level);
        ev.Collect(100, items);
        check(items.Count == 2, "shape() and text() draw");
        check(ev.FirstHit(items, new Vec2(10, 80), 2, out _), "a concave shape hurts inside");
        check(!ev.FirstHit(items, new Vec2(50, 80), 2, out _), "but not in its notch");

        // Errors: compile errors with their line, runtime errors, endless loops.
        var broken = "function draw() {\n  rect(0, 0, 10, 10);\n  this is not javascript\n}";
        var lib = new ScriptLibrary(new Dictionary<string, string> { ["broken"] = broken });
        var err = lib.Get("broken").CompileError;
        check(err != null && err.Line == 3, $"compile errors name their line ({err})");
        var thrower = "function draw() {\n  rect(0, 0, 10, 10);\n  undefinedFunction();\n}";
        var loop = "function update() { while (true) {} }";
        level = Level("{\"id\":\"e\",\"start\":0,\"end\":3000,\"script\":{\"name\":\"thrower\"}},{\"id\":\"l\",\"start\":0,\"end\":3000,\"script\":{\"name\":\"loop\"}},{\"id\":\"m\",\"start\":0,\"end\":3000,\"script\":{\"name\":\"missing\"}}",
            new() { ["thrower"] = thrower, ["loop"] = loop });
        ev = new LevelEvaluator(level);
        var t0 = DateTime.UtcNow;
        ev.Collect(100, items);
        double secs = (DateTime.UtcNow - t0).TotalSeconds;
        var errs = ev.Scripts.Errors;
        check(errs.Any(x => x.Script == "thrower" && x.Line == 3), "runtime errors name their line: " + string.Join(" | ", errs));
        check(errs.Any(x => x.Script == "loop" && x.Message.Contains("too long")), "endless loops are stopped");
        check(errs.Any(x => x.Script == "missing"), "a missing script is reported");
        check(secs < 6, $"an endless loop does not hang the level ({secs:0.0} s)");
        ev.Collect(200, items);
        check(items.Count == 0, "a failed script stops drawing");

        // No access to the clock or the outside world.
        var sneaky = "function draw() { if (typeof Date !== 'undefined' || typeof require !== 'undefined' || typeof System !== 'undefined') rect(0,0,10,10); }";
        level = Level("{\"id\":\"x\",\"start\":0,\"end\":3000,\"script\":{\"name\":\"sneaky\"}}", new() { ["sneaky"] = sneaky });
        ev = new LevelEvaluator(level);
        ev.Collect(100, items);
        check(items.Count == 0, "scripts have no clock, require or .NET");

        // Patterns build ordinary objects.
        var pattern = @"
meta = { name: 'Ring of dots', kind: 'pattern', params: { count: { type: 'int', default: 4 } } };
function build(p) {
  for (var i = 0; i < p.count; i++) {
    var a = rad(360 * i / p.count);
    add({ name: 'dot', shape: 'circle', start: p.hit, end: p.hit + beat(2), x: p.x + Math.cos(a) * 100, y: p.y, w: 20, h: 20 });
  }
}";
        lib = new ScriptLibrary(new Dictionary<string, string> { ["ring"] = pattern });
        var type = lib.Get("ring");
        check(type.Kind == ScriptKind.Pattern && type.Params.Count == 1, "a pattern declares its kind and params");
        int n = 0;
        var built = ScriptHost.BuildPattern(type, new Dictionary<string, object> { ["count"] = 3.0, ["hit"] = 2000.0, ["x"] = 0.0, ["y"] = 0.0 }, 120,
            () => "o" + (++n), null, out var berr);
        check(berr == null && built.Count == 3 && built[0].Contains("\"id\":\"o1\"") && built[0].Contains("\"end\":3000"), "build(p) adds objects: " + (berr?.ToString() ?? string.Join(" ", built.Take(1))));
    }

    /// <summary>Every script in library/ compiles; attacks and effects run for a few seconds, patterns build.</summary>
    public static void Library(Action<bool, string> check)
    {
        var dir = System.IO.Path.Combine(Environment.CurrentDirectory, "library");
        if (!System.IO.Directory.Exists(dir)) { check(false, "library/ not found (run from the repo root)"); return; }
        var sources = new Dictionary<string, string>();
        foreach (var f in System.IO.Directory.GetFiles(dir, "*.js", System.IO.SearchOption.AllDirectories))
            sources[System.IO.Path.GetFileNameWithoutExtension(f)] = System.IO.File.ReadAllText(f);
        var lib = new ScriptLibrary(sources);
        foreach (var e in lib.CompileErrors) check(false, "library script does not compile: " + e);
        var objects = new List<string>();
        int i = 0;
        foreach (var t in lib.All)
        {
            if (t.Kind != ScriptKind.Attack) continue;
            objects.Add($"{{\"id\":\"o{i++}\",\"start\":1000,\"end\":9000,\"script\":{{\"name\":\"{t.Name}\"}},\"tracks\":{{\"x\":[[0,100]],\"y\":[[0,-50]]}}}}");
        }
        var level = Level(string.Join(",", objects), sources);
        var ev = new LevelEvaluator(level);
        var pl = new PlayerList();
        pl.Players.Add(new PlayerInfo { Number = 1, Position = new Vec2(-300, 100), Alive = true, Color = new Rgba(0, 1, 1) });
        ev.Players = pl;
        var items = new List<DrawItem>();
        int drawn = 0;
        for (double tm = 1000; tm < 9000; tm += 250) { ev.Collect(tm, items); drawn += items.Count; ev.CameraAt(tm); }
        foreach (var e in ev.Scripts.Errors) check(false, "library script fails while running: " + e);
        check(drawn > 0, $"library attacks draw ({drawn} shapes over the run)");
        int n = 0;
        foreach (var t in lib.All)
        {
            if (t.Kind != ScriptKind.Pattern) continue;
            var built = ScriptHost.BuildPattern(t, new Dictionary<string, object> { ["hit"] = 4000.0, ["x"] = 0.0, ["y"] = 0.0 }, 120, () => "p" + (++n), null, out var err);
            check(err == null && built.Count > 0, $"pattern {t.Name} builds ({err?.ToString() ?? built.Count + " objects"})");
            foreach (var json in built)
            {
                try { LevelData.Parse("{\"format\":\"beatform.level\",\"meta\":{\"id\":\"t\",\"audio\":\"a.ogg\"},\"objects\":[" + json + "]}"); }
                catch (Exception ex) { check(false, $"pattern {t.Name} made an object the loader cannot read: {ex.Message}"); break; }
            }
        }
    }

    private sealed class ScriptParamCheck
    {
        public bool Ok;
        public string Info;
        public ScriptParamCheck(string src)
        {
            var t = new ScriptType("slide", src);
            var speed = t.Params.FirstOrDefault(p => p.Key == "speed");
            var size = t.Params.FirstOrDefault(p => p.Key == "size");
            Ok = t.Title == "Slide" && speed != null && speed.Default is double d && d == 100 && size != null && size.Type == "number";
            Info = $"{t.Title} {t.Params.Count} params";
        }
    }
}
