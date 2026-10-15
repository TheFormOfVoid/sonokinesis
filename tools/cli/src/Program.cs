using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sonokinesis.Cli;

/// <summary>
/// sonokinesis: the Sonokinesis command-line tool. Every command is also an MCP tool (sonokinesis mcp).
/// Arguments are --name value; a few may be given in order without names. Values that look like
/// JSON (numbers, arrays, objects, true/false) are read as JSON; @file reads a file.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") { Help(args.Length > 1 ? args[1] : null); return 0; }
        if (args[0] is "--version" or "version") { Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString(3)); return 0; }
        if (args[0] == "mcp") return Mcp.Serve();

        string name = args[0].Replace('-', '_');
        var cmd = Commands.All.FirstOrDefault(c => c.Name == name)
            ?? Commands.All.FirstOrDefault(c => c.Name.Split('_').Any(p => p == name) && Commands.All.Count(x => x.Name.Split('_').Any(q => q == name)) == 1);
        if (cmd == null) { Console.Error.WriteLine($"unknown command '{args[0]}'. Run `sonokinesis help` for the list."); return 2; }
        if (args.Skip(1).Any(a => a is "-h" or "--help")) { Help(cmd.Name); return 0; }
        try
        {
            var a = Parse(cmd, args.Skip(1).ToArray());
            var r = cmd.Run(a);
            if (r.Text != null) Console.WriteLine(r.Text);
            if (r.Data != null) Console.WriteLine(r.Data.ToJsonString(LevelFolder.Pretty));
            if (r.Data is JsonObject o && o["ok"] is JsonValue ok && ok.TryGetValue<bool>(out var b) && !b) return 1;
            return 0;
        }
        catch (CliError e) { Console.Error.WriteLine("error: " + e.Message); return 1; }
        catch (Exception e) { Console.Error.WriteLine($"error: {e.GetType().Name}: {e.Message}"); return 1; }
    }

    private static JsonObject Parse(Command cmd, string[] args)
    {
        var res = new JsonObject();
        int pos = 0;
        for (int i = 0; i < args.Length; i++)
        {
            string key, val;
            if (args[i].StartsWith("--"))
            {
                key = args[i][2..];
                int eq = key.IndexOf('=');
                if (eq >= 0) { val = key[(eq + 1)..]; key = key[..eq]; }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) val = args[++i];
                else val = "true";
            }
            else
            {
                if (pos >= cmd.Positional.Length) throw new CliError($"unexpected argument '{args[i]}'");
                key = cmd.Positional[pos++];
                val = args[i];
            }
            res[key] = Value(cmd, key, val);
        }
        return res;
    }

    private static JsonNode Value(Command cmd, string key, string val)
    {
        string type = (string)cmd.Schema["properties"]?[key]?["type"];
        if (type == null) throw new CliError($"'{cmd.Name}' has no option --{key}. Run `sonokinesis help {cmd.Name}`.");
        if (val.StartsWith("@") && type is "array" or "object" && System.IO.File.Exists(val[1..])) val = System.IO.File.ReadAllText(val[1..]);
        if (type == "string")
        {
            if (val.StartsWith("@") && System.IO.File.Exists(val[1..]) && key is "code" or "json") return System.IO.File.ReadAllText(val[1..]);
            return val;
        }
        if (type == "array" && key == "times" && !val.TrimStart().StartsWith("[")) return val;
        try { return JsonNode.Parse(val); }
        catch (JsonException) { throw new CliError($"--{key} must be {(type == "number" ? "a number" : "JSON")}"); }
    }

    private static void Help(string command)
    {
        if (command != null)
        {
            var c = Commands.All.FirstOrDefault(x => x.Name == command.Replace('-', '_'));
            if (c == null) { Console.Error.WriteLine($"unknown command '{command}'"); return; }
            Console.WriteLine($"sonokinesis {c.Name.Replace('_', '-')} {string.Join(" ", c.Positional.Select(p => $"[{p}]"))}\n\n{c.Summary}\n");
            var req = (c.Schema["required"] as JsonArray)?.Select(r => (string)r).ToHashSet() ?? new();
            foreach (var kv in (JsonObject)c.Schema["properties"])
                Console.WriteLine($"  --{kv.Key,-10} {(string)kv.Value["type"],-8} {(req.Contains(kv.Key) ? "(required) " : "")}{(string)kv.Value["description"]}");
            return;
        }
        Console.WriteLine("sonokinesis: make and check Sonokinesis levels from the command line (and for AI, as an MCP server).\n");
        Console.WriteLine("usage: sonokinesis <command> [arguments]     sonokinesis help <command>     sonokinesis mcp\n");
        foreach (var c in Commands.All) Console.WriteLine($"  {c.Name.Replace('_', '-'),-18} {c.Summary}");
        Console.WriteLine("\n  mcp                Run as an MCP server on stdin/stdout (for AI clients).");
        Console.WriteLine("\nExamples:\n  sonokinesis new-level \"My Level\" --song song.ogg --title \"My Song\"\n  sonokinesis analyze-song \"My Level\"\n" +
            "  sonokinesis place-script \"My Level\" bullet_ring --at 4000 --params '{\"count\":16}'\n  sonokinesis validate-level \"My Level\"\n" +
            "  sonokinesis render-sheet \"My Level\" --from 0 --to 16000 --count 12\n  sonokinesis play-in-game \"My Level\" --from 4000");
    }
}
