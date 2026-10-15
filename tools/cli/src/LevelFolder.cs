using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Beatform.Core;

namespace Beatform.Cli;

/// <summary>A mistake in how the tool was used: reported to the user without a stack trace.</summary>
public sealed class CliError : Exception
{
    public CliError(string message) : base(message) { }
}

/// <summary>
/// A level folder on disk: level.json (edited as JSON so unknown fields survive), its song and its
/// scripts/ folder.
/// </summary>
public sealed class LevelFolder
{
    public readonly string Folder;
    public JsonObject Root;

    public static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };

    public LevelFolder(string folder)
    {
        Folder = Path.GetFullPath(folder ?? throw new CliError("a level folder is required"));
        if (File.Exists(Folder) && Path.GetFileName(Folder).Equals("level.json", StringComparison.OrdinalIgnoreCase)) Folder = Path.GetDirectoryName(Folder);
        var file = LevelJson;
        if (!File.Exists(file)) throw new CliError($"no level.json in {Folder}");
        try
        {
            Root = JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                ?? throw new CliError("level.json is not a JSON object");
        }
        catch (JsonException e) { throw new CliError($"level.json is not valid JSON: {e.Message}"); }
    }

    public string LevelJson => Path.Combine(Folder, "level.json");
    public string ScriptsDir => Path.Combine(Folder, "scripts");
    public JsonObject Meta => Root["meta"] as JsonObject ?? (JsonObject)(Root["meta"] = new JsonObject());
    public JsonArray Objects => Root["objects"] as JsonArray ?? (JsonArray)(Root["objects"] = new JsonArray());
    public JsonArray Events => Root["events"] as JsonArray ?? (JsonArray)(Root["events"] = new JsonArray());
    public double Bpm => Num(Meta["bpm"], 120) is var b && b > 0 ? b : 120;
    public double BeatMs => 60000 / Bpm;

    public void Save()
    {
        var file = LevelJson;
        if (File.Exists(file)) File.Copy(file, file + ".bak", true);
        File.WriteAllText(file, Root.ToJsonString(Pretty));
    }

    /// <summary>The level as the game reads it, with its scripts.</summary>
    public LevelData Load()
    {
        var data = LevelData.Parse(Root.ToJsonString());
        data.LoadScripts(Folder);
        return data;
    }

    /// <summary>Song length in ms (decoding the audio), or null if it cannot be read.</summary>
    public double? SongLengthMs()
    {
        var audio = (string)Meta["audio"];
        if (string.IsNullOrEmpty(audio)) return null;
        var path = Path.Combine(Folder, audio);
        if (!File.Exists(path)) return null;
        try { return Beatform.Audio.AudioDecoder.Decode(path).LengthMs - Num(Meta["offsetMs"], 0); }
        catch { return null; }
    }

    /// <summary>Where the level ends: meta.lengthMs, else the song, else the last object.</summary>
    public double LengthMs()
    {
        if (Meta["lengthMs"] != null) return Num(Meta["lengthMs"], 0);
        var song = SongLengthMs();
        if (song is double s) return s;
        double end = 0;
        foreach (var o in Objects.OfType<JsonObject>()) end = Math.Max(end, Num(o["end"], 0));
        return end;
    }

    public static double Num(JsonNode n, double fallback)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
        }
        return fallback;
    }

    // ---------------- ids ----------------

    private HashSet<string> Ids() => new(Objects.OfType<JsonObject>().Select(o => (string)o["id"]).Where(i => i != null));

    public Func<string> IdMaker()
    {
        var ids = Ids();
        int n = 0;
        foreach (var id in ids)
        {
            var m = Regex.Match(id, @"^o(\d+)$");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var k)) n = Math.Max(n, k);
        }
        return () =>
        {
            string id;
            do id = "o" + (++n); while (ids.Contains(id));
            ids.Add(id);
            return id;
        };
    }

    public JsonObject Groups
    {
        get
        {
            var editor = Root["editor"] as JsonObject ?? (JsonObject)(Root["editor"] = new JsonObject());
            return editor["groups"] as JsonObject ?? (JsonObject)(editor["groups"] = new JsonObject());
        }
    }

    // ---------------- new level ----------------

    public static string Create(string folder, string song, string title, string artist, double? bpm, double? offsetMs, out JsonObject analysis)
    {
        analysis = null;
        folder = Path.GetFullPath(folder ?? throw new CliError("a folder is required"));
        if (File.Exists(Path.Combine(folder, "level.json"))) throw new CliError($"{folder} already has a level.json");
        if (string.IsNullOrEmpty(song) || !File.Exists(song)) throw new CliError($"song file not found: {song}");
        var ext = Path.GetExtension(song).ToLowerInvariant();
        if (ext is not (".ogg" or ".wav" or ".mp3")) throw new CliError("the song must be .ogg, .wav or .mp3");
        if (bpm == null || offsetMs == null)
        {
            var a = SongAnalysis.Analyze(song, bpm);
            analysis = a;
            bpm ??= (double)a["bpm"];
            offsetMs ??= (double)a["offsetMs"];
        }
        Directory.CreateDirectory(folder);
        File.Copy(song, Path.Combine(folder, "song" + ext), true);
        var root = new JsonObject
        {
            ["format"] = "beatform.level",
            ["formatVersion"] = LevelData.SupportedFormatVersion,
            ["meta"] = new JsonObject
            {
                ["id"] = "lvl-" + Guid.NewGuid().ToString("N")[..12],
                ["title"] = string.IsNullOrWhiteSpace(title) ? Path.GetFileName(folder) : title,
                ["artist"] = artist ?? "",
                ["author"] = "",
                ["description"] = "",
                ["audio"] = "song" + ext,
                ["bpm"] = Math.Round(bpm ?? 120, 3),
                ["beatsPerBar"] = 4,
                ["offsetMs"] = Math.Round(offsetMs ?? 0, 1),
                ["difficulty"] = 3,
            },
            ["theme"] = new JsonObject { ["background"] = "#0b0d12", ["hazard"] = "#ff2a6d", ["flash"] = "#ffffff" },
            ["settings"] = new JsonObject { ["hitboxScale"] = 0.9 },
            ["checkpoints"] = new JsonArray(0),
            ["objects"] = new JsonArray(),
            ["events"] = new JsonArray(),
            ["editor"] = new JsonObject(),
        };
        File.WriteAllText(Path.Combine(folder, "level.json"), root.ToJsonString(Pretty));
        Directory.CreateDirectory(Path.Combine(folder, "scripts"));
        return folder;
    }

    // ---------------- scripts ----------------

    public string ScriptPath(string name) => Path.Combine(ScriptsDir, name + ".js");

    public Dictionary<string, string> LevelScriptSources()
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(ScriptsDir))
            foreach (var f in Directory.GetFiles(ScriptsDir, "*.js")) res[Path.GetFileNameWithoutExtension(f)] = File.ReadAllText(f);
        return res;
    }

    public static ScriptLibrary LibraryScripts(out Dictionary<string, (string Path, string Category)> files)
    {
        files = Paths.LibraryScripts();
        var src = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in files) src[kv.Key] = File.ReadAllText(kv.Value.Path);
        return new ScriptLibrary(src);
    }

    /// <summary>A script by name: the level's copy, else the library's (inLevel tells which).</summary>
    public ScriptType FindScript(string name, out bool inLevel)
    {
        var level = new ScriptLibrary(LevelScriptSources());
        var t = level.Get(name);
        inLevel = t != null;
        if (t != null) return t;
        return LibraryScripts(out _).Get(name);
    }

    private static readonly Regex SpawnRef = new(@"spawn\(\s*['""]([A-Za-z0-9_\-]+)['""]");

    /// <summary>Copies a library script (and the scripts it spawns or lists in meta.uses) into the level.</summary>
    public List<string> EnsureScript(string name)
    {
        var copied = new List<string>();
        var have = LevelScriptSources();
        if (have.ContainsKey(name)) return copied;
        var lib = LibraryScripts(out var files);
        var todo = new Stack<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        todo.Push(name);
        Directory.CreateDirectory(ScriptsDir);
        while (todo.Count > 0)
        {
            var n = todo.Pop();
            if (!seen.Add(n) || have.ContainsKey(n)) continue;
            if (!files.TryGetValue(n, out var f))
            {
                if (n.Equals(name, StringComparison.OrdinalIgnoreCase)) throw new CliError($"no script named '{name}' in the level or the library");
                continue;
            }
            var text = File.ReadAllText(f.Path);
            File.WriteAllText(ScriptPath(n), text);
            copied.Add(n);
            foreach (Match m in SpawnRef.Matches(text)) todo.Push(m.Groups[1].Value);
            var t = lib.Get(n);
            if (t != null) foreach (var u in t.Uses) todo.Push(u);
        }
        return copied;
    }

    public static object ToClr(JsonNode n) => n switch
    {
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        null => null,
        _ => n.ToJsonString(),
    };

    /// <summary>Adds an object that runs a scripted attack or effect. Returns its id.</summary>
    public JsonObject PlaceScript(string name, double at, double? end, double? beats, double x, double y, double rot, JsonObject prms, string objName)
    {
        var copied = EnsureScript(name);
        var type = new ScriptLibrary(LevelScriptSources()).Get(name) ?? throw new CliError($"no script named '{name}'");
        if (type.CompileError != null) throw new CliError("the script has an error: " + type.CompileError);
        if (type.Kind == ScriptKind.Pattern) throw new CliError($"'{name}' is a pattern: add it with add_pattern / `beatform pattern`");
        double len = beats ?? (type.LengthBeats > 0 ? type.LengthBeats : 8);
        double stop = end ?? at + len * BeatMs;
        if (stop <= at) throw new CliError("end must be after the start");
        var id = IdMaker()();
        var script = new JsonObject { ["name"] = name };
        if (prms != null && prms.Count > 0)
        {
            foreach (var k in prms.Select(kv => kv.Key))
                if (!type.Params.Any(p => p.Key == k)) throw new CliError($"'{name}' has no setting '{k}' (it has: {string.Join(", ", type.Params.Select(p => p.Key))})");
            script["params"] = prms.DeepClone();
        }
        var tracks = new JsonObject { ["x"] = Key(x), ["y"] = Key(y), ["w"] = Key(40), ["h"] = Key(40) };
        if (rot != 0) tracks["rot"] = Key(rot);
        var o = new JsonObject
        {
            ["id"] = id,
            ["name"] = objName ?? type.Title,
            ["shape"] = new JsonObject { ["type"] = "rect" },
            ["start"] = Math.Round(at, 3),
            ["end"] = Math.Round(stop, 3),
            ["script"] = script,
            ["tracks"] = tracks,
        };
        Objects.Add(o);
        return new JsonObject { ["id"] = id, ["start"] = at, ["end"] = Math.Round(stop, 3), ["copiedScripts"] = new JsonArray(copied.Select(c => (JsonNode)c).ToArray()) };
    }

    private static JsonArray Key(double v) => new(new JsonArray(0, Math.Round(v, 3)));

    /// <summary>Runs a pattern script and adds the objects it builds. Returns their ids and group.</summary>
    public JsonObject AddPattern(string name, double hit, double x, double y, JsonObject prms, List<string> logs)
    {
        var type = FindScript(name, out _) ?? throw new CliError($"no pattern named '{name}' in the level or the library");
        if (type.Kind != ScriptKind.Pattern) throw new CliError($"'{name}' is not a pattern (it runs live): add it with place_script / `beatform place`");
        var values = new Dictionary<string, object>();
        if (prms != null)
            foreach (var kv in prms)
            {
                if (!type.Params.Any(p => p.Key == kv.Key)) throw new CliError($"'{name}' has no setting '{kv.Key}' (it has: {string.Join(", ", type.Params.Select(p => p.Key))})");
                values[kv.Key] = ToClr(kv.Value);
            }
        values["hit"] = hit;
        values["x"] = x;
        values["y"] = y;
        var built = ScriptHost.BuildPattern(type, values, Bpm, IdMaker(), logs, out var err);
        if (err != null) throw new CliError("the pattern failed: " + err);
        int n = 1;
        var groups = Groups;
        while (groups.Any(kv => (string)kv.Value == name + n)) n++;
        string group = name + n;
        var ids = new JsonArray();
        foreach (var j in built)
        {
            var o = (JsonObject)JsonNode.Parse(j);
            if (o["shape"] == null) o["shape"] = new JsonObject { ["type"] = "rect" };
            Objects.Add(o);
            groups[(string)o["id"]] = group;
            ids.Add((string)o["id"]);
        }
        return new JsonObject { ["group"] = group, ["ids"] = ids, ["count"] = ids.Count };
    }

    // ---------------- edit operations ----------------

    /// <summary>Applies edit operations (see docs/ai-tools.md) and returns what changed.</summary>
    public JsonObject Apply(JsonArray ops)
    {
        var result = new JsonObject();
        var added = new JsonArray();
        int updated = 0, removed = 0, eventsAdded = 0, eventsRemoved = 0;
        var newId = IdMaker();
        foreach (var opNode in ops)
        {
            if (opNode is not JsonObject op) throw new CliError("each operation must be an object");
            string kind = (string)op["op"] ?? throw new CliError("an operation needs \"op\"");
            switch (kind)
            {
                case "add":
                {
                    var list = op["objects"] as JsonArray ?? (op["object"] is JsonObject one ? new JsonArray(one.DeepClone()) : throw new CliError("add needs \"objects\": [...]"));
                    var ids = Ids();
                    foreach (var n in list)
                    {
                        if (n is not JsonObject o) throw new CliError("objects must be JSON objects");
                        var c = (JsonObject)o.DeepClone();
                        var id = (string)c["id"];
                        if (string.IsNullOrEmpty(id)) c["id"] = id = newId();
                        else if (ids.Contains(id)) throw new CliError($"an object with id '{id}' already exists");
                        ids.Add(id);
                        if (c["shape"] == null) c["shape"] = new JsonObject { ["type"] = "rect" };
                        FitText(c);
                        Objects.Add(c);
                        added.Add(id);
                    }
                    break;
                }
                case "update":
                {
                    var set = op["set"] as JsonObject ?? throw new CliError("update needs \"set\": {...}");
                    foreach (var o in Select(op))
                    {
                        foreach (var kv in set)
                        {
                            if (kv.Key == "id") throw new CliError("ids cannot be changed with update");
                            if (kv.Value == null) o.Remove(kv.Key);
                            else o[kv.Key] = kv.Value.DeepClone();
                        }
                        updated++;
                    }
                    break;
                }
                case "shift":
                {
                    double by = Num(op["by"], double.NaN);
                    if (double.IsNaN(by)) throw new CliError("shift needs \"by\" (ms)");
                    foreach (var o in Select(op))
                    {
                        o["start"] = Math.Round(Num(o["start"], 0) + by, 3);
                        o["end"] = Math.Round(Num(o["end"], 0) + by, 3);
                        updated++;
                    }
                    break;
                }
                case "remove":
                {
                    var gone = new HashSet<string>(Select(op).Select(o => (string)o["id"]));
                    for (int i = Objects.Count - 1; i >= 0; i--)
                        if (Objects[i] is JsonObject o && gone.Contains((string)o["id"])) { Objects.RemoveAt(i); removed++; }
                    foreach (var id in gone) Groups.Remove(id);
                    break;
                }
                case "addEvents":
                {
                    var list = op["events"] as JsonArray ?? throw new CliError("addEvents needs \"events\": [...]");
                    foreach (var e in list)
                    {
                        if (e is not JsonObject eo || eo["t"] == null || eo["type"] == null) throw new CliError("every event needs \"t\" and \"type\"");
                        Events.Add(eo.DeepClone());
                        eventsAdded++;
                    }
                    break;
                }
                case "removeEvents":
                {
                    string type = (string)op["type"];
                    double from = Num(op["from"], double.NegativeInfinity), to = Num(op["to"], double.PositiveInfinity);
                    for (int i = Events.Count - 1; i >= 0; i--)
                    {
                        if (Events[i] is not JsonObject e) continue;
                        double t = Num(e["t"], 0);
                        if ((type == null || (string)e["type"] == type) && t >= from && t <= to) { Events.RemoveAt(i); eventsRemoved++; }
                    }
                    break;
                }
                case "meta":
                case "theme":
                case "settings":
                {
                    var set = op["set"] as JsonObject ?? throw new CliError($"{kind} needs \"set\": {{...}}");
                    var target = Root[kind] as JsonObject ?? (JsonObject)(Root[kind] = new JsonObject());
                    foreach (var kv in set)
                    {
                        if (kv.Value == null) target.Remove(kv.Key);
                        else target[kv.Key] = kv.Value.DeepClone();
                    }
                    break;
                }
                case "checkpoints":
                {
                    var set = op["set"] as JsonArray ?? throw new CliError("checkpoints needs \"set\": [ms, ...]");
                    var list = set.Select(n => Num(n, double.NaN)).Where(d => !double.IsNaN(d)).Append(0).Distinct().OrderBy(d => d);
                    Root["checkpoints"] = new JsonArray(list.Select(d => (JsonNode)d).ToArray());
                    break;
                }
                default:
                    throw new CliError($"unknown operation '{kind}' (add, update, shift, remove, addEvents, removeEvents, meta, theme, settings, checkpoints)");
            }
        }
        result["added"] = added;
        result["updated"] = updated;
        result["removed"] = removed;
        result["eventsAdded"] = eventsAdded;
        result["eventsRemoved"] = eventsRemoved;
        return result;
    }

    /// <summary>
    /// A text object without a width gets the width its letters need at its height, so they are not
    /// stretched (what the editor does when text is typed).
    /// </summary>
    private static void FitText(JsonObject o)
    {
        if ((string)o["shape"]?["type"] != "text") return;
        var tracks = o["tracks"] as JsonObject ?? (JsonObject)(o["tracks"] = new JsonObject());
        if (tracks["w"] != null) return;
        string text = (string)o["shape"]["text"] ?? "";
        bool bold = o["shape"]["bold"]?.GetValue<bool>() ?? true;
        double factor = TextFont.Width(text, bold);
        var keys = new JsonArray();
        if (tracks["h"] is JsonArray hk && hk.Count > 0)
        {
            foreach (var k in hk.OfType<JsonArray>())
                keys.Add(new JsonArray(k[0]?.DeepClone(), Math.Round(Num(k[1], 100) * factor, 2)));
        }
        else keys.Add(new JsonArray(0, Math.Round(100 * factor, 2)));
        tracks["w"] = keys;
    }

    /// <summary>Objects an operation names: "id", "ids", "group", or "from"/"to" (start times).</summary>
    private List<JsonObject> Select(JsonObject op)
    {
        var all = Objects.OfType<JsonObject>().ToList();
        if (op["id"] != null || op["ids"] != null)
        {
            var ids = new HashSet<string>();
            if (op["id"] != null) ids.Add((string)op["id"]);
            if (op["ids"] is JsonArray a) foreach (var i in a) ids.Add((string)i);
            var res = all.Where(o => ids.Contains((string)o["id"])).ToList();
            var missing = ids.Except(res.Select(o => (string)o["id"])).ToList();
            if (missing.Count > 0) throw new CliError("no object with id " + string.Join(", ", missing));
            return res;
        }
        if (op["group"] != null)
        {
            string g = (string)op["group"];
            var ids = new HashSet<string>(Groups.Where(kv => (string)kv.Value == g).Select(kv => kv.Key));
            if (ids.Count == 0) throw new CliError($"no group '{g}'");
            return all.Where(o => ids.Contains((string)o["id"])).ToList();
        }
        if (op["from"] != null || op["to"] != null)
        {
            double from = Num(op["from"], double.NegativeInfinity), to = Num(op["to"], double.PositiveInfinity);
            return all.Where(o => Num(o["start"], 0) >= from && Num(o["start"], 0) < to).ToList();
        }
        throw new CliError($"\"{(string)op["op"]}\" needs \"id\", \"ids\", \"group\" or \"from\"/\"to\"");
    }
}
