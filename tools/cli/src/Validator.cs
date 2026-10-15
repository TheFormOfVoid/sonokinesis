using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Sonokinesis.Core;

namespace Sonokinesis.Cli;

/// <summary>
/// Checks a level the way the game will read it: the file's structure, its scripts (compiled and run
/// over the whole level), and things that make a level unfair or broken.
/// </summary>
public static class Validator
{
    private static readonly HashSet<string> ShapeTypes = new() { "rect", "circle", "polygon", "star", "ring", "spikes", "text" };
    private static readonly HashSet<string> EventTypes = new() { "shake", "flash", "background", "sway", "sound", "playerSpeed", "zoom", "credits" };
    private static readonly HashSet<string> Sounds = new() { "playerDeath", "playerHit", "playerRespawn", "explosion" };
    private static readonly HashSet<string> TrackNames = new() { "x", "y", "rot", "w", "h", "opacity" };
    public static readonly HashSet<string> Eases = new()
    {
        "linear", "step", "inSine", "outSine", "inOutSine", "inQuad", "outQuad", "inOutQuad", "inCubic", "outCubic", "inOutCubic",
        "inQuart", "outQuart", "inOutQuart", "inQuint", "outQuint", "inOutQuint", "inExpo", "outExpo", "inOutExpo",
        "inCirc", "outCirc", "inOutCirc", "inBack", "outBack", "inOutBack", "inElastic", "outElastic", "inOutElastic",
        "inBounce", "outBounce", "inOutBounce",
    };

    public static JsonObject Run(LevelFolder lf, double stepMs = 1000.0 / 30, bool simulate = true)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var root = lf.Root;
        void E(string s) => errors.Add(s);
        void W(string s) => warnings.Add(s);
        double N(JsonNode n, double f) => LevelFolder.Num(n, f);

        if (!LevelData.IsFormatName((string)root["format"])) E("\"format\" must be \"sonokinesis.level\"");
        int version = (int)N(root["formatVersion"], 1);
        if (version > LevelData.SupportedFormatVersion) E($"formatVersion {version} is newer than this tool supports ({LevelData.SupportedFormatVersion})");

        var meta = root["meta"] as JsonObject;
        if (meta == null) E("missing \"meta\"");
        else
        {
            if (string.IsNullOrWhiteSpace((string)meta["id"])) E("meta.id is required");
            if (string.IsNullOrWhiteSpace((string)meta["title"])) W("meta.title is empty");
            var audio = (string)meta["audio"];
            if (string.IsNullOrEmpty(audio)) E("meta.audio is required");
            else if (!File.Exists(Path.Combine(lf.Folder, audio))) E($"the song '{audio}' is not in the level folder");
            if (N(meta["bpm"], 120) <= 0) E("meta.bpm must be above 0");
        }

        var cps = (root["checkpoints"] as JsonArray)?.Select(c => N(c, double.NaN)).ToList() ?? new List<double> { 0 };
        if (cps.Count == 0 || cps[0] != 0) E("checkpoints must start with 0");
        for (int i = 1; i < cps.Count; i++) if (!(cps[i] > cps[i - 1])) E("checkpoints must be sorted and different");

        double length = lf.LengthMs();
        var objs = (root["objects"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
        var byId = new Dictionary<string, JsonObject>();
        var levelScripts = new ScriptLibrary(lf.LevelScriptSources());
        var library = LevelFolder.LibraryScripts(out _);
        int scripted = 0;

        foreach (var o in objs)
        {
            var id = (string)o["id"];
            string who = $"object {id ?? "(no id)"}{((string)o["name"] is string nm && nm.Length > 0 ? $" \"{nm}\"" : "")}";
            if (string.IsNullOrEmpty(id)) { E($"{who}: needs an id"); continue; }
            if (!byId.TryAdd(id, o)) E($"{who}: the id is used twice");
            double start = N(o["start"], double.NaN), end = N(o["end"], double.NaN);
            if (double.IsNaN(start) || double.IsNaN(end)) E($"{who}: start and end must be numbers");
            else
            {
                if (end <= start) E($"{who}: end ({end}) must be after start ({start})");
                double warn = N(o["warn"], 0), outro = N(o["outro"], 0);
                if (warn < 0 || outro < 0) E($"{who}: warn and outro cannot be negative");
                if (o["script"] == null && o["hitbox"]?.GetValue<bool>() != false && warn + outro >= end - start) W($"{who}: warn + outro fill its whole life, so it never hurts");
                if (start >= length && length > 0) W($"{who}: starts at {start} ms, after the level ends ({length:0} ms)");
            }
            var shape = o["shape"] as JsonObject;
            string stype = (string)shape?["type"];
            if (o["script"] == null)
            {
                if (stype == null) E($"{who}: needs a shape with a type");
                else if (!ShapeTypes.Contains(stype)) E($"{who}: unknown shape type '{stype}' ({string.Join(", ", ShapeTypes)})");
            }
            if (o["tracks"] is JsonObject tracks)
            {
                foreach (var kv in tracks)
                {
                    if (!TrackNames.Contains(kv.Key)) { W($"{who}: unknown track '{kv.Key}'"); continue; }
                    if (kv.Value is not JsonArray keys) { E($"{who}: track {kv.Key} must be a list of keys"); continue; }
                    double prev = double.NegativeInfinity;
                    foreach (var k in keys)
                    {
                        if (k is not JsonArray ka || ka.Count < 2) { E($"{who}: track {kv.Key} keys must be [t, value] or [t, value, ease]"); break; }
                        double kt = N(ka[0], double.NaN), kvv = N(ka[1], double.NaN);
                        if (double.IsNaN(kt) || double.IsNaN(kvv)) { E($"{who}: track {kv.Key} has a key that is not numbers"); break; }
                        if (kt < prev) E($"{who}: track {kv.Key} keys are not sorted by time");
                        prev = kt;
                        if (ka.Count > 2 && (string)ka[2] is string ease && !Eases.Contains(ease)) W($"{who}: unknown ease '{ease}' (treated as linear)");
                    }
                }
            }
            if (o["color"] is JsonValue cv && cv.TryGetValue<string>(out var col) && col != "@player" && !IsColor(col)) E($"{who}: color '{col}' is not #rrggbb or #rrggbbaa");
            if (o["script"] != null)
            {
                scripted++;
                var sname = (string)o["script"]?["name"];
                if (string.IsNullOrEmpty(sname)) { E($"{who}: script needs a name"); continue; }
                var t = levelScripts.Get(sname);
                if (t == null)
                {
                    if (library.Get(sname) != null) E($"{who}: script '{sname}' is in the library but not in the level's scripts folder (copy it: `sonokinesis place` / place_script do that)");
                    else E($"{who}: no script named '{sname}' in scripts/");
                }
                else if (t.Kind == ScriptKind.Pattern) E($"{who}: '{sname}' is a pattern; patterns build objects in the editor and cannot run in the game");
                else if (o["script"]?["params"] is JsonObject prms)
                    foreach (var kv in prms)
                        if (!t.Params.Any(p => p.Key == kv.Key)) W($"{who}: script '{sname}' has no setting '{kv.Key}'");
            }
        }

        // Parents: they must exist and not loop.
        foreach (var o in objs)
        {
            var parent = (string)o["parent"];
            if (parent == null) continue;
            if (parent.StartsWith("@player"))
            {
                if (parent is not ("@player" or "@player1" or "@player2" or "@player3" or "@player4")) E($"object {o["id"]}: unknown player parent '{parent}'");
                continue;
            }
            if (!byId.ContainsKey(parent)) { E($"object {o["id"]}: parent '{parent}' does not exist"); continue; }
            var seen = new HashSet<string> { (string)o["id"] };
            var p = parent;
            for (int depth = 0; p != null && byId.TryGetValue(p, out var po); depth++)
            {
                if (!seen.Add(p)) { E($"object {o["id"]}: its parents form a loop"); break; }
                if (depth > LevelEvaluator.MaxParentDepth) { E($"object {o["id"]}: more than {LevelEvaluator.MaxParentDepth} levels of parents"); break; }
                p = (string)po["parent"];
            }
        }

        foreach (var e in (root["events"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            string type = (string)e["type"];
            double t = N(e["t"], double.NaN);
            string who = $"event {type} at {t}";
            if (double.IsNaN(t)) E($"event {type}: \"t\" must be a number");
            if (type == null || !EventTypes.Contains(type)) { E($"{who}: unknown type ({string.Join(", ", EventTypes)})"); continue; }
            if (type == "sound" && !Sounds.Contains((string)e["sound"] ?? "")) E($"{who}: sound must be one of {string.Join(", ", Sounds)}");
            if (e["ease"] is JsonValue ev && ev.TryGetValue<string>(out var ease) && !Eases.Contains(ease)) W($"{who}: unknown ease '{ease}'");
            if (e["color"] is JsonValue ec && ec.TryGetValue<string>(out var c) && !IsColor(c)) E($"{who}: color '{c}' is not #rrggbb or #rrggbbaa");
        }

        foreach (var err in levelScripts.CompileErrors) E("script " + err);

        // Run the level from start to end like the game would, collecting script errors and logs.
        var logs = new List<string>();
        var runtime = new List<string>();
        int maxItems = 0;
        double maxItemsAt = 0;
        if (simulate && errors.Count == 0)
        {
            try
            {
                var data = lf.Load();
                var r = new LevelRenderer(data, new RenderOptions());
                var items = new List<DrawItem>();
                var signals = new List<ScriptSignal>();
                double prev = -1;
                for (double t = 0; t <= length; t += stepMs)
                {
                    r.Ev.Collect(t, items);
                    r.Ev.CameraAt(t);
                    r.Ev.ScriptSignals(prev, t, signals);
                    prev = t;
                    if (items.Count > maxItems) { maxItems = items.Count; maxItemsAt = t; }
                }
                var host = r.Ev.Scripts;
                if (host != null)
                {
                    foreach (var er in host.Errors) runtime.Add(er.ToString());
                    logs.AddRange(host.Logs.Take(200));
                }
            }
            catch (Exception ex) { E("the level could not be run: " + ex.Message); }
        }
        foreach (var r in runtime) E("script " + r);
        if (maxItems > 3000) W($"{maxItems} shapes at once around {maxItemsAt / 1000:0.0}s: this may run slowly in the game");

        return new JsonObject
        {
            ["ok"] = errors.Count == 0,
            ["errors"] = new JsonArray(errors.Select(s => (JsonNode)s).ToArray()),
            ["warnings"] = new JsonArray(warnings.Distinct().Take(100).Select(s => (JsonNode)s).ToArray()),
            ["objects"] = objs.Count,
            ["scriptedObjects"] = scripted,
            ["events"] = (root["events"] as JsonArray)?.Count ?? 0,
            ["lengthMs"] = Math.Round(length, 1),
            ["mostShapesAtOnce"] = maxItems,
            ["scriptLogs"] = new JsonArray(logs.Select(s => (JsonNode)s).ToArray()),
        };
    }

    public static bool IsColor(string s) =>
        s != null && s.Length is 7 or 9 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
}
