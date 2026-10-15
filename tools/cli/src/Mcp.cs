using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sonokinesis.Cli;

/// <summary>
/// A Model Context Protocol server over stdio (JSON-RPC 2.0, one message per line), so AI clients can
/// use every command as a tool. Pictures (renders, screenshots) come back as images the model can see,
/// and the documentation is also offered as resources.
/// </summary>
public static class Mcp
{
    private const string Protocol = "2025-06-18";

    public static int Serve()
    {
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        // Anything else that writes to the console must not corrupt the protocol.
        Console.SetOut(TextWriter.Null);
        string line;
        while ((line = stdin.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject msg;
            try { msg = JsonNode.Parse(line) as JsonObject; }
            catch { Send(stdout, Error(null, -32700, "parse error")); continue; }
            if (msg == null) continue;
            var id = msg["id"]?.DeepClone();
            string method = (string)msg["method"];
            if (method == null) continue;              // a response to something we never asked
            if (id == null) continue;                  // notifications need no answer
            JsonObject reply;
            try { reply = Handle(id, method, msg["params"] as JsonObject ?? new JsonObject()); }
            catch (Exception e) { reply = Error(id, -32603, e.Message); }
            Send(stdout, reply);
        }
        return 0;
    }

    private static void Send(StreamWriter w, JsonObject o)
    {
        lock (w) w.WriteLine(o.ToJsonString());
    }

    private static JsonObject Result(JsonNode id, JsonNode result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
    private static JsonObject Error(JsonNode id, int code, string message) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static JsonObject Handle(JsonNode id, string method, JsonObject p)
    {
        switch (method)
        {
            case "initialize":
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = (string)p["protocolVersion"] ?? Protocol,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject(), ["resources"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "sonokinesis", ["version"] = typeof(Mcp).Assembly.GetName().Version?.ToString(3) ?? "0" },
                    ["instructions"] = "Sonokinesis makes custom levels for a rhythm bullet-hell game. Read the docs tool (topic 'ai-guide') first. " +
                        "Typical loop: new_level or level_info → analyze_song → write objects/scripts (edit_level, write_script, place_script, add_pattern) → validate_level → render_sheet/render_frames to look → play_in_game.",
                });
            case "ping":
                return Result(id, new JsonObject());
            case "tools/list":
                return Result(id, new JsonObject
                {
                    ["tools"] = new JsonArray(Commands.All.Select(c => (JsonNode)new JsonObject
                    {
                        ["name"] = c.Name,
                        ["description"] = c.Summary,
                        ["inputSchema"] = c.Schema.DeepClone(),
                    }).ToArray()),
                });
            case "tools/call":
            {
                string name = (string)p["name"];
                var cmd = Commands.All.FirstOrDefault(c => c.Name == name);
                if (cmd == null) return Error(id, -32602, $"unknown tool '{name}'");
                var args = p["arguments"] as JsonObject ?? new JsonObject();
                var content = new JsonArray();
                bool isError = false;
                try
                {
                    var r = cmd.Run((JsonObject)args.DeepClone());
                    if (r.Text != null) content.Add(new JsonObject { ["type"] = "text", ["text"] = r.Text });
                    if (r.Data != null) content.Add(new JsonObject { ["type"] = "text", ["text"] = r.Data.ToJsonString(LevelFolder.Pretty) });
                    foreach (var (_, png) in r.Images)
                        content.Add(new JsonObject { ["type"] = "image", ["mimeType"] = "image/png", ["data"] = Convert.ToBase64String(png) });
                }
                catch (CliError e) { isError = true; content.Add(new JsonObject { ["type"] = "text", ["text"] = "Error: " + e.Message }); }
                catch (Exception e) { isError = true; content.Add(new JsonObject { ["type"] = "text", ["text"] = $"Error: {e.GetType().Name}: {e.Message}" }); }
                return Result(id, new JsonObject { ["content"] = content, ["isError"] = isError });
            }
            case "resources/list":
                return Result(id, new JsonObject
                {
                    ["resources"] = new JsonArray(Docs.Topics().Select(kv => (JsonNode)new JsonObject
                    {
                        ["uri"] = "sonokinesis://docs/" + kv.Key,
                        ["name"] = kv.Key,
                        ["mimeType"] = "text/markdown",
                    }).ToArray()),
                });
            case "resources/read":
            {
                string uri = (string)p["uri"] ?? "";
                const string prefix = "sonokinesis://docs/";
                if (!uri.StartsWith(prefix)) return Error(id, -32602, "unknown resource");
                var topics = Docs.Topics();
                if (!topics.TryGetValue(uri[prefix.Length..], out var file)) return Error(id, -32602, "unknown resource");
                return Result(id, new JsonObject
                {
                    ["contents"] = new JsonArray(new JsonObject { ["uri"] = uri, ["mimeType"] = "text/markdown", ["text"] = File.ReadAllText(file) }),
                });
            }
            case "resources/templates/list":
                return Result(id, new JsonObject { ["resourceTemplates"] = new JsonArray() });
            case "prompts/list":
                return Result(id, new JsonObject { ["prompts"] = new JsonArray() });
            default:
                return Error(id, -32601, $"method not found: {method}");
        }
    }
}
