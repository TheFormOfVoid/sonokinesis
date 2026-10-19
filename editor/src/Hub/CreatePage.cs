using System;
using System.IO;
using System.Linq;
using Godot;
using Sonokinesis.Editor.UI;

namespace Sonokinesis.Editor.Hub;

/// <summary>Create: the editor, recent levels, the AI tool and the guides.</summary>
public partial class CreatePage : HubPage
{
    private VBoxContainer recent;
    private Label toolStatus;
    private HBoxContainer toolButtons;
    private ProgressBar toolProgress;
    private bool downloading;
    private string shownRecent;

    public CreatePage()
    {
        Body.AddChild(HubUi.PageTitle("Create"));
        Body.AddChild(HubUi.Dim("Make your own levels: place shapes on the beat of any song, watch them play right in the editor, then test them in the game with one key (F5)."));

        var open = HubUi.Primary("✎   Open the editor", () => Hub.I.OpenEditor(), UiTheme.Pink, 18);
        open.CustomMinimumSize = new Vector2(0, 56);
        var fresh = HubUi.Secondary("＋  New level from a song…", () => Hub.I.OpenEditor(newLevel: true), "Pick an .ogg, .mp3 or .wav file to build a level on");
        fresh.CustomMinimumSize = new Vector2(0, 56);
        Body.AddChild(HubUi.Row(12, open, fresh));

        Body.AddChild(HubUi.Card(out var r));
        r.AddChild(HubUi.Heading("Recent levels"));
        recent = HubUi.Column(6);
        r.AddChild(recent);

        Body.AddChild(HubUi.Card(out var ai));
        ai.AddChild(HubUi.Heading("Make levels with an AI"));
        ai.AddChild(HubUi.Dim("The Sonokinesis tool lets an AI assistant (Claude, Codex, Cursor and others that support MCP) build levels for you: it reads the song's beats, writes the level and checks it, and can drive the editor and the game. It is a separate download of about 35 MB."));
        toolStatus = HubUi.Text("", 14, UiTheme.Dim, null, true);
        ai.AddChild(toolStatus);
        toolProgress = new ProgressBar { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10), Visible = false };
        ai.AddChild(toolProgress);
        toolButtons = HubUi.Row(10);
        ai.AddChild(toolButtons);

        Body.AddChild(HubUi.Card(out var docs));
        docs.AddChild(HubUi.Heading("Guides"));
        var links = new HFlowContainer();
        links.AddThemeConstantOverride("h_separation", 10);
        links.AddThemeConstantOverride("v_separation", 10);
        foreach (var (label, file) in new[] { ("Editor guide", "editor-guide.md"), ("Scripting: attacks and effects in code", "scripting.md"), ("Level format", "level-format.md"), ("Guide for AI assistants", "ai-guide.md") })
            links.AddChild(HubUi.Secondary(label, () => OpenDoc(file)));
        docs.AddChild(links);
    }

    public override void Refresh()
    {
        var items = EditorSettings.Recent.Where(f => File.Exists(Path.Combine(f, "level.json"))).Take(6).ToList();
        var key = string.Join("|", items);
        if (key != shownRecent)
        {
            shownRecent = key;
            foreach (var c in recent.GetChildren()) c.QueueFree();
            if (items.Count == 0) recent.AddChild(HubUi.Dim("Levels you open or make in the editor show up here.", 14));
            foreach (var folder in items)
            {
                var info = LevelLibrary.ReadFolder(folder);
                var text = HubUi.Column(0);
                text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                text.AddChild(HubUi.Text(info?.Title ?? Path.GetFileName(folder), 16, UiTheme.Text, HubUi.Semi));
                var path = HubUi.Text(folder, 12, UiTheme.Dim);
                path.ClipText = true;
                text.AddChild(path);
                var f = folder;
                recent.AddChild(HubUi.Row(12, new LevelSwatch(UiTheme.ParseHex(info?.Background, UiTheme.Ink), UiTheme.ParseHex(info?.Hazard, UiTheme.Pink)) { CustomMinimumSize = new Vector2(44, 44) }, text, HubUi.Secondary("Open", () => Hub.I.OpenEditor(f))));
            }
        }
        if (!downloading) RefreshTool();
    }

    private void RefreshTool()
    {
        foreach (var c in toolButtons.GetChildren()) c.QueueFree();
        var exe = Updater.ToolExe;
        if (exe == null)
        {
            toolStatus.Text = "Not downloaded.";
            toolButtons.AddChild(HubUi.Primary("Download the AI tool", DownloadTool, UiTheme.Cyan, 14));
        }
        else if (Updater.IsNewer(Core.BuildInfo.Version, Updater.ToolVersion(exe)) && exe.StartsWith(Updater.AppDir, StringComparison.OrdinalIgnoreCase))
        {
            toolStatus.Text = $"An older version ({Updater.ToolVersion(exe)}) is downloaded: update it to match this app.";
            toolButtons.AddChild(HubUi.Primary("Update the AI tool", DownloadTool, UiTheme.Cyan, 14));
        }
        else
        {
            toolStatus.Text = "Ready: " + exe;
            toolButtons.AddChild(HubUi.Secondary("Copy MCP settings", () =>
            {
                DisplayServer.ClipboardSet(Updater.McpConfig(exe));
                Hub.I.Toast("Copied. Paste it into your AI app's MCP settings (for Claude Code: a .mcp.json file in your project folder).");
            }, "The settings an AI app needs to use the tool"));
            toolButtons.AddChild(HubUi.Secondary("Open its folder", () => OS.ShellShowInFileManager(exe)));
        }
        toolButtons.AddChild(HubUi.Link("Setup for AI assistants ↗", () => OS.ShellOpen("https://github.com/TheFormOfVoid/sonokinesis-ai")));
    }

    private void DownloadTool()
    {
        downloading = true;
        foreach (var c in toolButtons.GetChildren()) c.QueueFree();
        toolStatus.Text = "Downloading…";
        toolProgress.Value = 0;
        toolProgress.Visible = true;
        Hub.I.RunAsync(() => Updater.DownloadTool(p => Hub.I.Post(() => toolProgress.Value = p)), () =>
        {
            downloading = false;
            toolProgress.Visible = false;
            Hub.I.Toast("The AI tool is ready.");
            RefreshTool();
        }, e =>
        {
            downloading = false;
            toolProgress.Visible = false;
            RefreshTool();
            toolStatus.Text = "The download failed: " + e.Message + ".";
        });
    }

    private static void OpenDoc(string file)
    {
        foreach (var dir in DocDirs())
        {
            var p = Path.Combine(dir, file);
            if (File.Exists(p)) { OS.ShellOpen(p); return; }
        }
        OS.ShellOpen($"https://github.com/{Core.BuildInfo.Repo}/tree/main/docs");
    }

    private static string[] DocDirs()
    {
        var res = new System.Collections.Generic.List<string> { Path.Combine(Updater.AppDir, "docs") };
        try
        {
            var src = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
            res.Add(Path.Combine(src, "docs"));
            res.Add(Path.Combine(src, "spec"));
        }
        catch { }
        return res.ToArray();
    }
}
