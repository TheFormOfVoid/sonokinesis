using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beatform.Editor.Model;

public sealed class EditKey
{
    public double T;
    public double V;
    public string Ease = "linear";

    public EditKey Clone() => new() { T = T, V = V, Ease = Ease };
}

public sealed class ShapeSpec
{
    public string Type = "rect";
    public int Sides = 3;
    public int Points = 5;
    public int Spikes = 12;
    public double Inner = 0.5;
    public double Thickness = 0.12;
    public string Text = "TEXT";
    /// <summary>Montserrat Bold (true) or Medium.</summary>
    public bool Bold = true;

    public ShapeSpec Clone() => (ShapeSpec)MemberwiseClone();

    public static readonly string[] Types = { "rect", "circle", "polygon", "star", "ring", "spikes", "text" };
}

/// <summary>Player-like movement of an imposter (see the level format). Defaults match the loader's.</summary>
public sealed class ImposterSpec
{
    public string Mode = "chase";
    public double Angle = 180;
    public double Speed = 383;
    public double DashSpeed = 1467;
    public double DashDrag = 0.9;
    public double DashTime = 450;
    public double DashEvery = 550;
    /// <summary>Above 0: dash on the beat, every this many beats.</summary>
    public double DashBeats;
    public double FirstDash = -1;
    public double StopAt = -1;
    public double StopFor = 0;
    public double LeaveAt = -1;
    /// <summary>When a stop ends: dash, or just cruise on.</summary>
    public bool StopDash = true;
    /// <summary>Closes in on the nearest player from ArriveFrom, reaching them at ArriveAt (-1: never).</summary>
    public double ArriveFrom = -1, ArriveAt = -1;
    public string ArriveEase = "inQuad";
    public bool Bounds = true;
    public double StretchSpeed = 2000;
    public double StretchMax = 0.4;
    public bool Effects = true;
    /// <summary>"ring" or "disk".</summary>
    public string DashPulse = "ring";

    public ImposterSpec Clone() => (ImposterSpec)MemberwiseClone();
}

public sealed class EditObject
{
    public static readonly string[] TrackNames = { "x", "y", "rot", "w", "h", "opacity" };

    public string Id = "";
    public string Name = "";
    public ShapeSpec Shape = new();
    public double Start, Warn, End = 1000, Outro;
    public bool Hitbox = true;
    public double HitboxScale = 1;
    /// <summary>Null means the theme's hazard color.</summary>
    public string Color;
    public int Layer;
    public string Parent;
    public double PivotX, PivotY;
    /// <summary>Null for ordinary objects.</summary>
    public ImposterSpec Imposter;
    public Dictionary<string, List<EditKey>> Tracks = new();
    /// <summary>Editor-only: objects made by one pattern share a group id.</summary>
    public string Group;
    /// <summary>Random offsets per play, [min, max]; null for none.</summary>
    public double[] RandomX, RandomY, RandomRot;
    /// <summary>Objects with the same random seed name share their offsets (empty: the id).</summary>
    public string RandomSeed;
    /// <summary>All must hold for the object to exist in a play.</summary>
    public List<ChoiceSpec> Choices = new();

    public List<EditKey> Track(string name)
    {
        if (!Tracks.TryGetValue(name, out var list)) Tracks[name] = list = new List<EditKey>();
        return list;
    }

    public static double DefaultValue(string track) => track switch { "w" or "h" => 100, "opacity" => 1, _ => 0 };

    public EditObject Clone(string newId)
    {
        var o = (EditObject)MemberwiseClone();
        o.Id = newId;
        o.Shape = Shape.Clone();
        o.Imposter = Imposter?.Clone();
        o.RandomX = (double[])RandomX?.Clone();
        o.RandomY = (double[])RandomY?.Clone();
        o.RandomRot = (double[])RandomRot?.Clone();
        o.Choices = Choices.Select(c => c.Clone()).ToList();
        o.Tracks = new Dictionary<string, List<EditKey>>();
        foreach (var kv in Tracks) o.Tracks[kv.Key] = kv.Value.Select(k => k.Clone()).ToList();
        return o;
    }

    public double Duration => End - Start;
}

/// <summary>A choice condition: the group rolls 0..Count-1 once per play (see the level format).</summary>
public sealed class ChoiceSpec
{
    public string Group = "group";
    public int Count = 2;
    public List<int> Show = new() { 0 };
    public List<int> Hide = new();

    public ChoiceSpec Clone() => new() { Group = Group, Count = Count, Show = new List<int>(Show), Hide = new List<int>(Hide) };
}

public sealed class EditEvent
{
    public static readonly string[] Types = { "shake", "flash", "background", "sway", "sound", "playerSpeed", "credits", "zoom" };
    /// <summary>Game sounds a "sound" event can play (see the level format).</summary>
    public static readonly string[] Sounds = { "playerDeath", "playerHit", "playerRespawn", "explosion" };

    public double T;
    public string Type = "flash";
    public double Duration = 300;
    public double Intensity = 10;
    public double Opacity = 0.7;
    public string Color = "#ffffff";
    public string Ease = "linear";
    public double SwayRotation = 1, SwayX = 5, SwayY = 0, Period = 2150, PeriodX = 2330, PeriodY = 2600;
    public string Sound = "playerDeath";
    public double Volume = 1;
    /// <summary>playerSpeed: a part of the players' normal speed (0..2).</summary>
    public double Speed = 0.05;
    /// <summary>playerSpeed: the part of the way to their heading the players turn each 1/60 s.</summary>
    public double Turn = 1;
    /// <summary>flash: time at full opacity before fading.</summary>
    public double Hold;
    /// <summary>zoom: scale, and the point it zooms on (x, y), or "@player".</summary>
    public double Scale = 1.3;
    public string Focus;

    public EditEvent Clone() => (EditEvent)MemberwiseClone();
}

/// <summary>
/// The level being edited. It mirrors spec/level-format.md, keeps editor-only data in the "editor"
/// block, and writes files the loader reads with the shared parser.
/// </summary>
public sealed class LevelDocument
{
    public string FolderPath;
    public string Id = Guid.NewGuid().ToString("N")[..12];
    public string Title = "Untitled";
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

    public string Background = "#0b0d12";
    public string Hazard = "#ff2a6d";
    public string Flash = "#ffffff";
    public double HitboxScale = 0.9;

    public List<double> Checkpoints = new() { 0 };
    public List<EditObject> Objects = new();
    public List<EditEvent> Events = new();
    /// <summary>Editor block content we do not interpret, kept as is.</summary>
    public JsonObject EditorExtra = new();

    private int nextId = 1;

    public string NewId()
    {
        string id;
        do id = "o" + nextId++; while (Objects.Any(o => o.Id == id));
        return id;
    }

    public EditObject Find(string id) => id == null ? null : Objects.FirstOrDefault(o => o.Id == id);

    public double BeatMs => 60000.0 / Math.Max(1, Bpm);

    /// <summary>Song time of the last thing in the level.</summary>
    public double ContentEnd => Math.Max(Objects.Count == 0 ? 0 : Objects.Max(o => o.End), Events.Count == 0 ? 0 : Events.Max(e => e.T + e.Duration));

    public string AudioPath => FolderPath == null || string.IsNullOrEmpty(Audio) ? null : Path.Combine(FolderPath, Audio);

    // ---------------- writing ----------------

    /// <param name="forPreview">Fills required fields that a new level may not have yet (no song chosen).</param>
    public string ToJson(bool indented = true, bool forPreview = false)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = indented, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("format", "beatform.level");
            w.WriteNumber("formatVersion", 2);

            w.WriteStartObject("meta");
            w.WriteString("id", forPreview && string.IsNullOrEmpty(Id) ? "preview" : Id);
            w.WriteString("title", Title);
            w.WriteString("artist", Artist);
            w.WriteString("author", Author);
            if (!string.IsNullOrEmpty(Description)) w.WriteString("description", Description);
            w.WriteString("audio", forPreview && string.IsNullOrEmpty(Audio) ? "none.ogg" : Audio);
            Num(w, "bpm", Bpm);
            w.WriteNumber("beatsPerBar", BeatsPerBar);
            Num(w, "offsetMs", OffsetMs);
            if (LengthMs is double len) Num(w, "lengthMs", len);
            Num(w, "previewStartMs", PreviewStartMs);
            w.WriteNumber("difficulty", Difficulty);
            w.WriteEndObject();

            w.WriteStartObject("theme");
            w.WriteString("background", Background);
            w.WriteString("hazard", Hazard);
            w.WriteString("flash", Flash);
            w.WriteEndObject();

            w.WriteStartObject("settings");
            Num(w, "hitboxScale", HitboxScale);
            w.WriteEndObject();

            w.WriteStartArray("checkpoints");
            foreach (var c in Checkpoints.Distinct().OrderBy(c => c)) Num(w, null, c);
            w.WriteEndArray();

            w.WriteStartArray("objects");
            foreach (var o in Objects) WriteObject(w, o);
            w.WriteEndArray();

            w.WriteStartArray("events");
            foreach (var e in Events.OrderBy(e => e.T)) WriteEvent(w, e);
            w.WriteEndArray();

            w.WritePropertyName("editor");
            var extra = (JsonObject)EditorExtra.DeepClone();
            extra["app"] = "Beatform Editor";
            var groups = new JsonObject();
            foreach (var o in Objects.Where(o => o.Group != null)) groups[o.Id] = o.Group;
            extra["groups"] = groups;
            extra.WriteTo(w);

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteObject(Utf8JsonWriter w, EditObject o)
    {
        w.WriteStartObject();
        w.WriteString("id", o.Id);
        if (!string.IsNullOrEmpty(o.Name)) w.WriteString("name", o.Name);
        w.WriteStartObject("shape");
        w.WriteString("type", o.Shape.Type);
        switch (o.Shape.Type)
        {
            case "polygon": w.WriteNumber("sides", o.Shape.Sides); break;
            case "star": w.WriteNumber("points", o.Shape.Points); Num(w, "inner", o.Shape.Inner); break;
            case "spikes": w.WriteNumber("spikes", o.Shape.Spikes); Num(w, "inner", o.Shape.Inner); break;
            case "ring": Num(w, "thickness", o.Shape.Thickness); break;
            case "text": w.WriteString("text", o.Shape.Text ?? ""); if (!o.Shape.Bold) w.WriteBoolean("bold", false); break;
        }
        w.WriteEndObject();
        Num(w, "start", o.Start);
        if (o.Warn > 0) Num(w, "warn", o.Warn);
        Num(w, "end", o.End);
        if (o.Outro > 0) Num(w, "outro", o.Outro);
        if (!o.Hitbox) w.WriteBoolean("hitbox", false);
        if (Math.Abs(o.HitboxScale - 1) > 1e-9) Num(w, "hitboxScale", o.HitboxScale);
        if (!string.IsNullOrEmpty(o.Color)) w.WriteString("color", o.Color);
        if (o.Layer != 0) w.WriteNumber("layer", o.Layer);
        if (!string.IsNullOrEmpty(o.Parent)) w.WriteString("parent", o.Parent);
        if (o.RandomX != null || o.RandomY != null || o.RandomRot != null)
        {
            w.WriteStartObject("random");
            foreach (var (name, r) in new[] { ("x", o.RandomX), ("y", o.RandomY), ("rot", o.RandomRot) })
            {
                if (r == null) continue;
                w.WriteStartArray(name);
                Num(w, null, r[0]);
                Num(w, null, r[1]);
                w.WriteEndArray();
            }
            if (!string.IsNullOrEmpty(o.RandomSeed)) w.WriteString("seed", o.RandomSeed);
            w.WriteEndObject();
        }
        if (o.Choices.Count > 0)
        {
            w.WriteStartArray("choice");
            foreach (var c in o.Choices)
            {
                w.WriteStartObject();
                w.WriteString("group", c.Group ?? "");
                w.WriteNumber("count", c.Count);
                if (c.Show.Count > 0) { w.WriteStartArray("show"); foreach (var v in c.Show) w.WriteNumberValue(v); w.WriteEndArray(); }
                if (c.Hide.Count > 0) { w.WriteStartArray("hide"); foreach (var v in c.Hide) w.WriteNumberValue(v); w.WriteEndArray(); }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        if (o.Imposter != null)
        {
            var m = o.Imposter;
            w.WriteStartObject("imposter");
            w.WriteString("mode", m.Mode);
            Num(w, "angle", m.Angle);
            Num(w, "speed", m.Speed);
            Num(w, "dashSpeed", m.DashSpeed);
            Num(w, "dashDrag", m.DashDrag);
            Num(w, "dashTime", m.DashTime);
            Num(w, "dashEvery", m.DashEvery);
            if (m.DashBeats > 0) Num(w, "dashBeats", m.DashBeats);
            if (m.FirstDash >= 0) Num(w, "firstDash", m.FirstDash);
            if (m.StopAt >= 0) { Num(w, "stopAt", m.StopAt); Num(w, "stopFor", m.StopFor); if (!m.StopDash) w.WriteBoolean("stopDash", false); }
            if (m.LeaveAt >= 0) Num(w, "leaveAt", m.LeaveAt);
            if (m.ArriveFrom >= 0 && m.ArriveAt > m.ArriveFrom)
            {
                Num(w, "arriveFrom", m.ArriveFrom);
                Num(w, "arriveAt", m.ArriveAt);
                if (m.ArriveEase != "inQuad") w.WriteString("arriveEase", m.ArriveEase);
            }
            w.WriteBoolean("bounds", m.Bounds);
            Num(w, "stretchSpeed", m.StretchSpeed);
            Num(w, "stretchMax", m.StretchMax);
            if (!m.Effects) w.WriteBoolean("effects", false);
            if (m.DashPulse == "disk") w.WriteString("dashPulse", "disk");
            w.WriteEndObject();
        }
        if (o.PivotX != 0 || o.PivotY != 0)
        {
            w.WriteStartArray("pivot");
            Num(w, null, o.PivotX);
            Num(w, null, o.PivotY);
            w.WriteEndArray();
        }
        w.WriteStartObject("tracks");
        foreach (var name in EditObject.TrackNames)
        {
            if (!o.Tracks.TryGetValue(name, out var keys) || keys.Count == 0) continue;
            w.WriteStartArray(name);
            foreach (var k in keys.OrderBy(k => k.T))
            {
                w.WriteStartArray();
                Num(w, null, k.T);
                Num(w, null, k.V);
                if (!string.IsNullOrEmpty(k.Ease) && k.Ease != "linear") w.WriteStringValue(k.Ease);
                w.WriteEndArray();
            }
            w.WriteEndArray();
        }
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteEvent(Utf8JsonWriter w, EditEvent e)
    {
        w.WriteStartObject();
        Num(w, "t", e.T);
        w.WriteString("type", e.Type);
        Num(w, "duration", e.Duration);
        switch (e.Type)
        {
            case "shake": Num(w, "intensity", e.Intensity); break;
            case "flash": w.WriteString("color", e.Color); Num(w, "opacity", e.Opacity); if (e.Hold > 0) Num(w, "hold", e.Hold); break;
            case "zoom":
                Num(w, "scale", e.Scale);
                if (!string.IsNullOrEmpty(e.Focus)) w.WriteString("focus", e.Focus);
                else { Num(w, "x", e.SwayX); Num(w, "y", e.SwayY); }
                if (e.Ease != "linear") w.WriteString("ease", e.Ease);
                break;
            case "background": w.WriteString("color", e.Color); if (e.Ease != "linear") w.WriteString("ease", e.Ease); break;
            case "sway":
                Num(w, "rotation", e.SwayRotation); Num(w, "x", e.SwayX); Num(w, "y", e.SwayY);
                Num(w, "period", e.Period); Num(w, "periodX", e.PeriodX); Num(w, "periodY", e.PeriodY);
                if (e.Ease != "linear") w.WriteString("ease", e.Ease);
                break;
            case "sound": w.WriteString("sound", e.Sound ?? ""); Num(w, "volume", e.Volume); break;
            case "playerSpeed":
                Num(w, "speed", e.Speed);
                if (e.Turn < 1) Num(w, "turn", e.Turn);
                if (e.Ease != "linear") w.WriteString("ease", e.Ease);
                break;
        }
        w.WriteEndObject();
    }

    /// <summary>Numbers are rounded to 3 decimals: plenty for ms and game units, and keeps files small.</summary>
    private static void Num(Utf8JsonWriter w, string name, double v)
    {
        v = Math.Round(v, 3);
        if (v == Math.Floor(v) && Math.Abs(v) < 1e15)
        {
            if (name == null) w.WriteNumberValue((long)v); else w.WriteNumber(name, (long)v);
        }
        else
        {
            if (name == null) w.WriteNumberValue(v); else w.WriteNumber(name, v);
        }
    }

    // ---------------- reading ----------------

    public static LevelDocument FromJson(string json, string folder)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })!.AsObject();
        if ((string)root["format"] != "beatform.level") throw new FormatException("This file is not a Beatform level.");
        int version = (int?)root["formatVersion"] ?? 1;
        if (version > Core.LevelData.SupportedFormatVersion) throw new FormatException($"This level needs a newer editor (format {version}).");

        var d = new LevelDocument { FolderPath = folder };
        var meta = root["meta"]?.AsObject();
        if (meta != null)
        {
            d.Id = (string)meta["id"] ?? d.Id;
            d.Title = (string)meta["title"] ?? d.Title;
            d.Artist = (string)meta["artist"] ?? "";
            d.Author = (string)meta["author"] ?? "";
            d.Description = (string)meta["description"] ?? "";
            d.Audio = (string)meta["audio"] ?? "";
            d.Bpm = D(meta["bpm"], 120);
            d.BeatsPerBar = (int)D(meta["beatsPerBar"], 4);
            d.OffsetMs = D(meta["offsetMs"], 0);
            d.LengthMs = meta["lengthMs"] is JsonValue lv ? lv.GetValue<double>() : null;
            d.PreviewStartMs = D(meta["previewStartMs"], 0);
            d.Difficulty = (int)D(meta["difficulty"], 3);
        }
        var theme = root["theme"]?.AsObject();
        if (theme != null)
        {
            d.Background = (string)theme["background"] ?? d.Background;
            d.Hazard = (string)theme["hazard"] ?? d.Hazard;
            d.Flash = (string)theme["flash"] ?? d.Flash;
        }
        d.HitboxScale = D(root["settings"]?["hitboxScale"], 0.9);
        if (root["checkpoints"] is JsonArray cps)
        {
            d.Checkpoints = cps.Select(c => D(c, 0)).Distinct().OrderBy(c => c).ToList();
            if (d.Checkpoints.Count == 0 || d.Checkpoints[0] != 0) d.Checkpoints.Insert(0, 0);
        }
        var groups = root["editor"]?["groups"] as JsonObject;
        if (root["objects"] is JsonArray objs)
        {
            foreach (var on in objs)
            {
                if (on is not JsonObject o) continue;
                var eo = new EditObject
                {
                    Id = (string)o["id"] ?? d.NewId(),
                    Name = (string)o["name"] ?? "",
                    Start = D(o["start"], 0),
                    Warn = D(o["warn"], 0),
                    End = D(o["end"], 1000),
                    Outro = D(o["outro"], 0),
                    Hitbox = o["hitbox"] is JsonValue hb ? hb.GetValue<bool>() : true,
                    HitboxScale = D(o["hitboxScale"], 1),
                    Color = (string)o["color"],
                    Layer = (int)D(o["layer"], 0),
                    Parent = (string)o["parent"],
                };
                if (o["pivot"] is JsonArray pv && pv.Count >= 2) { eo.PivotX = D(pv[0], 0); eo.PivotY = D(pv[1], 0); }
                if (o["random"] is JsonObject rnd)
                {
                    static double[] Range(JsonNode n) => n is JsonArray a && a.Count >= 2 ? new[] { D(a[0], 0), D(a[1], 0) } : null;
                    eo.RandomX = Range(rnd["x"]);
                    eo.RandomY = Range(rnd["y"]);
                    eo.RandomRot = Range(rnd["rot"]);
                    eo.RandomSeed = (string)rnd["seed"];
                }
                if (o["choice"] is JsonNode chn)
                {
                    var list = chn is JsonArray arr ? arr.OfType<JsonObject>() : chn is JsonObject one ? new[] { one } : Enumerable.Empty<JsonObject>();
                    foreach (var c in list)
                    {
                        static List<int> Ints(JsonNode n) => n is JsonArray a ? a.Select(v => (int)D(v, 0)).ToList() : new List<int>();
                        eo.Choices.Add(new ChoiceSpec { Group = (string)c["group"] ?? "", Count = (int)D(c["count"], 2), Show = Ints(c["show"]), Hide = Ints(c["hide"]) });
                    }
                }
                if (o["imposter"] is JsonObject im)
                {
                    var m = new ImposterSpec { Mode = (string)im["mode"] == "straight" ? "straight" : "chase" };
                    m.Angle = D(im["angle"], m.Angle);
                    m.Speed = D(im["speed"], m.Speed);
                    m.DashSpeed = D(im["dashSpeed"], m.DashSpeed);
                    m.DashDrag = D(im["dashDrag"], m.DashDrag);
                    m.DashTime = D(im["dashTime"], m.DashTime);
                    m.DashEvery = D(im["dashEvery"], m.DashEvery);
                    m.DashBeats = Math.Max(0, D(im["dashBeats"], 0));
                    m.FirstDash = D(im["firstDash"], m.FirstDash);
                    m.StopAt = D(im["stopAt"], m.StopAt);
                    m.StopFor = D(im["stopFor"], m.StopFor);
                    m.LeaveAt = D(im["leaveAt"], m.LeaveAt);
                    m.StopDash = im["stopDash"] is JsonValue sdv ? sdv.GetValue<bool>() : true;
                    m.ArriveFrom = D(im["arriveFrom"], -1);
                    m.ArriveAt = D(im["arriveAt"], -1);
                    m.ArriveEase = (string)im["arriveEase"] ?? "inQuad";
                    m.Bounds = im["bounds"] is JsonValue bv ? bv.GetValue<bool>() : m.Mode == "chase";
                    m.StretchSpeed = D(im["stretchSpeed"], m.StretchSpeed);
                    m.StretchMax = D(im["stretchMax"], m.StretchMax);
                    m.Effects = im["effects"] is JsonValue ev ? ev.GetValue<bool>() : true;
                    m.DashPulse = (string)im["dashPulse"] == "disk" ? "disk" : "ring";
                    eo.Imposter = m;
                }
                if (o["shape"] is JsonObject sh)
                {
                    eo.Shape.Type = (string)sh["type"] ?? "rect";
                    eo.Shape.Sides = (int)D(sh["sides"], 3);
                    eo.Shape.Points = (int)D(sh["points"], 5);
                    eo.Shape.Spikes = (int)D(sh["spikes"], 12);
                    eo.Shape.Inner = D(sh["inner"], eo.Shape.Type == "spikes" ? 0.75 : 0.5);
                    eo.Shape.Thickness = D(sh["thickness"], 0.12);
                    eo.Shape.Text = (string)sh["text"] ?? "";
                    eo.Shape.Bold = sh["bold"] is JsonValue bv ? bv.GetValue<bool>() : true;
                }
                if (o["tracks"] is JsonObject tr)
                {
                    foreach (var name in EditObject.TrackNames)
                    {
                        if (tr[name] is not JsonArray keys) continue;
                        var list = eo.Track(name);
                        foreach (var k in keys)
                        {
                            if (k is not JsonArray ka || ka.Count < 2) continue;
                            list.Add(new EditKey { T = D(ka[0], 0), V = D(ka[1], 0), Ease = ka.Count > 2 ? (string)ka[2] ?? "linear" : "linear" });
                        }
                        list.Sort((a, b) => a.T.CompareTo(b.T));
                    }
                }
                if (groups != null && groups[eo.Id] is JsonValue g) eo.Group = g.GetValue<string>();
                d.Objects.Add(eo);
            }
        }
        if (root["events"] is JsonArray evs)
        {
            foreach (var en in evs)
            {
                if (en is not JsonObject e) continue;
                d.Events.Add(new EditEvent
                {
                    T = D(e["t"], 0),
                    Type = (string)e["type"] ?? "flash",
                    Duration = D(e["duration"], 300),
                    Intensity = D(e["intensity"], 10),
                    Opacity = D(e["opacity"], 0.7),
                    Color = (string)e["color"] ?? "#ffffff",
                    Ease = (string)e["ease"] ?? "linear",
                    SwayRotation = D(e["rotation"], 0),
                    SwayX = D(e["x"], 0),
                    SwayY = D(e["y"], 0),
                    Period = D(e["period"], 2000),
                    PeriodX = D(e["periodX"], D(e["period"], 2000) * 1.08),
                    PeriodY = D(e["periodY"], D(e["period"], 2000) * 1.21),
                    Sound = (string)e["sound"] ?? "playerDeath",
                    Volume = D(e["volume"], 1),
                    Speed = D(e["speed"], 1),
                    Turn = D(e["turn"], 1),
                    Hold = D(e["hold"], 0),
                    Scale = D(e["scale"], 1),
                    Focus = (string)e["focus"],
                });
            }
        }
        if (root["editor"] is JsonObject ed)
        {
            d.EditorExtra = (JsonObject)ed.DeepClone();
            d.EditorExtra.Remove("groups");
        }
        // Continue numbering after existing ids like "o17".
        foreach (var o in d.Objects)
            if (o.Id.Length > 1 && o.Id[0] == 'o' && int.TryParse(o.Id[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                d.nextId = Math.Max(d.nextId, n + 1);
        return d;
    }

    private static double D(JsonNode n, double fallback)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<long>(out var l)) return l;
        }
        return fallback;
    }

    public static LevelDocument Load(string folder)
    {
        var json = File.ReadAllText(Path.Combine(folder, "level.json"));
        return FromJson(json, folder);
    }

    public void Save()
    {
        if (FolderPath == null) throw new InvalidOperationException("The level has no folder yet.");
        Directory.CreateDirectory(FolderPath);
        var path = Path.Combine(FolderPath, "level.json");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson());
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
        else File.Move(tmp, path);
    }
}
