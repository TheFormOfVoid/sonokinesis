using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace Beatform.Editor;

/// <summary>
/// Runs a script of editor actions, one per line, and saves screenshots. Used to check the editor
/// without clicking: `godot --path editor -- --autotest script.txt`.
/// Commands: open DIR · at MS · select IDS|all|none|group:G · shot FILE · wait FRAMES · play · stop ·
/// playtest · key NAME · palette ITEM X Y · save · undo · quit
/// </summary>
public sealed class AutoTest
{
    private readonly Queue<string> lines;
    private int wait;

    public AutoTest(string path)
    {
        lines = new Queue<string>(File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")));
    }

    public void Step(App app)
    {
        if (wait > 0) { wait--; return; }
        if (lines.Count == 0) return;
        var line = lines.Dequeue();
        var parts = line.Split(' ', 2);
        var arg = parts.Length > 1 ? parts[1] : "";
        var s = EditorState.I;
        GD.Print("autotest: " + line);
        try
        {
            switch (parts[0])
            {
                case "open": app.OpenFolder(arg); wait = 10; break;
                case "at": s.Playhead = double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture); app.PlayheadMoved(); wait = 2; break;
                case "select":
                    if (arg == "none") s.ClearSelection();
                    else if (arg == "all") s.Select(s.Doc.Objects.Select(o => o.Id));
                    else if (arg.StartsWith("group:")) s.Select(s.Doc.Objects.Where(o => o.Group == arg[6..]).Select(o => o.Id));
                    else s.Select(arg.Split(','));
                    wait = 2;
                    break;
                case "event": s.SelectEvent(int.Parse(arg)); wait = 2; break;
                case "shot":
                    var img = app.GetViewport().GetTexture().GetImage();
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arg)));
                    img.SavePng(arg);
                    break;
                case "wait": wait = int.Parse(arg); break;
                case "play": app.StartPlayback(s.Playhead); break;
                case "stop": app.StopPlayback(); break;
                case "playtest": app.TogglePlaytest(); break;
                case "palette":
                    var p = arg.Split(' ');
                    app.Palette.DropAt(p[0], double.Parse(p[1]), double.Parse(p[2]));
                    wait = 2;
                    break;
                case "settings": app.ShowLevelSettings(); wait = 5; break;
                case "save": app.Save(); break;
                case "undo": s.Undo(); wait = 2; break;
                case "quit": app.GetTree().Quit(); break;
                case "size":
                    var wh = arg.Split('x');
                    app.GetWindow().Size = new Vector2I(int.Parse(wh[0]), int.Parse(wh[1]));
                    wait = 5;
                    break;
                default: GD.PrintErr("autotest: unknown command " + parts[0]); break;
            }
        }
        catch (Exception e) { GD.PrintErr($"autotest: {line} failed: {e}"); }
    }
}
