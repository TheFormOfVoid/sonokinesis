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
/// key NAME · palette ITEM X Y · save · undo · quit · code NAME · codeshot FILE · codeset FILE · codesave ·
/// newscript NAME KIND · param KEY VALUE · place NAME · report
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
                case "palette":
                    var p = arg.Split(' ');
                    app.Palette.DropAt(p[0], double.Parse(p[1]), double.Parse(p[2]));
                    wait = 2;
                    break;
                case "settings": app.ShowLevelSettings(); wait = 5; break;
                case "repeat": UI.ToolDialogs.ShowRepeat(app); wait = 5; break;
                case "stretch": UI.ToolDialogs.ShowStretch(app); wait = 5; break;
                case "newscriptdialog": UI.ScriptEditorWindow.ShowNew(app); wait = 5; break;
                case "closedialogs":
                    foreach (var w in app.FindChildren("*", "AcceptDialog", true, false)) ((Window)w).Hide();
                    wait = 3;
                    break;
                case "new":
                {
                    // new <song file> | <level folder>
                    var p2 = arg.Split('|');
                    app.CreateLevelAt(p2[0].Trim(), p2[1].Trim());
                    wait = 20;
                    break;
                }
                case "bpm": s.Edit("BPM", () => s.Doc.Bpm = double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)); break;
                case "testingame": app.TestInGameNow(); wait = 5; break;
                case "place": app.Palette.Place(arg); wait = 10; break;
                case "code": UI.ScriptEditorWindow.Open(arg.Length > 0 ? arg : null); wait = 15; break;
                case "codeshot":
                {
                    var w = UI.ScriptEditorWindow.Current ?? throw new Exception("the script editor is not open");
                    var cimg = w.GetTexture().GetImage();
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arg)));
                    cimg.SavePng(arg);
                    break;
                }
                case "codeset": (UI.ScriptEditorWindow.Current ?? throw new Exception("the script editor is not open")).SetText(File.ReadAllText(arg)); wait = 45; break;
                case "codesave": UI.ScriptEditorWindow.Current?.SaveNow(); wait = 10; break;
                case "newscript":
                {
                    var a = arg.Split(' ');
                    if (Scripts.ScriptsService.I.Create(a[0], a.Length > 1 ? a[1] : "attack", out var err) == null) throw new Exception(err);
                    UI.ScriptEditorWindow.Open(a[0]);
                    wait = 15;
                    break;
                }
                case "param":
                {
                    // param KEY VALUE: sets a setting of the selected scripted object
                    var a = arg.Split(' ', 2);
                    var o = s.Primary?.Script ?? throw new Exception("select a scripted object first");
                    object v = double.TryParse(a[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d
                        : a[1] == "true" ? true : a[1] == "false" ? false : a[1];
                    s.Edit("Set " + a[0], () => o.Params[a[0]] = v);
                    wait = 3;
                    break;
                }
                case "report":
                {
                    foreach (var e in Scripts.ScriptsService.I.LevelCompileErrors) GD.Print("autotest: compile error " + e);
                    var host = s.Evaluator?.Scripts;
                    if (host != null)
                    {
                        foreach (var e in host.Errors) GD.Print("autotest: script error " + e);
                        foreach (var l in host.Logs.TakeLast(20)) GD.Print("autotest: log " + l);
                    }
                    if (s.Primary?.Script is { } sc)
                        GD.Print($"autotest: selected script {sc.Name} params {{{string.Join(", ", sc.Params.Select(kv => kv.Key + "=" + kv.Value))}}}");
                    GD.Print($"autotest: objects {s.Doc.Objects.Count}, scripts in level: {string.Join(", ", Scripts.ScriptsService.I.LevelSources.Keys)}, selected {string.Join(",", s.Selected)}, status '{app.LastStatus}'");
                    break;
                }
                case "save": app.Save(); break;
                case "export": app.ExportZipTo(arg); break;
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
