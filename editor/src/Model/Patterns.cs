using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Beatform.Core;

namespace Beatform.Editor.Model;

/// <summary>A pattern parameter shown in the pattern dialog.</summary>
public sealed class PatternParam
{
    public string Key, Label, Tooltip;
    /// <summary>number, int, beats, text, bool, choice, color, ease or shape (as declared by the script).</summary>
    public string Kind = "number";
    public double Value, Min, Max, Step = 1;
    /// <summary>For text, color, ease, shape and choice parameters.</summary>
    public string Text = "";
    public bool Flag;
    /// <summary>Options of a choice (and the eases and shapes).</summary>
    public string[] Choices;

    public object Current => Kind switch
    {
        "text" or "color" or "ease" or "shape" or "choice" => Text,
        "bool" => Flag,
        _ => Value,
    };
}

/// <summary>
/// A pattern: a pattern script (library/patterns or the level's scripts) whose build(p) turns a few
/// parameters into ordinary objects. Times are built around the "hit" time (when the pattern becomes
/// dangerous), which is the playhead when the pattern is added.
/// </summary>
public sealed class Pattern
{
    public readonly ScriptType Script;
    public string Id => Script.Name;
    public string Name => Script.Title;
    public string Description => Script.Description;
    public readonly List<PatternParam> Params = new();

    public static readonly string[] Eases =
    {
        "linear", "step", "inSine", "outSine", "inOutSine", "inQuad", "outQuad", "inOutQuad", "inCubic", "outCubic", "inOutCubic",
        "inQuart", "outQuart", "inOutQuart", "inQuint", "outQuint", "inOutQuint", "inExpo", "outExpo", "inOutExpo",
        "inCirc", "outCirc", "inOutCirc", "inBack", "outBack", "inOutBack", "inElastic", "outElastic", "inOutElastic",
        "inBounce", "outBounce", "inOutBounce",
    };
    public static readonly string[] ShapeNames = { "rect", "circle", "polygon", "star", "ring", "spikes" };

    public Pattern(ScriptType script)
    {
        Script = script;
        foreach (var p in script.Params) Params.Add(ToParam(p));
    }

    /// <summary>A dialog field for a script parameter, starting at its default.</summary>
    public static PatternParam ToParam(ScriptParam p)
    {
        var res = new PatternParam { Key = p.Key, Label = p.Label, Tooltip = p.Tooltip, Kind = p.Type, Min = p.Min, Max = p.Max, Step = p.Step };
        res.Choices = p.Type switch { "ease" => Eases, "shape" => ShapeNames, _ => p.Options };
        switch (p.Default)
        {
            case double d: res.Value = d; break;
            case bool b: res.Flag = b; break;
            case string s: res.Text = s; break;
        }
        if (res.Kind is "choice" or "ease" or "shape" && string.IsNullOrEmpty(res.Text) && res.Choices?.Length > 0) res.Text = res.Choices[0];
        if (res.Min <= -1e9) res.Min = -100000;
        if (res.Max >= 1e9) res.Max = 100000;
        return res;
    }

    /// <summary>Builds the objects. <paramref name="hit"/> is the song time the pattern becomes dangerous.</summary>
    public List<EditObject> Build(LevelDocument doc, double hit, double x, double y, out ScriptError error, List<string> logs = null)
    {
        var values = Params.ToDictionary(p => p.Key, p => p.Current);
        values["hit"] = hit;
        values["x"] = x;
        values["y"] = y;
        var json = ScriptHost.BuildPattern(Script, values, doc.Bpm, doc.NewId, logs, out error);
        var group = NewGroupId(doc, Id);
        var res = new List<EditObject>();
        foreach (var j in json)
        {
            var o = LevelDocument.ReadObject((JsonObject)JsonNode.Parse(j), doc.NewId);
            o.Group = group;
            res.Add(o);
        }
        return res;
    }

    public static string NewGroupId(LevelDocument doc, string prefix)
    {
        int n = 1;
        while (doc.Objects.Any(o => o.Group == prefix + n)) n++;
        return prefix + n;
    }
}
