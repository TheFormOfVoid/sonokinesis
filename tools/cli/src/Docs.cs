using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Sonokinesis.Cli;

/// <summary>The documentation as topics: docs/*.md plus the level format specification.</summary>
public static class Docs
{
    public static Dictionary<string, string> Topics()
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Paths.Docs != null)
            foreach (var f in Directory.GetFiles(Paths.Docs, "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                res[Path.GetFileNameWithoutExtension(f)] = f;
        if (Paths.Spec != null && !res.ContainsKey("level-format")) res["level-format"] = Paths.Spec;
        return res;
    }

    private static string Title(string file)
    {
        foreach (var line in File.ReadLines(file))
            if (line.StartsWith("# ")) return line[2..].Trim();
        return Path.GetFileNameWithoutExtension(file);
    }

    public static CommandResult Read(string topic)
    {
        var topics = Topics();
        if (topics.Count == 0) throw new CliError("the documentation was not found (set SONOKINESIS_DOCS to the docs folder)");
        if (string.IsNullOrEmpty(topic))
        {
            var sb = new StringBuilder("Sonokinesis documentation. Read a topic with docs <topic>:\n\n");
            foreach (var kv in topics) sb.Append($"- {kv.Key}: {Title(kv.Value)}\n");
            sb.Append("\nStart with 'ai-guide' (how to build a level step by step), then 'level-format' and 'scripting'.\n");
            return new CommandResult { Text = sb.ToString() };
        }
        if (!topics.TryGetValue(topic, out var file))
        {
            var match = topics.Keys.FirstOrDefault(k => k.Contains(topic, StringComparison.OrdinalIgnoreCase));
            if (match == null) throw new CliError($"no topic '{topic}' (topics: {string.Join(", ", topics.Keys)})");
            file = topics[match];
        }
        return new CommandResult { Text = File.ReadAllText(file) };
    }
}
