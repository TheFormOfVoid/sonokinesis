using System;
using System.Collections.Generic;
using System.Globalization;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Sonokinesis.Core;

/// <summary>What a script is for: an attack runs every frame in the game; a pattern builds ordinary objects in the editor.</summary>
public enum ScriptKind { Attack, Pattern }

/// <summary>A parameter a script declares in its <c>meta.params</c>; the editor shows a field for it.</summary>
public sealed class ScriptParam
{
    public string Key = "";
    /// <summary>number, int, beats, text, bool, choice, color, ease or shape.</summary>
    public string Type = "number";
    public string Label = "";
    public string Tooltip = "";
    public object Default;
    public double Min = -1e9, Max = 1e9, Step = 1;
    public string[] Options;
}

/// <summary>A problem in a script: where it is and what went wrong.</summary>
public sealed class ScriptError
{
    public string Script = "";
    public string Message = "";
    /// <summary>1-based line in the script file, 0 if unknown.</summary>
    public int Line;
    public override string ToString() => Line > 0 ? $"{Script}.js:{Line}: {Message}" : $"{Script}.js: {Message}";
}

/// <summary>
/// One script file (scripts/NAME.js in a level folder), compiled once in its own sandboxed JavaScript
/// engine: no files, no network, no clock, no .NET access. Instances of the script share the engine and
/// keep their own state on <c>this</c>. See docs/scripting.md.
/// </summary>
public sealed class ScriptType
{
    public readonly string Name;
    public readonly string Source;
    public ScriptKind Kind = ScriptKind.Attack;
    public string Title = "";
    public string Description = "";
    public string Category = "";
    /// <summary>How long a new instance lasts when placed, in beats (meta.length; 0 = the editor's default).</summary>
    public double LengthBeats;
    /// <summary>Other scripts this one spawns (meta.uses), copied along with it.</summary>
    public string[] Uses = Array.Empty<string>();
    public readonly List<ScriptParam> Params = new();
    /// <summary>Set when the script failed to compile; it then does nothing.</summary>
    public ScriptError CompileError;

    internal Engine Engine;
    internal JsValue StartFn = JsValue.Undefined, UpdateFn = JsValue.Undefined, DrawFn = JsValue.Undefined, BuildFn = JsValue.Undefined;

    public bool HasUpdate => !UpdateFn.IsUndefined();

    public ScriptType(string name, string source)
    {
        Name = name;
        Source = source ?? "";
        Title = name;
        try
        {
            Engine = new Engine(o => o
                .Strict(false)
                .LimitRecursion(200)
                .MaxStatements(20_000_000)
                .TimeoutInterval(TimeSpan.FromSeconds(2))
                .LimitMemory(256L * 1024 * 1024));
            ScriptApi.Install(Engine);
            Engine.Execute(Source, name + ".js");
            StartFn = Fn("start");
            UpdateFn = Fn("update");
            DrawFn = Fn("draw");
            BuildFn = Fn("build");
            ReadMeta();
            if (Kind == ScriptKind.Pattern && BuildFn.IsUndefined())
                CompileError = new ScriptError { Script = name, Message = "a pattern needs a build(p) function" };
        }
        catch (Exception e)
        {
            CompileError = ScriptApi.ToError(name, e);
        }
    }

    private JsValue Fn(string name)
    {
        var v = Engine.GetValue(name);
        return v is Jint.Native.Function.Function ? v : JsValue.Undefined;
    }

    private void ReadMeta()
    {
        var meta = Engine.GetValue("meta");
        if (!meta.IsObject()) return;
        var m = meta.AsObject();
        Title = Str(m.Get("name")) ?? Name;
        Description = Str(m.Get("description")) ?? "";
        Category = Str(m.Get("category")) ?? "";
        if (Str(m.Get("kind")) == "pattern") Kind = ScriptKind.Pattern;
        if (m.Get("length").IsNumber()) LengthBeats = Math.Max(0, m.Get("length").AsNumber());
        var uses = m.Get("uses");
        if (uses.IsArray())
        {
            var ua = uses.AsArray();
            var ul = new List<string>();
            for (uint i = 0; i < ua.Length; i++) ul.Add(TypeConverter.ToString(ua[i]));
            Uses = ul.ToArray();
        }
        var ps = m.Get("params");
        if (!ps.IsObject()) return;
        var po = ps.AsObject();
        foreach (var key in po.GetOwnPropertyKeys())
        {
            var def = po.Get(key);
            var p = new ScriptParam { Key = key.ToString(), Label = key.ToString() };
            if (def.IsObject())
            {
                var d = def.AsObject();
                p.Type = Str(d.Get("type")) ?? "number";
                p.Label = Str(d.Get("label")) ?? p.Key;
                p.Tooltip = Str(d.Get("tooltip")) ?? "";
                p.Default = ScriptApi.ToClr(d.Get("default"));
                if (d.Get("min").IsNumber()) p.Min = d.Get("min").AsNumber();
                if (d.Get("max").IsNumber()) p.Max = d.Get("max").AsNumber();
                if (d.Get("step").IsNumber()) p.Step = d.Get("step").AsNumber();
                var opts = d.Get("options");
                if (opts.IsArray())
                {
                    var arr = opts.AsArray();
                    var list = new List<string>();
                    for (uint i = 0; i < arr.Length; i++) list.Add(TypeConverter.ToString(arr[i]));
                    p.Options = list.ToArray();
                }
            }
            else
            {
                // A bare default: "count: 8" or "label: 'HI'".
                p.Default = ScriptApi.ToClr(def);
                p.Type = def.IsString() ? "text" : def.IsBoolean() ? "bool" : "number";
            }
            p.Default ??= p.Type switch { "text" or "color" or "ease" or "shape" or "choice" => (object)(p.Options?.Length > 0 ? p.Options[0] : ""), "bool" => false, _ => 0.0 };
            if (p.Type == "int") p.Step = Math.Max(1, Math.Round(p.Step));
            Params.Add(p);
        }
    }

    private static string Str(JsValue v) => v.IsString() ? v.AsString() : null;

    /// <summary>The parameters an instance gets: the script's defaults, overridden by the level's values.</summary>
    public Dictionary<string, object> MergeParams(IReadOnlyDictionary<string, object> values)
    {
        var res = new Dictionary<string, object>();
        foreach (var p in Params) res[p.Key] = p.Default;
        if (values != null) foreach (var kv in values) res[kv.Key] = kv.Value;
        return res;
    }
}

/// <summary>The scripts of one level, by name (file name without .js).</summary>
public sealed class ScriptLibrary
{
    private readonly Dictionary<string, ScriptType> types = new(StringComparer.OrdinalIgnoreCase);

    public ScriptLibrary(IReadOnlyDictionary<string, string> sources)
    {
        if (sources == null) return;
        foreach (var kv in sources) types[kv.Key] = new ScriptType(kv.Key, kv.Value);
    }

    public ScriptType Get(string name) => name != null && types.TryGetValue(name, out var t) ? t : null;
    public IEnumerable<ScriptType> All => types.Values;

    public IEnumerable<ScriptError> CompileErrors
    {
        get { foreach (var t in types.Values) if (t.CompileError != null) yield return t.CompileError; }
    }

    internal static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
