using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Beatform.Core;

/// <summary>What a script asked to draw this frame.</summary>
public struct ScriptDraw
{
    public ShapeGeometry Geometry;
    public Affine Transform;
    public Rgba Color;
    public int Layer;
    public bool Hurts, Front, Warning;
    /// <summary>Draw order within the instance.</summary>
    public int Order;
}

/// <summary>Something a script did at a song time that the game acts on once: a sound, a hit, a teleport.</summary>
public struct ScriptSignal
{
    public double T;
    /// <summary>"sound", "hit" or "move".</summary>
    public string Kind;
    public string Name;
    public double Volume;
    public int Player;
    public double X, Y;
}

/// <summary>The camera, screen and player changes all running scripts ask for this frame.</summary>
public sealed class ScriptFrame
{
    public double CamX, CamY, CamRot, Zoom = 1, ZoomX, ZoomY;
    public bool ZoomFocus;
    public readonly List<Rgba> Flashes = new();
    public Rgba? Background;
    public double PlayerSpeed = 1, PlayerTurn = 1;

    public void Clear()
    {
        CamX = CamY = CamRot = ZoomX = ZoomY = 0;
        Zoom = 1;
        ZoomFocus = false;
        Flashes.Clear();
        Background = null;
        PlayerSpeed = PlayerTurn = 1;
    }
}

internal enum ScriptPhase { None, Start, Update, Draw, Build }

/// <summary>The state the native API functions work on during a call into a script (one at a time).</summary>
internal sealed class ScriptContext
{
    [ThreadStatic] public static ScriptContext Current;

    public ScriptWorld World;
    public ScriptInstance Instance;
    public ScriptPhase Phase;
    public double Time;
    public List<ScriptDraw> Draws;
    public ScriptFrame Frame;
    public int FrameNumber;
    public string PlayersJson = "[]";
    public Rgba Hazard = new(1, 0x2a / 255f, 0x6d / 255f);
    public Rgba PlayerColor = LevelData.DefaultPlayerColor;
    public List<string> Logs;
    /// <summary>Pattern builds: the objects added, as JSON, and a function that names them.</summary>
    public List<string> Added;
    public Func<string> NewId;
}

/// <summary>The native functions behind the JavaScript API (ScriptPrelude), installed in every script engine.</summary>
internal static class ScriptApi
{
    public const int MaxDrawsPerFrame = 50_000;
    public const int MaxLogs = 500;

    public static void Install(Engine e)
    {
        e.SetValue("__draw", new Action<int, double, double, double, double, double, string, double, int, int, double, double, string>(Draw));
        e.SetValue("__polygon", new Action<string, string, double, int, int>(Polygon));
        e.SetValue("__textWidth", new Func<string, double, int, double>((s, size, bold) => TextFont.Width(s, bold != 0) * size));
        e.SetValue("__frame", new Func<int>(() => C?.FrameNumber ?? 0));
        e.SetValue("__playersJson", new Func<string>(() => C?.PlayersJson ?? "[]"));
        e.SetValue("__hit", new Action<int>(n => Signal(new ScriptSignal { Kind = "hit", Player = n })));
        e.SetValue("__move", new Action<int, double, double>((n, x, y) => Signal(new ScriptSignal { Kind = "move", Player = n, X = x, Y = y })));
        e.SetValue("__sound", new Action<string, double>((n, v) => Signal(new ScriptSignal { Kind = "sound", Name = n, Volume = v })));
        e.SetValue("__speed", new Action<double, double>(Speed));
        e.SetValue("__cam", new Action<int, double, double, double>(Cam));
        e.SetValue("__effect", new Action<int, double, double, double, double, string, string>(Effect));
        e.SetValue("__background", new Action<string>(Background));
        e.SetValue("__spawn", new Action<string, string, double, double>(Spawn));
        e.SetValue("__kill", new Action(() => { if (C?.Instance != null) C.Instance.Dead = true; }));
        e.SetValue("__random", new Func<double>(() => C?.World?.NextRandom() ?? 0.5));
        e.SetValue("__ease", new Func<string, double, double>((name, t) => Ease.Apply(Ease.Parse(name), Math.Clamp(t, 0, 1))));
        e.SetValue("__log", new Action<string>(Log));
        e.SetValue("__add", new Func<string, string>(Add));
        e.SetValue("bpm", 120);
        e.SetValue("beatMs", 500);
        e.Execute(ScriptPrelude.Source, "prelude.js");
        // No wall clock: the same play must give the same result every time.
        e.Execute("delete globalThis.Date;", "prelude-clean.js");
    }

    private static ScriptContext C => ScriptContext.Current;

    private static readonly ShapeDef rectDef = new() { Type = ShapeType.Rect };
    private static readonly ShapeDef circleDef = new() { Type = ShapeType.Circle };

    private static void Draw(int kind, double x, double y, double w, double h, double rot, string color, double alpha,
        int flags, int layer, double a, double b, string text)
    {
        var c = C;
        if (c == null || c.Phase != ScriptPhase.Draw || c.Draws == null || c.Draws.Count >= MaxDrawsPerFrame) return;
        ShapeDef def;
        switch (kind)
        {
            case 0: def = rectDef; break;
            case 1: def = circleDef; break;
            case 2: def = new ShapeDef { Type = ShapeType.Polygon, Sides = (int)Math.Clamp(a, 3, 64) }; break;
            case 3: def = new ShapeDef { Type = ShapeType.Star, Points = (int)Math.Clamp(a, 3, 64), Inner = Math.Clamp(b, 0.05, 0.99) }; break;
            case 4: def = new ShapeDef { Type = ShapeType.Ring, Thickness = Math.Clamp(a, 0.01, 0.5) }; break;
            case 5: def = new ShapeDef { Type = ShapeType.Spikes, Spikes = (int)Math.Clamp(a, 3, 64), Inner = Math.Clamp(b, 0.05, 0.99) }; break;
            case 6:
            {
                bool bold = (flags & 32) == 0;
                def = new ShapeDef { Type = ShapeType.Text, Text = text ?? "", Bold = bold };
                w = TextFont.Width(text ?? "", bold) * h;
                // Centred by default; left or right aligned on x.
                double shift = (flags & 8) != 0 ? w / 2 : (flags & 16) != 0 ? -w / 2 : 0;
                double r = rot * Math.PI / 180;
                x += Math.Cos(r) * shift;
                y += Math.Sin(r) * shift;
                break;
            }
            default: return;
        }
        Add(c, ShapeGeometry.For(def), Scaled(x, y, rot, w, h), color, alpha, flags, layer);
    }

    private static void Polygon(string points, string color, double alpha, int flags, int layer)
    {
        var c = C;
        if (c == null || c.Phase != ScriptPhase.Draw || c.Draws == null || c.Draws.Count >= MaxDrawsPerFrame) return;
        var parts = (points ?? "").Split(',');
        var pts = new List<Vec2>();
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var px)
                && double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var py))
                pts.Add(new Vec2(px, py));
        }
        var g = PolygonFill.Build(pts);
        if (g?.Placement is not (double px0, double py0, double pw, double ph)) return;
        Add(c, g, Scaled(px0, py0, 0, pw, ph), color, alpha, flags, layer);
    }

    private static void Add(ScriptContext c, ShapeGeometry g, Affine m, string color, double alpha, int flags, int layer)
    {
        var col = ParseColor(color, c);
        col.A = (float)(col.A * Math.Clamp(alpha, 0, 1));
        c.Draws.Add(new ScriptDraw
        {
            Geometry = g, Transform = m, Color = col, Layer = layer,
            Hurts = (flags & 1) != 0 && (flags & 2) == 0 && (flags & 4) == 0,
            Front = (flags & 2) != 0, Warning = (flags & 4) != 0, Order = c.Draws.Count,
        });
    }

    internal static Rgba ParseColor(string color, ScriptContext c)
    {
        if (string.IsNullOrEmpty(color)) return c.Hazard;
        if (color == "player" || color == "@player") return c.PlayerColor;
        return Rgba.Parse(color, c.Hazard);
    }

    internal static Affine Scaled(double x, double y, double deg, double w, double h)
    {
        double r = deg * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        return new Affine { A = cos * w, B = sin * w, C = -sin * h, D = cos * h, Tx = x, Ty = y };
    }

    private static void Signal(ScriptSignal s)
    {
        var c = C;
        if (c?.World == null || c.Phase == ScriptPhase.Draw || c.Phase == ScriptPhase.Build) return;
        s.T = c.Time;
        c.World.Signals.Add(s);
    }

    private static void Speed(double speed, double turn)
    {
        var f = C?.Frame;
        if (f == null || C.Phase != ScriptPhase.Draw) return;
        f.PlayerSpeed = Math.Min(f.PlayerSpeed, Math.Clamp(speed, 0, 2));
        f.PlayerTurn = Math.Min(f.PlayerTurn, Math.Clamp(turn, 0.001, 1));
    }

    private static void Cam(int kind, double a, double b, double c2)
    {
        var f = C?.Frame;
        if (f == null || C.Phase != ScriptPhase.Draw) return;
        switch (kind)
        {
            case 0: f.CamX += a; f.CamY += b; break;
            case 1: f.CamRot += a; break;
            case 2: f.Zoom *= Math.Clamp(a, 0.05, 20); break;
            case 3: f.Zoom *= Math.Clamp(a, 0.05, 20); f.ZoomX = b; f.ZoomY = c2; f.ZoomFocus = true; break;
        }
    }

    private static void Effect(int kind, double a, double b, double c2, double ms, string ease, string color)
    {
        var c = C;
        if (c?.World == null || c.Phase == ScriptPhase.Build) return;
        if (kind == 4 && ms < 0)
        {
            // A flash for this frame only.
            if (c.Phase == ScriptPhase.Draw && c.Frame != null)
            {
                var col = ParseColor(color, c);
                col.A = (float)(col.A * Math.Clamp(a, 0, 1));
                c.Frame.Flashes.Add(col);
            }
            return;
        }
        // Timed effects belong to the world, so they repeat exactly after a rewind. In draw they are
        // ignored (draw may run any number of times per step).
        if (c.Phase == ScriptPhase.Draw) return;
        c.World.Effects.Add(new ScriptEffect
        {
            Kind = kind, A = a, B = b, Start = c.Time, Duration = Math.Max(1, ms), Ease = Ease.Parse(ease),
            Color = kind == 4 ? ParseColor(color, c) : default, Seed = c.World.Effects.Count,
        });
    }

    private static void Background(string color)
    {
        var c = C;
        if (c?.Frame == null || c.Phase != ScriptPhase.Draw) return;
        c.Frame.Background = ParseColor(color, c);
    }

    private static void Spawn(string name, string paramsJson, double x, double y)
    {
        var c = C;
        if (c?.World == null || c.Phase == ScriptPhase.Draw || c.Phase == ScriptPhase.Build) return;
        c.World.Spawn(name, paramsJson, x, y, c.Time, c.Instance);
    }

    private static void Log(string s)
    {
        var c = C;
        if (c?.Logs == null) return;
        if (c.Logs.Count >= MaxLogs) c.Logs.RemoveRange(0, MaxLogs / 5);
        string who = c.Instance?.Type.Name ?? "script";
        c.Logs.Add($"[{who} {c.Time / 1000:0.000}s] {s}");
    }

    private static string Add(string json)
    {
        var c = C;
        if (c?.Added == null || c.Phase != ScriptPhase.Build) throw new JavaScriptException("add() only works in a pattern's build(p)");
        string id = c.NewId?.Invoke() ?? ("s" + c.Added.Count);
        c.Added.Add(InsertId(json, id));
        return id;
    }

    /// <summary>Gives an added object its id unless it named one itself.</summary>
    private static string InsertId(string json, string id)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
        if (node == null) throw new JavaScriptException("add() needs an object");
        if (node["id"] == null) node["id"] = id;
        return node.ToJsonString();
    }

    /// <summary>JavaScript value to a plain .NET value for parameters (number, string, bool, or null).</summary>
    public static object ToClr(JsValue v)
    {
        if (v.IsNumber()) return v.AsNumber();
        if (v.IsString()) return v.AsString();
        if (v.IsBoolean()) return v.AsBoolean();
        return null;
    }

    public static ScriptError ToError(string script, Exception e)
    {
        var err = new ScriptError { Script = script, Message = e.Message };
        if (e is JavaScriptException js)
        {
            err.Line = js.Location.Start.Line;
            err.Message = js.Error.IsObject() ? TypeConverter.ToString(js.Error.AsObject().Get("message")) : js.Message;
        }
        else if (e is Acornima.ParseErrorException pe)
        {
            err.Line = pe.LineNumber;
            err.Message = pe.Description ?? pe.Message;
        }
        else if (e is TimeoutException || e.GetType().Name.Contains("StatementsCount") || e.GetType().Name.Contains("MemoryLimit"))
        {
            err.Message = "took too long or used too much memory (an endless loop?)";
        }
        else if (e.GetType().Name.Contains("Recursion"))
        {
            err.Message = "calls itself too deeply";
        }
        if (string.IsNullOrEmpty(err.Message)) err.Message = e.GetType().Name;
        return err;
    }

    /// <summary>Parameters as JSON for the script's start(p).</summary>
    public static string ParamsJson(Dictionary<string, object> values)
    {
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var kv in values)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(System.Text.Json.JsonSerializer.Serialize(kv.Key)).Append(':');
            sb.Append(kv.Value switch
            {
                null => "null",
                bool bv => bv ? "true" : "false",
                double d => double.IsFinite(d) ? d.ToString("R", CultureInfo.InvariantCulture) : "0",
                int i => i.ToString(CultureInfo.InvariantCulture),
                string s => System.Text.Json.JsonSerializer.Serialize(s),
                _ => System.Text.Json.JsonSerializer.Serialize(kv.Value.ToString()),
            });
        }
        return sb.Append('}').ToString();
    }
}
