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

    public ShapeSpec Clone() => (ShapeSpec)MemberwiseClone();

    public static readonly string[] Types = { "rect", "circle", "polygon", "star", "ring", "spikes" };
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
    public Dictionary<string, List<EditKey>> Tracks = new();
    /// <summary>Editor-only: objects made by one pattern share a group id.</summary>
    public string Group;

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
        o.Tracks = new Dictionary<string, List<EditKey>>();
        foreach (var kv in Tracks) o.Tracks[kv.Key] = kv.Value.Select(k => k.Clone()).ToList();
        return o;
    }

    public double Duration => End - Start;
}

public sealed class EditEvent
{
    public static readonly string[] Types = { "shake", "flash", "background" };

    public double T;
    public string Type = "flash";
    public double Duration = 300;
    public double Intensity = 10;
    public double Opacity = 0.7;
    public string Color = "#ffffff";
    public string Ease = "linear";

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
            w.WriteNumber("formatVersion", 1);

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
            case "flash": w.WriteString("color", e.Color); Num(w, "opacity", e.Opacity); break;
            case "background": w.WriteString("color", e.Color); if (e.Ease != "linear") w.WriteString("ease", e.Ease); break;
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
                if (o["shape"] is JsonObject sh)
                {
                    eo.Shape.Type = (string)sh["type"] ?? "rect";
                    eo.Shape.Sides = (int)D(sh["sides"], 3);
                    eo.Shape.Points = (int)D(sh["points"], 5);
                    eo.Shape.Spikes = (int)D(sh["spikes"], 12);
                    eo.Shape.Inner = D(sh["inner"], eo.Shape.Type == "spikes" ? 0.75 : 0.5);
                    eo.Shape.Thickness = D(sh["thickness"], 0.12);
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
