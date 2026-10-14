using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace Beatform.Editor;

/// <summary>
/// Lets other programs (the beatform command-line tool, an AI through it) drive the running editor:
/// they write a command to %LOCALAPPDATA%/Beatform/editor-bridge/command.json and read the answer
/// from result.json. Commands: ping, state, open, reload, save, seek, play, stop, select, shot.
/// See docs/ai-tools.md.
/// </summary>
public static class EditorBridge
{
    public static string Dir => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Beatform", "editor-bridge");
    private static double nextCheck;
    private static string pendingShot;
    private static JsonObject pendingShotReply;
    private static int shotFrames;

    private static EditorState S => EditorState.I;

    public static void Poll(App app)
    {
        if (pendingShot != null && --shotFrames <= 0) FinishShot();
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < nextCheck) return;
        nextCheck = now + 0.1;
        var file = Path.Combine(Dir, "command.json");
        if (!File.Exists(file)) return;
        JsonObject cmd;
        try
        {
            cmd = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
            File.Delete(file);
        }
        catch { return; }
        if (cmd == null) return;
        var reply = new JsonObject { ["id"] = cmd["id"]?.DeepClone(), ["ok"] = true };
        try
        {
            if (Run(app, cmd, reply)) Write(reply);
        }
        catch (Exception e)
        {
            reply["ok"] = false;
            reply["error"] = e.Message;
            Write(reply);
        }
    }

    /// <summary>Runs a command; false when the answer comes later (screenshots).</summary>
    private static bool Run(App app, JsonObject cmd, JsonObject reply)
    {
        string name = (string)cmd["cmd"] ?? "";
        switch (name)
        {
            case "ping":
                reply["editor"] = "Beatform Editor";
                break;
            case "state":
                break;
            case "open":
                app.OpenFolder((string)cmd["folder"] ?? throw new Exception("open needs a folder"));
                break;
            case "reload":
                if (S.Doc.FolderPath == null) throw new Exception("no level folder to reload");
                if (S.Dirty && cmd["force"]?.GetValue<bool>() != true) throw new Exception("the level has unsaved edits (use force: true to discard them)");
                app.OpenFolder(S.Doc.FolderPath);
                break;
            case "save":
                if (!app.Save()) throw new Exception("could not save");
                break;
            case "seek":
                S.Playhead = Num(cmd, "ms");
                app.PlayheadMoved();
                break;
            case "play":
                app.StartPlayback(cmd["ms"] != null ? Num(cmd, "ms") : S.Playhead);
                break;
            case "stop":
                app.StopPlayback();
                break;
            case "select":
                S.Select((cmd["ids"] as JsonArray)?.Select(v => (string)v) ?? Enumerable.Empty<string>());
                break;
            case "shot":
            {
                var path = (string)cmd["path"] ?? Path.Combine(Dir, "shot.png");
                if (cmd["ms"] != null) { S.Playhead = Num(cmd, "ms"); app.PlayheadMoved(); }
                pendingShot = path;
                pendingShotReply = reply;
                shotFrames = 3;
                return false;
            }
            default:
                throw new Exception($"unknown command '{name}'");
        }
        AddState(reply);
        return true;
    }

    private static double Num(JsonObject cmd, string key) => cmd[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : throw new Exception($"{key} must be a number");

    private static void AddState(JsonObject reply)
    {
        reply["folder"] = S.Doc.FolderPath;
        reply["title"] = S.Doc.Title;
        reply["playheadMs"] = Math.Round(S.Playhead, 1);
        reply["playing"] = S.Playing;
        reply["dirty"] = S.Dirty;
        reply["objects"] = S.Doc.Objects.Count;
        reply["events"] = S.Doc.Events.Count;
        reply["selected"] = new JsonArray(S.Selected.Select(id => (JsonNode)id).ToArray());
        var host = S.Evaluator.Scripts;
        var errors = new JsonArray();
        foreach (var e in Scripts.ScriptsService.I.LevelCompileErrors) errors.Add(e.ToString());
        if (host != null) foreach (var e in host.Errors) errors.Add(e.ToString());
        reply["scriptErrors"] = errors;
        var logs = new JsonArray();
        if (host != null) foreach (var l in host.Logs.Skip(Math.Max(0, host.Logs.Count - 50))) logs.Add(l);
        reply["scriptLogs"] = logs;
        if (S.Evaluator != null && App.Instance != null) reply["previewError"] = S.PreviewError;
    }

    private static void FinishShot()
    {
        var path = pendingShot;
        var reply = pendingShotReply;
        pendingShot = null;
        pendingShotReply = null;
        try
        {
            var img = App.Instance.GetViewport().GetTexture().GetImage();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            img.SavePng(path);
            reply["path"] = path;
            AddState(reply);
        }
        catch (Exception e)
        {
            reply["ok"] = false;
            reply["error"] = e.Message;
        }
        Write(reply);
    }

    private static void Write(JsonObject reply)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = Path.Combine(Dir, "result.tmp");
            File.WriteAllText(tmp, reply.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, Path.Combine(Dir, "result.json"), true);
        }
        catch { }
    }
}
