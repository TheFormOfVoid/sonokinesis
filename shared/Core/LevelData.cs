using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Sonokinesis.Core;

public struct Rgba
{
    public float R, G, B, A;
    public Rgba(float r, float g, float b, float a = 1) { R = r; G = g; B = b; A = a; }

    public static Rgba Parse(string s, Rgba fallback)
    {
        if (string.IsNullOrEmpty(s) || s[0] != '#' || (s.Length != 7 && s.Length != 9)) return fallback;
        try
        {
            float C(int i) => int.Parse(s.Substring(i, 2), NumberStyles.HexNumber) / 255f;
            return new Rgba(C(1), C(3), C(5), s.Length == 9 ? C(7) : 1f);
        }
        catch { return fallback; }
    }

    public static Rgba Lerp(Rgba a, Rgba b, float t) =>
        new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

    public string ToHex()
    {
        static string H(float v) => ((int)Math.Round(Math.Clamp(v, 0, 1) * 255)).ToString("x2");
        return "#" + H(R) + H(G) + H(B) + (A < 1 ? H(A) : "");
    }
}

/// <summary>An object that runs a script (scripts/NAME.js in the level folder) with these parameters.</summary>
public sealed class ScriptRef
{
    public string Name = "";
    public Dictionary<string, object> Params = new();

    public static object ToClr(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetDouble(),
        JsonValueKind.String => v.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}

public struct Keyframe
{
    public double T;
    public double V;
    public Ease.Kind Ease;
}

public sealed class Track
{
    public Keyframe[] Keys = Array.Empty<Keyframe>();
    public double Default;

    public double Evaluate(double t)
    {
        var k = Keys;
        if (k.Length == 0) return Default;
        if (t < k[0].T) return k[0].V;
        if (t >= k[k.Length - 1].T) return k[k.Length - 1].V;
        // Binary search for the last key with key.T <= t.
        int lo = 0, hi = k.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (k[mid].T <= t) lo = mid; else hi = mid - 1;
        }
        var k0 = k[lo];
        var k1 = k[lo + 1];
        double span = k1.T - k0.T;
        if (span <= 0) return k1.V;
        double p = (t - k0.T) / span;
        return k0.V + (k1.V - k0.V) * Core.Ease.Apply(k1.Ease, p);
    }
}

public enum ShapeType { Rect, Circle, Polygon, Star, Ring, Spikes, Text }

public sealed class ShapeDef
{
    public ShapeType Type = ShapeType.Rect;
    public int Sides = 3;
    public int Points = 5;
    public double Inner = 0.5;
    public double Thickness = 0.12;
    public int Spikes = 12;
    public string Text = "";
    /// <summary>Text: Montserrat Bold (true) or Medium.</summary>
    public bool Bold = true;

    /// <summary>Key used to share precomputed geometry between objects with identical shapes.</summary>
    public string GeometryKey => Type switch
    {
        ShapeType.Rect => "rect",
        ShapeType.Circle => "circle",
        ShapeType.Polygon => $"poly{Sides}",
        ShapeType.Star => $"star{Points}_{Inner:0.###}",
        ShapeType.Ring => $"ring{Thickness:0.###}",
        ShapeType.Spikes => $"spikes{Spikes}_{Inner:0.###}",
        ShapeType.Text => $"text{(Bold ? "b" : "m")}_{Text}",
        _ => "rect",
    };
}

public sealed class LevelObject
{
    public string Id = "";
    public string Name = "";
    public ShapeDef Shape = new();
    public double Start, Warn, End, Outro;
    public bool Hitbox = true;
    public double HitboxScale = 1;
    public Rgba Color;
    public int Layer;
    public string ParentId;
    public LevelObject Parent;
    /// <summary>1–4 when the parent is a player ("@player", "@player2"...), 0 otherwise.</summary>
    public int PlayerAnchor;
    /// <summary>The colour is the colour of the anchored player (or player 1): "color": "@player".</summary>
    public bool ColorFromPlayer;
    /// <summary>Moves like a player instead of following its x/y tracks. Null for ordinary objects.</summary>
    public ImposterBehavior Imposter;
    /// <summary>Runs a script instead of drawing a shape (its x/y/rot at the start are where it spawns). Null otherwise.</summary>
    public ScriptRef Script;
    public double PivotX, PivotY;
    public Track X = new() { Default = 0 };
    public Track Y = new() { Default = 0 };
    public Track Rot = new() { Default = 0 };
    public Track W = new() { Default = 100 };
    public Track H = new() { Default = 100 };
    public Track Opacity = new() { Default = 1 };
    /// <summary>Position in the file; ties in layer keep this order.</summary>
    public int FileIndex;
    /// <summary>Random offsets per play: [min, max] added to every x / y / rot value. Null: none.</summary>
    public double[] RandomX, RandomY, RandomRot;
    /// <summary>Objects with the same random seed name get the same offsets (default: the id).</summary>
    public string RandomSeed;
    /// <summary>All must hold for the object to exist in a play. Null: always.</summary>
    public RandomChoice[] Choices;

    /// <summary>The current play's offsets and choice (set by LevelEvaluator.Seed).</summary>
    public double OffX, OffY, OffRot;
    public bool Hidden;

    public double ActiveStart => Start + Warn;
    public double ActiveEnd => End - Outro;
}

/// <summary>
/// One roll per play for a named group: a whole number from 0 to Count - 1. The object exists only
/// when the roll is in Show (if given) and not in Hide.
/// </summary>
public sealed class RandomChoice
{
    public string Group = "";
    public int Count = 2;
    public int[] Show, Hide;
}

public enum EventType { Shake, Flash, Background, Sway, Sound, PlayerSpeed, Credits, Zoom }

public sealed class LevelEvent
{
    public double T;
    public EventType Type;
    public double Duration;
    public double Intensity;
    public Rgba Color;
    public double Opacity = 1;
    public Ease.Kind Ease;
    /// <summary>Sway: amplitudes (degrees, units) and periods (ms).</summary>
    public double SwayRotation, SwayX, SwayY, Period = 2000, PeriodX = 2000, PeriodY = 2000;
    /// <summary>Sound: the name of a game sound (see spec) and its volume 0..1.</summary>
    public string Sound;
    public double Volume = 1;
    /// <summary>PlayerSpeed: how fast the players move, as a part of their normal speed (0..2).</summary>
    public double Speed = 1;
    /// <summary>PlayerSpeed: the part of the way to their heading the players turn each 1/60 s (1 = at once).</summary>
    public double Turn = 1;
    /// <summary>Flash: time at full opacity before it fades over its duration.</summary>
    public double Hold;
    /// <summary>Zoom: how much bigger everything is drawn (1 = normal); the point it zooms on is x, y.</summary>
    public double Scale = 1;
    /// <summary>Zoom: 1–4 to zoom on that player instead of x, y ("focus": "@player"); 0 otherwise.</summary>
    public int FocusPlayer;
}

public sealed class LevelMeta
{
    public string Id = "";
    public string Title = "";
    public string Artist = "";
    public string Author = "";
    public string Description = "";
    public string Audio = "";
    public double Bpm = 120;
    public int BeatsPerBar = 4;
    public double OffsetMs;
    public double? LengthMs;
    public double PreviewStartMs;
    public int Difficulty = 3;
}

public sealed class LevelData
{
    public const int SupportedFormatVersion = 2;

    public int FormatVersion = 1;
    public LevelMeta Meta = new();
    public Rgba Background = new(0x0b / 255f, 0x0d / 255f, 0x12 / 255f);
    public Rgba Hazard = new(1f, 0x2a / 255f, 0x6d / 255f);
    public Rgba Flash = new(1, 1, 1);
    public double HitboxScale = 0.9;
    public List<double> Checkpoints = new() { 0 };
    public List<LevelObject> Objects = new();
    public List<LevelEvent> Events = new();
    /// <summary>The level's scripts by name (scripts/*.js), filled in by whoever loaded the folder.</summary>
    public Dictionary<string, string> ScriptSources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads scripts/*.js of a level folder into <see cref="ScriptSources"/>.</summary>
    public void LoadScripts(string levelFolder)
    {
        ScriptSources.Clear();
        var dir = System.IO.Path.Combine(levelFolder, "scripts");
        if (!System.IO.Directory.Exists(dir)) return;
        foreach (var f in System.IO.Directory.GetFiles(dir, "*.js"))
            ScriptSources[System.IO.Path.GetFileNameWithoutExtension(f)] = System.IO.File.ReadAllText(f);
    }

    /// <summary>The "format" value of a level: "sonokinesis.level" ("beatform.level" from before the rename).</summary>
    public const string FormatName = "sonokinesis.level";
    public static bool IsFormatName(string s) => s == FormatName || s == "beatform.level";

    public static LevelData Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        var level = new LevelData();

        if (!IsFormatName(Str(root, "format"))) throw new FormatException("not a Sonokinesis level (format field)");
        level.FormatVersion = (int)Num(root, "formatVersion", 1);
        if (level.FormatVersion > SupportedFormatVersion)
            throw new FormatException($"level needs format version {level.FormatVersion}; this loader supports up to {SupportedFormatVersion}");

        if (root.TryGetProperty("meta", out var meta))
        {
            var m = level.Meta;
            m.Id = Str(meta, "id") ?? "";
            m.Title = Str(meta, "title") ?? "";
            m.Artist = Str(meta, "artist") ?? "";
            m.Author = Str(meta, "author") ?? "";
            m.Description = Str(meta, "description") ?? "";
            m.Audio = Str(meta, "audio") ?? "";
            m.Bpm = Num(meta, "bpm", 120);
            m.BeatsPerBar = (int)Num(meta, "beatsPerBar", 4);
            m.OffsetMs = Num(meta, "offsetMs", 0);
            if (meta.TryGetProperty("lengthMs", out var len) && len.ValueKind == JsonValueKind.Number) m.LengthMs = len.GetDouble();
            m.PreviewStartMs = Num(meta, "previewStartMs", 0);
            m.Difficulty = Math.Clamp((int)Num(meta, "difficulty", 3), 1, 5);
        }
        if (string.IsNullOrEmpty(level.Meta.Id)) throw new FormatException("meta.id is required");
        if (string.IsNullOrEmpty(level.Meta.Audio)) throw new FormatException("meta.audio is required");

        if (root.TryGetProperty("theme", out var theme))
        {
            level.Background = Rgba.Parse(Str(theme, "background"), level.Background);
            level.Hazard = Rgba.Parse(Str(theme, "hazard"), level.Hazard);
            level.Flash = Rgba.Parse(Str(theme, "flash"), level.Flash);
        }
        if (root.TryGetProperty("settings", out var settings))
            level.HitboxScale = Num(settings, "hitboxScale", 0.9);

        if (root.TryGetProperty("checkpoints", out var cps) && cps.ValueKind == JsonValueKind.Array)
        {
            level.Checkpoints.Clear();
            foreach (var c in cps.EnumerateArray()) if (c.ValueKind == JsonValueKind.Number) level.Checkpoints.Add(c.GetDouble());
            level.Checkpoints.Sort();
            if (level.Checkpoints.Count == 0 || level.Checkpoints[0] != 0) level.Checkpoints.Insert(0, 0);
        }

        if (root.TryGetProperty("objects", out var objs) && objs.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var o in objs.EnumerateArray()) level.Objects.Add(ParseObject(o, level, i++));
        }
        LinkParents(level);

        if (root.TryGetProperty("events", out var evs) && evs.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in evs.EnumerateArray())
            {
                var type = Str(e, "type");
                var ev = new LevelEvent
                {
                    T = Num(e, "t", 0),
                    Duration = Math.Max(0, Num(e, "duration", 0)),
                    Intensity = Num(e, "intensity", 8),
                    Opacity = Math.Clamp(Num(e, "opacity", 1), 0, 1),
                    Color = Rgba.Parse(Str(e, "color"), new Rgba(1, 1, 1)),
                    Ease = Core.Ease.Parse(Str(e, "ease")),
                    SwayRotation = Num(e, "rotation", 0),
                    SwayX = Num(e, "x", 0),
                    SwayY = Num(e, "y", 0),
                    Sound = Str(e, "sound"),
                    Volume = Math.Clamp(Num(e, "volume", 1), 0, 1),
                    Speed = Math.Clamp(Num(e, "speed", 1), 0, 2),
                    Turn = Math.Clamp(Num(e, "turn", 1), 0.001, 1),
                    Hold = Math.Max(0, Num(e, "hold", 0)),
                    Scale = Math.Clamp(Num(e, "scale", 1), 0.25, 4),
                    FocusPlayer = PlayerAnchorOf(Str(e, "focus")),
                };
                ev.Period = Math.Max(100, Num(e, "period", 2000));
                ev.PeriodX = Math.Max(100, Num(e, "periodX", ev.Period * 1.08));
                ev.PeriodY = Math.Max(100, Num(e, "periodY", ev.Period * 1.21));
                switch (type)
                {
                    case "shake": ev.Type = EventType.Shake; break;
                    case "flash": ev.Type = EventType.Flash; break;
                    case "background": ev.Type = EventType.Background; break;
                    case "sway": ev.Type = EventType.Sway; break;
                    case "sound": if (string.IsNullOrEmpty(ev.Sound)) continue; ev.Type = EventType.Sound; break;
                    case "playerSpeed": ev.Type = EventType.PlayerSpeed; break;
                    case "credits": ev.Type = EventType.Credits; break;
                    case "zoom": ev.Type = EventType.Zoom; break;
                    default: continue;
                }
                level.Events.Add(ev);
            }
            level.Events.Sort((a, b) => a.T.CompareTo(b.T));
        }
        return level;
    }

    private static LevelObject ParseObject(JsonElement o, LevelData level, int index)
    {
        var obj = new LevelObject
        {
            FileIndex = index,
            Id = Str(o, "id") ?? $"#{index}",
            Name = Str(o, "name") ?? "",
            Start = Num(o, "start", 0),
            Warn = Math.Max(0, Num(o, "warn", 0)),
            Outro = Math.Max(0, Num(o, "outro", 0)),
            Hitbox = Bool(o, "hitbox", true),
            HitboxScale = Num(o, "hitboxScale", 1),
            Color = Rgba.Parse(Str(o, "color"), level.Hazard),
            ColorFromPlayer = Str(o, "color") == "@player",
            Layer = (int)Num(o, "layer", 0),
            ParentId = Str(o, "parent"),
        };
        obj.End = Num(o, "end", obj.Start + 1000);
        if (obj.End <= obj.Start) obj.End = obj.Start + 1;
        if (o.TryGetProperty("pivot", out var pv) && pv.ValueKind == JsonValueKind.Array && pv.GetArrayLength() >= 2)
        {
            obj.PivotX = pv[0].GetDouble();
            obj.PivotY = pv[1].GetDouble();
        }
        if (o.TryGetProperty("shape", out var sh)) obj.Shape = ParseShape(sh);
        if (o.TryGetProperty("imposter", out var imp) && imp.ValueKind == JsonValueKind.Object) obj.Imposter = ImposterBehavior.Parse(imp);
        if (o.TryGetProperty("script", out var scr) && scr.ValueKind == JsonValueKind.Object && Str(scr, "name") is string sname && sname.Length > 0)
        {
            obj.Script = new ScriptRef { Name = sname };
            if (scr.TryGetProperty("params", out var sp) && sp.ValueKind == JsonValueKind.Object)
                foreach (var prop in sp.EnumerateObject()) obj.Script.Params[prop.Name] = ScriptRef.ToClr(prop.Value);
        }
        obj.PlayerAnchor = PlayerAnchorOf(obj.ParentId);
        if (obj.ColorFromPlayer) obj.Color = DefaultPlayerColor;
        if (o.TryGetProperty("random", out var rnd) && rnd.ValueKind == JsonValueKind.Object)
        {
            obj.RandomX = Range(rnd, "x");
            obj.RandomY = Range(rnd, "y");
            obj.RandomRot = Range(rnd, "rot");
            obj.RandomSeed = Str(rnd, "seed");
        }
        if (o.TryGetProperty("choice", out var ch))
        {
            var list = new List<RandomChoice>();
            if (ch.ValueKind == JsonValueKind.Object) list.Add(ParseChoice(ch));
            else if (ch.ValueKind == JsonValueKind.Array)
                foreach (var c in ch.EnumerateArray()) if (c.ValueKind == JsonValueKind.Object) list.Add(ParseChoice(c));
            list.RemoveAll(c => string.IsNullOrEmpty(c.Group));
            if (list.Count > 0) obj.Choices = list.ToArray();
        }
        if (o.TryGetProperty("tracks", out var tr))
        {
            ReadTrack(tr, "x", obj.X);
            ReadTrack(tr, "y", obj.Y);
            ReadTrack(tr, "rot", obj.Rot);
            ReadTrack(tr, "w", obj.W);
            ReadTrack(tr, "h", obj.H);
            ReadTrack(tr, "opacity", obj.Opacity);
        }
        return obj;
    }

    private static double[] Range(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var r) || r.ValueKind != JsonValueKind.Array || r.GetArrayLength() < 2) return null;
        if (r[0].ValueKind != JsonValueKind.Number || r[1].ValueKind != JsonValueKind.Number) return null;
        return new[] { r[0].GetDouble(), r[1].GetDouble() };
    }

    private static RandomChoice ParseChoice(JsonElement c)
    {
        return new RandomChoice
        {
            Group = Str(c, "group") ?? "",
            Count = Math.Clamp((int)Num(c, "count", 2), 1, 1000),
            Show = Ints(c, "show"),
            Hide = Ints(c, "hide"),
        };
    }

    private static int[] Ints(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Array) return null;
        var list = new List<int>();
        foreach (var v in a.EnumerateArray()) if (v.ValueKind == JsonValueKind.Number) list.Add((int)v.GetDouble());
        return list.ToArray();
    }

    public static ShapeDef ParseShape(JsonElement sh)
    {
        var s = new ShapeDef();
        switch (Str(sh, "type"))
        {
            case "circle": s.Type = ShapeType.Circle; break;
            case "polygon": s.Type = ShapeType.Polygon; break;
            case "star": s.Type = ShapeType.Star; break;
            case "ring": s.Type = ShapeType.Ring; break;
            case "spikes": s.Type = ShapeType.Spikes; break;
            case "text": s.Type = ShapeType.Text; break;
            default: s.Type = ShapeType.Rect; break;
        }
        s.Text = Str(sh, "text") ?? "";
        s.Bold = Bool(sh, "bold", true);
        s.Sides = Math.Clamp((int)Num(sh, "sides", 3), 3, 12);
        s.Points = Math.Clamp((int)Num(sh, "points", 5), 3, 12);
        s.Spikes = Math.Clamp((int)Num(sh, "spikes", 12), 4, 24);
        s.Thickness = Math.Clamp(Num(sh, "thickness", 0.12), 0.02, 0.5);
        double defaultInner = s.Type == ShapeType.Spikes ? 0.75 : 0.5;
        s.Inner = s.Type == ShapeType.Spikes
            ? Math.Clamp(Num(sh, "inner", defaultInner), 0.4, 0.95)
            : Math.Clamp(Num(sh, "inner", defaultInner), 0.1, 0.95);
        return s;
    }

    private static void ReadTrack(JsonElement tracks, string name, Track track)
    {
        if (!tracks.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        var keys = new List<Keyframe>();
        foreach (var k in arr.EnumerateArray())
        {
            if (k.ValueKind != JsonValueKind.Array || k.GetArrayLength() < 2) continue;
            var key = new Keyframe { T = k[0].GetDouble(), V = k[1].GetDouble(), Ease = Ease.Kind.Linear };
            if (k.GetArrayLength() >= 3 && k[2].ValueKind == JsonValueKind.String) key.Ease = Core.Ease.Parse(k[2].GetString());
            keys.Add(key);
        }
        // Stable sort by time so keys with equal t keep file order (the later one wins).
        var indexed = new List<(Keyframe k, int i)>();
        for (int i = 0; i < keys.Count; i++) indexed.Add((keys[i], i));
        indexed.Sort((a, b) => a.k.T != b.k.T ? a.k.T.CompareTo(b.k.T) : a.i.CompareTo(b.i));
        track.Keys = indexed.ConvertAll(x => x.k).ToArray();
    }

    /// <summary>The colour used for "@player" when the player's own colour is unknown (the editor's stand-in).</summary>
    public static readonly Rgba DefaultPlayerColor = new(0x2e / 255f, 0xe8 / 255f, 1f);

    /// <summary>"@player" is player 1; "@player2" to "@player4" are the others.</summary>
    public static int PlayerAnchorOf(string parent)
    {
        if (parent == null || !parent.StartsWith("@player")) return 0;
        if (parent.Length == 7) return 1;
        return int.TryParse(parent.Substring(7), out int n) && n >= 1 && n <= 4 ? n : 0;
    }

    private static void LinkParents(LevelData level)
    {
        var byId = new Dictionary<string, LevelObject>();
        foreach (var o in level.Objects) byId[o.Id] = o;
        foreach (var o in level.Objects)
            if (o.PlayerAnchor == 0 && !string.IsNullOrEmpty(o.ParentId) && byId.TryGetValue(o.ParentId, out var p) && p != o) o.Parent = p;
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement e, string name, double fallback) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    private static bool Bool(JsonElement e, string name, bool fallback) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : fallback;
}
