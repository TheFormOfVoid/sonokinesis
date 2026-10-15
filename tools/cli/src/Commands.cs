using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sonokinesis.Core;

namespace Sonokinesis.Cli;

/// <summary>What a command produced: data (shown as JSON) and any pictures.</summary>
public sealed class CommandResult
{
    public JsonNode Data;
    public readonly List<(string Path, byte[] Png)> Images = new();
    public string Text;
}

/// <summary>One command, shared by the command line and the MCP server.</summary>
public sealed class Command
{
    public string Name, Summary;
    /// <summary>Argument names that may be given without --name on the command line, in order.</summary>
    public string[] Positional = Array.Empty<string>();
    public JsonObject Schema;
    public Func<JsonObject, CommandResult> Run;
}

public static class Commands
{
    public static readonly List<Command> All = new();

    static Commands()
    {
        Add("docs", "Read the Sonokinesis documentation. Without a topic, lists the topics.",
            new[] { "topic" }, Props(("topic", "string", "A topic name from the list, e.g. 'scripting', 'level-format', 'ai-tools'.")),
            a => Docs.Read(Str(a, "topic")));

        Add("new_level", "Create a level folder from a song: copies the song, writes level.json, and detects the tempo and first beat unless given.",
            new[] { "folder" }, Props(("folder", "string", "New level folder (created)."), ("song", "string", "Song file (.ogg, .wav or .mp3)."),
                ("title", "string", "Song title."), ("artist", "string", "Song artist."), ("bpm", "number", "Tempo, if known."), ("offsetMs", "number", "Where the first beat is in the audio file (ms), if known.")),
            a =>
            {
                var folder = LevelFolder.Create(Req(a, "folder"), Req(a, "song"), Str(a, "title"), Str(a, "artist"), Num(a, "bpm"), Num(a, "offsetMs"), out var analysis);
                var lf = new LevelFolder(folder);
                var res = new JsonObject { ["folder"] = folder, ["meta"] = lf.Meta.DeepClone() };
                if (analysis != null) res["analysis"] = Summary(analysis);
                return Data(res);
            }, "folder", "song");

        Add("level_info", "Summarise a level: meta, timing, checkpoints, events, scripts and every object (id, name, what it is, start, end).",
            new[] { "folder" }, Props(("folder", "string", "Level folder."), ("objects", "boolean", "Include the object list (default true).")),
            a => Data(Info(new LevelFolder(Req(a, "folder")), Bool(a, "objects") ?? true)), "folder");

        Add("read_level", "Return level.json as text.", new[] { "folder" }, Props(("folder", "string", "Level folder.")),
            a => new CommandResult { Text = File.ReadAllText(new LevelFolder(Req(a, "folder")).LevelJson) }, "folder");

        Add("write_level", "Replace level.json with new JSON text (the old file is kept as level.json.bak). Run validate_level afterwards.",
            new[] { "folder" }, Props(("folder", "string", "Level folder."), ("json", "string", "The whole level.json.")),
            a =>
            {
                var lf = new LevelFolder(Req(a, "folder"));
                JsonObject root;
                try { root = JsonNode.Parse(Req(a, "json")) as JsonObject ?? throw new CliError("the JSON must be an object"); }
                catch (JsonException e) { throw new CliError("not valid JSON: " + e.Message); }
                lf.Root = root;
                lf.Save();
                return Data(new JsonObject { ["saved"] = lf.LevelJson, ["objects"] = lf.Objects.Count, ["events"] = lf.Events.Count });
            }, "folder", "json");

        Add("edit_level", "Change level.json with a list of operations: add, update, shift, remove (objects by id, ids, group or from/to), addEvents, removeEvents, meta, theme, settings, checkpoints. See docs topic 'ai-tools'.",
            new[] { "folder" }, Props(("folder", "string", "Level folder."), ("ops", "array", "Operations, e.g. [{\"op\":\"add\",\"objects\":[...]}].")),
            a =>
            {
                var lf = new LevelFolder(Req(a, "folder"));
                var ops = a["ops"] switch
                {
                    JsonArray arr => arr,
                    JsonObject one => new JsonArray(one.DeepClone()),
                    JsonValue v when v.TryGetValue<string>(out var s) => JsonNode.Parse(s.StartsWith("@") ? File.ReadAllText(s[1..]) : s) as JsonArray ?? throw new CliError("ops must be a JSON array"),
                    _ => throw new CliError("ops is required"),
                };
                var res = lf.Apply(ops);
                lf.Save();
                return Data(res);
            }, "folder", "ops");

        Add("list_scripts", "List scripts: the level's own (scripts/*.js) and the built-in library, with their kind, settings and errors.",
            new[] { "folder" }, Props(("folder", "string", "Level folder (optional: library only without it)."), ("kind", "string", "attack, effect or pattern to filter.")),
            a => Data(ListScripts(Str(a, "folder"), Str(a, "kind"))));

        Add("read_script", "Return a script's source: the level's copy, else the library's.",
            new[] { "name" }, Props(("name", "string", "Script name (file name without .js)."), ("folder", "string", "Level folder (optional).")),
            a =>
            {
                string name = Req(a, "name"), folder = Str(a, "folder");
                if (folder != null)
                {
                    var p = new LevelFolder(folder).ScriptPath(name);
                    if (File.Exists(p)) return new CommandResult { Text = $"// {p}\n" + File.ReadAllText(p) };
                }
                if (Paths.LibraryScripts().TryGetValue(name, out var f)) return new CommandResult { Text = $"// {f.Path} (library)\n" + File.ReadAllText(f.Path) };
                throw new CliError($"no script named '{name}'");
            }, "name");

        Add("write_script", "Create or replace a script in the level's scripts folder and report whether it compiles (and, for patterns, what it builds).",
            new[] { "folder", "name" }, Props(("folder", "string", "Level folder."), ("name", "string", "Script name: letters, digits, _ and -."), ("code", "string", "JavaScript source.")),
            a =>
            {
                var lf = new LevelFolder(Req(a, "folder"));
                string name = Req(a, "name");
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_\\-]+$")) throw new CliError("script names use letters, digits, _ and - only");
                string code = Req(a, "code");
                if (code.StartsWith("@") && File.Exists(code[1..])) code = File.ReadAllText(code[1..]);
                Directory.CreateDirectory(lf.ScriptsDir);
                File.WriteAllText(lf.ScriptPath(name), code);
                return Data(CheckScript(lf, name));
            }, "folder", "name", "code");

        Add("check_script", "Compile a level script and test-run it: patterns are built with their defaults, attacks are run for their length at the screen centre.",
            new[] { "folder", "name" }, Props(("folder", "string", "Level folder."), ("name", "string", "Script name.")),
            a => Data(CheckScript(new LevelFolder(Req(a, "folder")), Req(a, "name"))), "folder", "name");

        Add("place_script", "Add an object that runs a scripted attack or effect (copied from the library into the level if needed).",
            new[] { "folder", "name" }, Props(("folder", "string", "Level folder."), ("name", "string", "Script name."), ("at", "number", "Song time it starts (ms)."),
                ("end", "number", "Song time it stops (ms). Default: the script's length."), ("beats", "number", "Length in beats instead of end."),
                ("x", "number", "Spawn x (default 0)."), ("y", "number", "Spawn y (default 0)."), ("rot", "number", "Spawn rotation in degrees."),
                ("params", "object", "Settings, e.g. {\"count\": 16}."), ("label", "string", "Object name.")),
            a =>
            {
                var lf = new LevelFolder(Req(a, "folder"));
                var res = lf.PlaceScript(Req(a, "name"), Num(a, "at") ?? 0, Num(a, "end"), Num(a, "beats"), Num(a, "x") ?? 0, Num(a, "y") ?? 0, Num(a, "rot") ?? 0, Obj(a, "params"), Str(a, "label"));
                lf.Save();
                return Data(res);
            }, "folder", "name", "at");

        Add("add_pattern", "Run a pattern script and add the plain objects it builds (grouped, editable afterwards).",
            new[] { "folder", "name" }, Props(("folder", "string", "Level folder."), ("name", "string", "Pattern name."), ("at", "number", "The 'hit' time: when it becomes dangerous (ms)."),
                ("x", "number", "Where (default 0)."), ("y", "number", "Where (default 0)."), ("params", "object", "Settings.")),
            a =>
            {
                var lf = new LevelFolder(Req(a, "folder"));
                var logs = new List<string>();
                var res = lf.AddPattern(Req(a, "name"), Num(a, "at") ?? 0, Num(a, "x") ?? 0, Num(a, "y") ?? 0, Obj(a, "params"), logs);
                lf.Save();
                if (logs.Count > 0) res["logs"] = new JsonArray(logs.Select(l => (JsonNode)l).ToArray());
                return Data(res);
            }, "folder", "name", "at");

        Add("validate_level", "Check a level: structure, scripts (compiled and run over the whole level), and common mistakes. Fix every error before playing.",
            new[] { "folder" }, Props(("folder", "string", "Level folder."), ("stepMs", "number", "Simulation step (default 33.3).")),
            a => Data(Validator.Run(new LevelFolder(Req(a, "folder")), Num(a, "stepMs") ?? 1000.0 / 30)), "folder");

        Add("render_frames", "Draw the level at song times as the game would show it (hazards, a stand-in player, camera, flashes) and return the pictures.",
            new[] { "folder" }, Props(("folder", "string", "Level folder."), ("times", "array", "Song times in ms, e.g. [1000, 2500]."), ("width", "number", "Picture width (default 960)."),
                ("seed", "number", "Random seed for 'random each play' (default 0)."), ("playerX", "number", "Stand-in player x (default -300)."), ("playerY", "number", "Stand-in player y (default 150)."),
                ("out", "string", "Folder for the PNG files (default: a temp folder).")),
            a => Render(a, false), "folder", "times");

        Add("render_sheet", "Draw evenly spaced frames between two song times into one contact sheet (a quick look at a whole section).",
            new[] { "folder" }, Props(("folder", "string", "Level folder."), ("from", "number", "First song time (ms)."), ("to", "number", "Last song time (ms)."),
                ("count", "number", "Number of frames (default 12)."), ("columns", "number", "Frames per row (default 4)."), ("width", "number", "Width of each frame (default 400)."),
                ("seed", "number", "Random seed."), ("playerX", "number", "Stand-in player x."), ("playerY", "number", "Stand-in player y."), ("out", "string", "Output PNG path.")),
            a => Render(a, true), "folder", "from", "to");

        Add("analyze_song", "Listen to a song: tempo, first beat (offsetMs), loudness per bar, sections and strongest hits.",
            new[] { "path" }, Props(("path", "string", "Song file, or a level folder."), ("bpm", "number", "Known tempo (skips detection).")),
            a =>
            {
                var path = Req(a, "path");
                if (Directory.Exists(path))
                {
                    var lf = new LevelFolder(path);
                    path = Path.Combine(lf.Folder, (string)lf.Meta["audio"] ?? "");
                }
                if (!File.Exists(path)) throw new CliError($"song not found: {path}");
                return Data(SongAnalysis.Analyze(path, Num(a, "bpm")));
            }, "path");

        Add("beat_times", "Song times of beats on the level's grid (meta.bpm), with bar.beat labels.",
            new[] { "folder" }, Props(("folder", "string", "Level folder."), ("from", "number", "From song time (ms, default 0)."), ("to", "number", "To song time (ms, default +16 beats)."),
                ("every", "number", "Step in beats (default 1; 0.5 for half beats).")),
            a =>
            {
                var lf = new LevelFolder(Req(a, "folder"));
                double beat = lf.BeatMs, every = Math.Max(0.0625, Num(a, "every") ?? 1);
                double from = Num(a, "from") ?? 0, to = Num(a, "to") ?? from + 16 * beat;
                int per = (int)LevelFolder.Num(lf.Meta["beatsPerBar"], 4);
                var list = new JsonArray();
                for (double b = Math.Ceiling(from / beat / every - 1e-9) * every; b * beat <= to + 1e-6 && list.Count < 2000; b += every)
                    list.Add(new JsonObject { ["ms"] = Math.Round(b * beat, 3), ["beat"] = b, ["bar"] = $"{(int)Math.Floor(b / per) + 1}.{b - Math.Floor(b / per) * per + 1:0.###}" });
                return Data(new JsonObject { ["bpm"] = lf.Bpm, ["beatMs"] = Math.Round(beat, 4), ["beats"] = list });
            }, "folder");

        Add("play_in_game", "Start the level in the running game (with the Sonokinesis loader) from a song time.",
            new[] { "folder" }, Props(("folder", "string", "Level folder."), ("from", "number", "Song time to start from (ms, default 0).")),
            a => Data(GameLink.Play(new LevelFolder(Req(a, "folder")).Folder, Num(a, "from") ?? 0)), "folder");

        Add("game_status", "Is the game running, is the loader installed, what happened to the last play request.",
            Array.Empty<string>(), Props(), a => Data(GameLink.Status()));

        Add("game_screenshot", "Take a screenshot of the game (needs DevConsole = true in BepInEx/config/dev.sonokinesis.loader.cfg).",
            Array.Empty<string>(), Props(("out", "string", "PNG path (default: a temp file).")),
            a =>
            {
                var path = GameLink.Screenshot(Str(a, "out") ?? Path.Combine(TempDir("game"), $"game_{DateTime.Now:HHmmss}.png"));
                var r = new CommandResult { Data = new JsonObject { ["path"] = path } };
                r.Images.Add((path, File.ReadAllBytes(path)));
                return r;
            });

        Add("editor", "Send a command to the running Sonokinesis Editor: ping, state, open {folder}, reload {force}, save, seek {ms}, play {ms}, stop, select {ids}, quit {force}.",
            new[] { "command" }, Props(("command", "string", "Editor command."), ("args", "object", "Its arguments, e.g. {\"folder\": \"...\"}.")),
            a =>
            {
                var cmd = (Obj(a, "args")?.DeepClone() as JsonObject) ?? new JsonObject();
                cmd["cmd"] = Req(a, "command");
                var res = EditorLink.Send(cmd);
                if (res["ok"]?.GetValue<bool>() == false) throw new CliError("editor: " + (string)res["error"]);
                return Data(res);
            }, "command");

        Add("editor_screenshot", "Screenshot of the running Sonokinesis Editor, optionally after moving the playhead.",
            Array.Empty<string>(), Props(("ms", "number", "Move the playhead here first."), ("out", "string", "PNG path (default: a temp file).")),
            a =>
            {
                var path = Path.GetFullPath(Str(a, "out") ?? Path.Combine(TempDir("editor"), $"editor_{DateTime.Now:HHmmss}.png"));
                var cmd = new JsonObject { ["cmd"] = "shot", ["path"] = path };
                if (Num(a, "ms") is double ms) cmd["ms"] = ms;
                var res = EditorLink.Send(cmd);
                if (res["ok"]?.GetValue<bool>() == false) throw new CliError("editor: " + (string)res["error"]);
                var r = new CommandResult { Data = res };
                r.Images.Add((path, File.ReadAllBytes(path)));
                return r;
            });
    }

    // ---------------- helpers ----------------

    private static void Add(string name, string summary, string[] positional, JsonObject props, Func<JsonObject, CommandResult> run, params string[] required)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (required.Length > 0) schema["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray());
        All.Add(new Command { Name = name, Summary = summary, Positional = positional, Schema = schema, Run = run });
    }

    private static JsonObject Props(params (string Name, string Type, string Description)[] p)
    {
        var o = new JsonObject();
        foreach (var (n, t, d) in p)
        {
            var prop = new JsonObject { ["type"] = t, ["description"] = d };
            if (t == "array") prop["items"] = n == "times" ? new JsonObject { ["type"] = "number" } : new JsonObject { ["type"] = "object" };
            o[n] = prop;
        }
        return o;
    }

    private static CommandResult Data(JsonNode n) => new() { Data = n };

    public static string Str(JsonObject a, string k) => a[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : a[k]?.ToJsonString();
    private static string Req(JsonObject a, string k) => Str(a, k) is string s && s.Length > 0 ? s : throw new CliError($"\"{k}\" is required");
    public static double? Num(JsonObject a, string k) => a[k] == null ? null : LevelFolder.Num(a[k], double.NaN) is var d && !double.IsNaN(d) ? d : throw new CliError($"\"{k}\" must be a number");
    private static bool? Bool(JsonObject a, string k) => a[k] is JsonValue v ? (v.TryGetValue<bool>(out var b) ? b : v.TryGetValue<string>(out var s) ? s is "true" or "1" or "yes" : null) : null;

    private static JsonObject Obj(JsonObject a, string k) => a[k] switch
    {
        JsonObject o => o,
        JsonValue v when v.TryGetValue<string>(out var s) && s.Length > 0 => JsonNode.Parse(s.StartsWith("@") ? File.ReadAllText(s[1..]) : s) as JsonObject ?? throw new CliError($"\"{k}\" must be a JSON object"),
        null => null,
        _ => throw new CliError($"\"{k}\" must be a JSON object"),
    };

    private static string TempDir(string sub)
    {
        var d = Path.Combine(Path.GetTempPath(), "sonokinesis", sub);
        Directory.CreateDirectory(d);
        return d;
    }

    private static JsonObject Summary(JsonObject analysis) => new()
    {
        ["bpm"] = analysis["bpm"]?.DeepClone(),
        ["offsetMs"] = analysis["offsetMs"]?.DeepClone(),
        ["lengthMs"] = analysis["lengthMs"]?.DeepClone(),
        ["bpmCandidates"] = analysis["bpmCandidates"]?.DeepClone(),
        ["sections"] = analysis["sections"]?.DeepClone(),
    };

    // ---------------- implementations ----------------

    private static JsonObject Info(LevelFolder lf, bool withObjects)
    {
        var objs = lf.Objects.OfType<JsonObject>().ToList();
        var groups = lf.Groups;
        var res = new JsonObject
        {
            ["folder"] = lf.Folder,
            ["meta"] = lf.Meta.DeepClone(),
            ["theme"] = lf.Root["theme"]?.DeepClone(),
            ["lengthMs"] = Math.Round(lf.LengthMs(), 1),
            ["beatMs"] = Math.Round(lf.BeatMs, 4),
            ["checkpoints"] = lf.Root["checkpoints"]?.DeepClone(),
            ["objectCount"] = objs.Count,
            ["events"] = lf.Events.DeepClone(),
        };
        var lib = new ScriptLibrary(lf.LevelScriptSources());
        res["scripts"] = new JsonArray(lib.All.OrderBy(t => t.Name).Select(t => (JsonNode)new JsonObject
        {
            ["name"] = t.Name,
            ["kind"] = t.Kind == ScriptKind.Pattern ? "pattern" : "attack",
            ["error"] = t.CompileError?.ToString(),
        }).ToArray());
        if (withObjects)
        {
            var list = new JsonArray();
            foreach (var o in objs.OrderBy(o => LevelFolder.Num(o["start"], 0)))
            {
                string what = o["script"] != null ? "script " + (string)o["script"]?["name"]
                    : o["imposter"] != null ? "imposter " + (string)o["shape"]?["type"]
                    : (string)o["shape"]?["type"] ?? "?";
                if ((string)o["shape"]?["type"] == "text" && o["script"] == null) what += $" \"{(string)o["shape"]?["text"]}\"";
                var e = new JsonObject
                {
                    ["id"] = o["id"]?.DeepClone(),
                    ["name"] = o["name"]?.DeepClone(),
                    ["what"] = what,
                    ["start"] = o["start"]?.DeepClone(),
                    ["end"] = o["end"]?.DeepClone(),
                };
                if (groups[(string)o["id"] ?? ""] is JsonNode g) e["group"] = g.DeepClone();
                if (o["parent"] != null) e["parent"] = o["parent"].DeepClone();
                if (o["hitbox"]?.GetValue<bool>() == false) e["hitbox"] = false;
                list.Add(e);
            }
            res["objects"] = list;
        }
        return res;
    }

    private static JsonObject ListScripts(string folder, string kind)
    {
        var res = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddAll(IEnumerable<ScriptType> types, string where, Func<string, string> category)
        {
            foreach (var t in types.OrderBy(t => t.Name))
            {
                if (!seen.Add(t.Name)) continue;
                string k = t.Kind == ScriptKind.Pattern ? "pattern" : category(t.Name) == "effects" || t.Category is "Camera" or "Screen" or "Effects" ? "effect" : "attack";
                if (kind != null && !k.Equals(kind, StringComparison.OrdinalIgnoreCase)) continue;
                res.Add(new JsonObject
                {
                    ["name"] = t.Name,
                    ["title"] = t.Title,
                    ["kind"] = k,
                    ["where"] = where,
                    ["description"] = t.Description,
                    ["lengthBeats"] = t.LengthBeats > 0 ? t.LengthBeats : null,
                    ["uses"] = t.Uses.Length > 0 ? new JsonArray(t.Uses.Select(u => (JsonNode)u).ToArray()) : null,
                    ["params"] = new JsonArray(t.Params.Select(p => (JsonNode)new JsonObject
                    {
                        ["key"] = p.Key,
                        ["type"] = p.Type,
                        ["label"] = p.Label,
                        ["default"] = JsonValue.Create(p.Default),
                        ["min"] = p.Min > -1e9 ? p.Min : null,
                        ["max"] = p.Max < 1e9 ? p.Max : null,
                        ["options"] = p.Options != null ? new JsonArray(p.Options.Select(x => (JsonNode)x).ToArray()) : null,
                    }).ToArray()),
                    ["error"] = t.CompileError?.ToString(),
                });
            }
        }
        if (folder != null)
        {
            var lf = new LevelFolder(folder);
            AddAll(new ScriptLibrary(lf.LevelScriptSources()).All, "level", _ => "");
        }
        var lib = LevelFolder.LibraryScripts(out var files);
        AddAll(lib.All, "library", n => files.TryGetValue(n, out var f) ? f.Category : "");
        return new JsonObject { ["library"] = Paths.Library, ["scripts"] = res };
    }

    /// <summary>Compiles a level script and gives it a short trial run.</summary>
    private static JsonObject CheckScript(LevelFolder lf, string name)
    {
        var path = lf.ScriptPath(name);
        if (!File.Exists(path)) throw new CliError($"no scripts/{name}.js in the level");
        var type = new ScriptLibrary(lf.LevelScriptSources()).Get(name);
        var res = new JsonObject { ["path"] = path };
        if (type == null) { res["ok"] = false; return res; }
        res["kind"] = type.Kind == ScriptKind.Pattern ? "pattern" : "attack";
        res["title"] = type.Title;
        res["params"] = new JsonArray(type.Params.Select(p => (JsonNode)$"{p.Key} ({p.Type}, default {Convert.ToString(p.Default, System.Globalization.CultureInfo.InvariantCulture)})").ToArray());
        if (type.CompileError != null) { res["ok"] = false; res["error"] = type.CompileError.ToString(); return res; }
        var logs = new List<string>();
        if (type.Kind == ScriptKind.Pattern)
        {
            int n = 0;
            var built = ScriptHost.BuildPattern(type, new Dictionary<string, object> { ["hit"] = 4000.0, ["x"] = 0.0, ["y"] = 0.0 }, lf.Bpm, () => "t" + (++n), logs, out var err);
            res["ok"] = err == null;
            if (err != null) res["error"] = err.ToString();
            res["trial"] = $"built {built.Count} objects with the default settings at hit 4000 ms";
        }
        else
        {
            // A throwaway level with just this script, run for its length.
            double beats = type.LengthBeats > 0 ? type.LengthBeats : 8;
            var data = lf.Load();
            var trial = LevelData.Parse(new JsonObject
            {
                ["format"] = LevelData.FormatName, ["formatVersion"] = 2,
                ["meta"] = new JsonObject { ["id"] = "trial", ["title"] = "trial", ["audio"] = "x.ogg", ["bpm"] = lf.Bpm },
                ["objects"] = new JsonArray(new JsonObject
                {
                    ["id"] = "trial", ["shape"] = new JsonObject { ["type"] = "rect" }, ["start"] = 1000, ["end"] = 1000 + beats * lf.BeatMs,
                    ["script"] = new JsonObject { ["name"] = name },
                }),
            }.ToJsonString());
            // Only this script and the ones that compile: another broken script must not fail the trial.
            var all = new ScriptLibrary(data.ScriptSources);
            foreach (var kv in data.ScriptSources)
                if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase) || all.Get(kv.Key)?.CompileError == null) trial.ScriptSources[kv.Key] = kv.Value;
            var r = new LevelRenderer(trial, new RenderOptions());
            var items = new List<DrawItem>();
            int maxDraws = 0;
            for (double t = 1000; t < 1000 + beats * lf.BeatMs; t += 1000.0 / 30)
            {
                r.Ev.Collect(t, items);
                maxDraws = Math.Max(maxDraws, items.Count);
            }
            var host = r.Ev.Scripts;
            var errs = host?.Errors.Select(e => e.ToString()).ToList() ?? new List<string>();
            res["ok"] = errs.Count == 0;
            if (errs.Count > 0) res["error"] = string.Join("\n", errs);
            res["trial"] = $"ran {beats} beats at the screen centre: at most {maxDraws} shapes on screen";
            if (host != null) logs.AddRange(host.Logs.Take(50));
        }
        if (logs.Count > 0) res["logs"] = new JsonArray(logs.Select(l => (JsonNode)l).ToArray());
        return res;
    }

    private static CommandResult Render(JsonObject a, bool sheet)
    {
        var lf = new LevelFolder(Req(a, "folder"));
        var opt = new RenderOptions
        {
            Width = (int)(Num(a, "width") ?? (sheet ? 400 : 960)),
            Seed = (int)(Num(a, "seed") ?? 0),
            Player = new Vec2(Num(a, "playerX") ?? -300, Num(a, "playerY") ?? 150),
        };
        opt.Width = Math.Clamp(opt.Width, 64, 3840);
        var r = new LevelRenderer(lf.Load(), opt);
        var result = new CommandResult();
        string name = Path.GetFileName(lf.Folder.TrimEnd(Path.DirectorySeparatorChar));
        if (sheet)
        {
            double from = Num(a, "from") ?? 0, to = Num(a, "to") ?? from + 8 * lf.BeatMs;
            int count = Math.Clamp((int)(Num(a, "count") ?? 12), 1, 64);
            var times = Enumerable.Range(0, count).Select(i => count == 1 ? from : from + (to - from) * i / (count - 1)).ToList();
            var png = r.Sheet(times, (int)(Num(a, "columns") ?? 4)).ToPng();
            var path = Path.GetFullPath(Str(a, "out") ?? Path.Combine(TempDir("renders"), $"{Safe(name)}_{from:0}-{to:0}.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, png);
            result.Images.Add((path, png));
            result.Data = new JsonObject { ["path"] = path, ["times"] = new JsonArray(times.Select(t => (JsonNode)Math.Round(t, 1)).ToArray()) };
            return result;
        }
        var list = a["times"] switch
        {
            JsonArray arr => arr.Select(n => LevelFolder.Num(n, double.NaN)).ToList(),
            JsonValue v when v.TryGetValue<string>(out var s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToList(),
            JsonValue v when v.TryGetValue<double>(out var d) => new List<double> { d },
            _ => throw new CliError("\"times\" is required: song times in ms"),
        };
        if (list.Any(double.IsNaN)) throw new CliError("times must be numbers");
        if (list.Count > 32) throw new CliError("at most 32 frames at once (use render_sheet for an overview)");
        var outDir = Str(a, "out") ?? TempDir("renders");
        Directory.CreateDirectory(outDir);
        var paths = new JsonArray();
        foreach (var t in list.OrderBy(t => t))
        {
            var png = r.Frame(t).ToPng();
            var path = Path.GetFullPath(Path.Combine(outDir, $"{Safe(name)}_{t:0}.png"));
            File.WriteAllBytes(path, png);
            result.Images.Add((path, png));
            paths.Add(path);
        }
        result.Data = new JsonObject { ["paths"] = paths };
        return result;
    }

    private static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}
