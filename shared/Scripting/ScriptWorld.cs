using System;
using System.Collections.Generic;
using Jint;
using Jint.Native;

namespace Beatform.Core;

internal sealed class ScriptInstance
{
    public ScriptType Type;
    public JsValue Self;
    public double SpawnT;
    public bool Dead;
}

/// <summary>A camera kick, shake or flash a script started; it plays out on its own.</summary>
internal struct ScriptEffect
{
    /// <summary>0 kick, 1 kick rotate, 2 kick zoom, 3 shake, 4 flash.</summary>
    public int Kind;
    public double A, B, Start, Duration;
    public Ease.Kind Ease;
    public Rgba Color;
    public int Seed;

    public double End => Start + (Kind == 4 ? B : 0) + Duration;
}

/// <summary>
/// One script object of the level and everything it spawned, run in fixed steps of 1/60 s from the
/// object's start. Going back in time starts it over (same seed, so the same result); jumping ahead
/// runs the steps in between. draw() sees the exact time, so motion computed from this.age is smooth.
/// </summary>
internal sealed class ScriptWorld
{
    public const double StepMs = 1000.0 / 60;
    public const int MaxInstances = 4000;
    /// <summary>A jump further than this is not caught up step by step (the level skips ahead instead).</summary>
    public const int MaxCatchUpSteps = 60 * 600;

    public readonly LevelObject Obj;
    public readonly List<ScriptInstance> Instances = new();
    public readonly List<ScriptSignal> Signals = new();
    public readonly List<ScriptEffect> Effects = new();
    public ScriptError Error;

    private readonly ScriptLibrary lib;
    private readonly ScriptHost host;
    private long steps = -1;
    private ulong rng;
    private ulong seed;

    public ScriptWorld(LevelObject obj, ScriptLibrary lib, ScriptHost host)
    {
        Obj = obj;
        this.lib = lib;
        this.host = host;
    }

    public double NextRandom()
    {
        // splitmix64
        rng += 0x9E3779B97F4A7C15UL;
        ulong z = rng;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53));
    }

    public void Invalidate() => steps = -1;

    private void Reset(int playSeed)
    {
        Instances.Clear();
        Signals.Clear();
        Effects.Clear();
        Error = null;
        seed = (ulong)(LevelEvaluator.Random01(playSeed, "script/" + Obj.Id) * ulong.MaxValue);
        rng = seed;
        steps = 0;
        var type = lib.Get(Obj.Script.Name);
        if (type == null) { Error = new ScriptError { Script = Obj.Script.Name, Message = "no script with this name in scripts/" }; host.Report(Error); return; }
        if (type.CompileError != null) { Error = type.CompileError; return; }
        if (type.Kind != ScriptKind.Attack) { Error = new ScriptError { Script = type.Name, Message = "a pattern script cannot be placed as an attack" }; host.Report(Error); return; }
        var origin = host.OriginOf(Obj, Obj.Start);
        var p = type.MergeParams(Obj.Script.Params);
        Create(type, ScriptApi.ParamsJson(p), origin.X, origin.Y, origin.Rot, Obj.Start, null);
    }

    public void Spawn(string name, string paramsJson, double x, double y, double t, ScriptInstance parent)
    {
        if (Instances.Count >= MaxInstances) return;
        var type = lib.Get(name);
        if (type == null) throw new Jint.Runtime.JavaScriptException($"spawn: no script named '{name}'");
        if (type.CompileError != null) throw new Jint.Runtime.JavaScriptException($"spawn: '{name}' does not compile: {type.CompileError.Message}");
        if (type.Kind != ScriptKind.Attack) throw new Jint.Runtime.JavaScriptException($"spawn: '{name}' is a pattern");
        double px = double.IsNaN(x) ? Num(parent?.Self, "x") : x;
        double py = double.IsNaN(y) ? Num(parent?.Self, "y") : y;
        var given = ParseParams(paramsJson);
        Create(type, ScriptApi.ParamsJson(type.MergeParams(given)), px, py, 0, t, parent);
    }

    private static double Num(JsValue self, string key)
    {
        if (self == null || !self.IsObject()) return 0;
        var v = self.AsObject().Get(key);
        return v.IsNumber() ? v.AsNumber() : 0;
    }

    private static Dictionary<string, object> ParseParams(string json)
    {
        var res = new Dictionary<string, object>();
        if (string.IsNullOrEmpty(json)) return res;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.EnumerateObject())
            res[p.Name] = ScriptRef.ToClr(p.Value);
        return res;
    }

    private void Create(ScriptType type, string paramsJson, double x, double y, double rot, double t, ScriptInstance parent)
    {
        var inst = new ScriptInstance { Type = type, SpawnT = t };
        Instances.Add(inst);
        var c = ScriptContext.Current;
        var prevInst = c.Instance;
        var prevPhase = c.Phase;
        c.Instance = inst;
        c.Phase = ScriptPhase.Start;
        c.Time = t;
        try
        {
            host.Prepare(type);
            inst.Self = type.Engine.Invoke("__new");
            type.Engine.Invoke("__start", inst.Self, paramsJson, x, y, rot, t);
        }
        catch (Exception e) { Fail(inst, e); }
        finally
        {
            c.Instance = prevInst;
            c.Phase = prevPhase;
        }
    }

    private void Fail(ScriptInstance inst, Exception e)
    {
        inst.Dead = true;
        Error ??= ScriptApi.ToError(inst.Type.Name, e);
        host.Report(Error);
    }

    public void AdvanceTo(double t, int playSeed)
    {
        double local = t - Obj.Start;
        long target = (long)Math.Floor(local / StepMs + 1e-9);
        if (steps < 0 || target < steps) Reset(playSeed);
        if (target - steps > MaxCatchUpSteps) steps = target - MaxCatchUpSteps;
        var c = ScriptContext.Current;
        while (steps < target)
        {
            steps++;
            double st = Obj.Start + steps * StepMs;
            int n = Instances.Count;
            for (int i = 0; i < n; i++)
            {
                var inst = Instances[i];
                if (inst.Dead || !inst.Type.HasUpdate) continue;
                c.Instance = inst;
                c.Phase = ScriptPhase.Update;
                c.Time = st;
                try
                {
                    host.Prepare(inst.Type);
                    inst.Type.Engine.Invoke("__update", inst.Self, st - inst.SpawnT, st);
                }
                catch (Exception e) { Fail(inst, e); }
            }
            Instances.RemoveAll(x => x.Dead);
            Effects.RemoveAll(e => e.End < st - 1000);
        }
        c.Instance = null;
        c.Phase = ScriptPhase.None;
    }

    public void Draw(double t, List<ScriptDraw> draws, ScriptFrame frame)
    {
        var c = ScriptContext.Current;
        c.Draws = draws;
        c.Frame = frame;
        foreach (var inst in Instances)
        {
            if (inst.Dead || inst.Type.DrawFn.IsUndefined()) continue;
            c.Instance = inst;
            c.Phase = ScriptPhase.Draw;
            c.Time = t;
            try
            {
                host.Prepare(inst.Type);
                inst.Type.Engine.Invoke("__drawCall", inst.Self, t - inst.SpawnT, t);
            }
            catch (Exception e) { Fail(inst, e); }
        }
        c.Instance = null;
        c.Phase = ScriptPhase.None;
        foreach (var e in Effects) ApplyEffect(e, t, frame);
    }

    private static void ApplyEffect(in ScriptEffect e, double t, ScriptFrame f)
    {
        if (t < e.Start || t >= e.End) return;
        double p = Math.Clamp((t - e.Start) / e.Duration, 0, 1);
        double left = 1 - Core.Ease.Apply(e.Ease, p);
        switch (e.Kind)
        {
            case 0: f.CamX += e.A * left; f.CamY += e.B * left; break;
            case 1: f.CamRot += e.A * left; break;
            case 2: f.Zoom *= 1 + (e.A - 1) * left; break;
            case 3:
            {
                double a = 2 * Math.PI * LevelEvaluator.Random01(e.Seed, "shake/" + Math.Floor((t - e.Start) / 50));
                double m = e.A * (1 - p);
                f.CamX += Math.Cos(a) * m;
                f.CamY += Math.Sin(a) * m;
                break;
            }
            case 4:
            {
                double fade = t < e.Start + e.B ? 0 : Math.Clamp((t - e.Start - e.B) / e.Duration, 0, 1);
                var col = e.Color;
                col.A = (float)(col.A * e.A * (1 - fade));
                f.Flashes.Add(col);
                break;
            }
        }
    }
}

/// <summary>
/// Runs a level's script objects for the evaluator: steps the worlds active at a time, collects what
/// they draw and what they do to the camera, the screen and the players.
/// </summary>
public sealed class ScriptHost
{
    private readonly LevelEvaluator ev;
    private readonly ScriptLibrary lib;
    private readonly List<LevelObject> scriptObjects = new();
    private readonly Dictionary<LevelObject, ScriptWorld> worlds = new();
    private readonly Dictionary<LevelObject, List<ScriptDraw>> draws = new();
    private readonly ScriptContext context = new() { Logs = new List<string>() };
    private double frameT = double.NaN;
    private int frameNumber;

    /// <summary>What the scripts ask of the camera, screen and players at the last evaluated time.</summary>
    public readonly ScriptFrame Frame = new();
    /// <summary>log() output, oldest first (capped).</summary>
    public List<string> Logs => context.Logs;
    /// <summary>Errors scripts ran into, each reported once.</summary>
    public readonly List<ScriptError> Errors = new();
    public ScriptLibrary Library => lib;
    public event Action<ScriptError> ErrorReported;

    public ScriptHost(LevelEvaluator evaluator, ScriptLibrary library)
    {
        ev = evaluator;
        lib = library;
        foreach (var o in evaluator.Level.Objects) if (o.Script != null) scriptObjects.Add(o);
        context.Hazard = evaluator.Level.Hazard;
        var bpm = evaluator.Level.Meta.Bpm;
        foreach (var t in lib.All)
        {
            if (t.Engine == null) continue;
            t.Engine.SetValue("bpm", bpm);
            t.Engine.SetValue("beatMs", 60000 / Math.Max(1, bpm));
        }
        foreach (var err in lib.CompileErrors) Report(err);
    }

    public bool Any => scriptObjects.Count > 0;

    internal void Prepare(ScriptType t) { }

    internal void Report(ScriptError e)
    {
        if (e == null) return;
        foreach (var x in Errors) if (x.Script == e.Script && x.Message == e.Message && x.Line == e.Line) return;
        Errors.Add(e);
        ErrorReported?.Invoke(e);
    }

    internal (double X, double Y, double Rot) OriginOf(LevelObject o, double t) => ev.ScriptOrigin(o, t);

    /// <summary>Forgets all runs, so the scripts start over (a new seed, or the players changed).</summary>
    public void Reset()
    {
        foreach (var w in worlds.Values) w.Invalidate();
        frameT = double.NaN;
    }

    /// <summary>Runs the scripts active at t and gathers their output (once per time).</summary>
    public void Evaluate(double t)
    {
        if (t == frameT) return;
        frameT = t;
        frameNumber++;
        Frame.Clear();
        draws.Clear();
        var prev = ScriptContext.Current;
        ScriptContext.Current = context;
        context.FrameNumber = frameNumber;
        context.PlayersJson = PlayersJson();
        context.Added = null;
        try
        {
            foreach (var o in scriptObjects)
            {
                if (o.Hidden || t < o.Start || t >= o.End) continue;
                if (!worlds.TryGetValue(o, out var w)) worlds[o] = w = new ScriptWorld(o, lib, this);
                context.World = w;
                w.AdvanceTo(t, ev.Seed);
                var list = new List<ScriptDraw>();
                w.Draw(t, list, Frame);
                draws[o] = list;
            }
        }
        finally
        {
            context.World = null;
            ScriptContext.Current = prev;
        }
    }

    /// <summary>What a script object drew at the last evaluated time (empty when it is not running).</summary>
    public List<ScriptDraw> DrawsOf(LevelObject o) => draws.TryGetValue(o, out var l) ? l : null;

    /// <summary>Sounds, hits and teleports the scripts made at times in (from, to].</summary>
    public void SignalsBetween(double from, double to, List<ScriptSignal> output)
    {
        output.Clear();
        foreach (var w in worlds.Values)
            foreach (var s in w.Signals)
                if (s.T > from && s.T <= to) output.Add(s);
        output.Sort((a, b) => a.T.CompareTo(b.T));
    }

    private string PlayersJson()
    {
        var ps = ev.Players;
        var sb = new System.Text.StringBuilder("[");
        context.PlayerColor = LevelData.DefaultPlayerColor;
        for (int i = 0; ps != null && i < ps.Count; i++)
        {
            var p = ps.Get(i);
            if (i == 0) context.PlayerColor = p.Color;
            if (i > 0) sb.Append(',');
            sb.Append("{\"number\":").Append(p.Number)
              .Append(",\"x\":").Append(ScriptLibrary.Num(p.Position.X))
              .Append(",\"y\":").Append(ScriptLibrary.Num(p.Position.Y))
              .Append(",\"alive\":").Append(p.Alive ? "true" : "false")
              .Append(",\"color\":\"").Append(p.Color.ToHex()).Append("\"}");
        }
        return sb.Append(']').ToString();
    }

    /// <summary>
    /// Runs a pattern script's build(p) and returns the objects it added, as level.json object JSON.
    /// <paramref name="newId"/> names each object (unless the script gave an id).
    /// </summary>
    public static List<string> BuildPattern(ScriptType type, IReadOnlyDictionary<string, object> values, double bpm,
        Func<string> newId, List<string> logs, out ScriptError error)
    {
        error = null;
        var res = new List<string>();
        if (type.CompileError != null) { error = type.CompileError; return res; }
        var ctx = new ScriptContext { Phase = ScriptPhase.Build, Added = res, NewId = newId, Logs = logs ?? new List<string>() };
        var prev = ScriptContext.Current;
        ScriptContext.Current = ctx;
        try
        {
            type.Engine.SetValue("bpm", bpm);
            type.Engine.SetValue("beatMs", 60000 / Math.Max(1, bpm));
            type.Engine.Invoke("__build", ScriptApi.ParamsJson(type.MergeParams(values)));
        }
        catch (Exception e) { error = ScriptApi.ToError(type.Name, e); }
        finally { ScriptContext.Current = prev; }
        return res;
    }
}
