using System;
using System.Collections.Generic;
using System.Linq;
using Sonokinesis.Core;
using Sonokinesis.Editor.Model;

namespace Sonokinesis.Editor;

/// <summary>
/// The shared state of the editor: the open document, selection, playhead, snapping, undo history
/// and the preview evaluator. UI panels listen to its events instead of talking to each other.
/// </summary>
public sealed class EditorState
{
    public static EditorState I { get; } = new();

    public LevelDocument Doc { get; private set; } = new();
    public int Version { get; private set; }
    public bool Dirty { get; private set; }

    public readonly HashSet<string> Selected = new();
    public int SelectedEvent = -1;
    public int SelectedCheckpoint = -1;

    public double Playhead;
    public bool Playing;
    /// <summary>Beat subdivision for snapping (4 = sixteenth notes in 4/4). 0 turns snapping off.</summary>
    public int SnapDivision = 4;

    public event Action DocReplaced;
    public event Action DocChanged;
    /// <summary>Scripts changed (edited, added, reloaded from disk): previews and lists should refresh.</summary>
    public event Action ScriptsReloaded;
    public event Action SelectionChanged;

    private readonly Stack<(string json, string[] sel, string label)> undo = new();
    private readonly Stack<(string json, string[] sel, string label)> redo = new();
    private const int MaxHistory = 150;

    // ----- document lifecycle -----

    public void Open(LevelDocument doc)
    {
        Doc = doc;
        Scripts.ScriptsService.I.Bind(doc.FolderPath);
        Selected.Clear();
        SelectedEvent = -1;
        SelectedCheckpoint = -1;
        undo.Clear();
        redo.Clear();
        Playhead = 0;
        Version++;
        Dirty = false;
        previewVersion = -1;
        DocReplaced?.Invoke();
        SelectionChanged?.Invoke();
    }

    public void MarkSaved() { Dirty = false; DocChanged?.Invoke(); }

    /// <summary>Rebuilds the preview (scripts changed on disk or in the code editor).</summary>
    public void ReloadScripts()
    {
        previewVersion = -1;
        ScriptsReloaded?.Invoke();
    }

    // ----- editing -----

    /// <summary>Records an undo step, applies a change and notifies listeners.</summary>
    public void Edit(string label, Action change)
    {
        Snapshot(label);
        change();
        Touch();
    }

    /// <summary>Records an undo step without changing anything yet (start of a drag).</summary>
    public void Snapshot(string label)
    {
        undo.Push((Doc.ToJson(false), Selected.ToArray(), label));
        while (undo.Count > MaxHistory) TrimOldest(undo);
        redo.Clear();
    }

    /// <summary>Notifies listeners about a change made since the last snapshot (during a drag).</summary>
    public void Touch()
    {
        Version++;
        Dirty = true;
        DocChanged?.Invoke();
    }

    public string UndoLabel => undo.Count > 0 ? undo.Peek().label : null;
    public string RedoLabel => redo.Count > 0 ? redo.Peek().label : null;

    public void Undo() => Step(undo, redo);
    public void Redo() => Step(redo, undo);

    private void Step(Stack<(string json, string[] sel, string label)> from, Stack<(string json, string[] sel, string label)> to)
    {
        if (from.Count == 0) return;
        var (json, sel, label) = from.Pop();
        to.Push((Doc.ToJson(false), Selected.ToArray(), label));
        var folder = Doc.FolderPath;
        Doc = LevelDocument.FromJson(json, folder);
        Selected.Clear();
        foreach (var id in sel) if (Doc.Find(id) != null) Selected.Add(id);
        SelectedEvent = -1;
        SelectedCheckpoint = -1;
        Version++;
        Dirty = true;
        DocReplaced?.Invoke();
        SelectionChanged?.Invoke();
    }

    private static void TrimOldest(Stack<(string, string[], string)> s)
    {
        var items = s.ToArray();
        s.Clear();
        for (int i = items.Length - 2; i >= 0; i--) s.Push(items[i]);
    }

    // ----- selection -----

    public IEnumerable<EditObject> SelectedObjects => Doc.Objects.Where(o => Selected.Contains(o.Id));

    public EditObject Primary => Selected.Count == 1 ? Doc.Find(Selected.First()) : null;

    public void Select(IEnumerable<string> ids, bool additive = false)
    {
        if (!additive) Selected.Clear();
        foreach (var id in ids) Selected.Add(id);
        SelectedEvent = -1;
        SelectedCheckpoint = -1;
        SelectionChanged?.Invoke();
    }

    public void Toggle(string id)
    {
        if (!Selected.Remove(id)) Selected.Add(id);
        SelectedEvent = -1;
        SelectedCheckpoint = -1;
        SelectionChanged?.Invoke();
    }

    public void SelectEvent(int index)
    {
        Selected.Clear();
        SelectedCheckpoint = -1;
        SelectedEvent = index;
        SelectionChanged?.Invoke();
    }

    public void SelectCheckpoint(int index)
    {
        Selected.Clear();
        SelectedEvent = -1;
        SelectedCheckpoint = index;
        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (Selected.Count == 0 && SelectedEvent < 0 && SelectedCheckpoint < 0) return;
        Selected.Clear();
        SelectedEvent = -1;
        SelectedCheckpoint = -1;
        SelectionChanged?.Invoke();
    }

    // ----- time helpers -----

    public double GridMs => SnapDivision > 0 ? Doc.BeatMs / SnapDivision : 0;

    public double Snap(double t)
    {
        if (SnapDivision <= 0) return t;
        double g = GridMs;
        return Math.Round((t - Doc.OffsetMsGrid()) / g) * g + Doc.OffsetMsGrid();
    }

    public string FormatTime(double ms)
    {
        if (double.IsNaN(ms)) return "--";
        var sign = ms < 0 ? "-" : "";
        ms = Math.Abs(ms);
        int total = (int)(ms / 1000);
        return $"{sign}{total / 60}:{total % 60:00}.{(int)(ms % 1000):000}";
    }

    public string FormatBeats(double ms)
    {
        double beats = ms / Doc.BeatMs;
        int bar = (int)Math.Floor(beats / Doc.BeatsPerBar) + 1;
        double beat = beats - (bar - 1) * Doc.BeatsPerBar + 1;
        return $"{bar}.{beat:0.##}";
    }

    // ----- preview -----

    private int previewVersion = -1;
    private LevelEvaluator evaluator;

    /// <summary>
    /// The editor has no player, so imposters chase and "@player" objects follow this stand-in. It can
    /// be dragged on the stage; it never moves on its own and nothing can hit it.
    /// </summary>
    public readonly PlayerList StandIn = new();
    public Vec2 StandInPosition
    {
        get => StandIn.Players[0].Position;
        set
        {
            var p = StandIn.Players[0];
            p.Position = value;
            StandIn.Players[0] = p;
            evaluator?.ResetImposters();   // so imposters react to the new spot from their start
        }
    }

    private void EnsureStandIn()
    {
        if (StandIn.Players.Count == 0)
            StandIn.Players.Add(new PlayerInfo { Number = 1, Position = new Vec2(-300, 150), Color = LevelData.DefaultPlayerColor, Alive = true });
    }
    public string PreviewError { get; private set; }

    /// <summary>The random roll the stage shows (the game picks a new one for every attempt).</summary>
    public int RandomSeed { get; private set; }

    public void Reroll()
    {
        RandomSeed = new Random().Next(1, int.MaxValue);
        if (evaluator != null) evaluator.Seed = RandomSeed;
        DocChanged?.Invoke();
    }

    /// <summary>
    /// The level as the game will see it: the document is written to JSON and read back with the
    /// loader's own parser, then evaluated with the loader's own evaluator.
    /// </summary>
    public LevelEvaluator Evaluator
    {
        get
        {
            if (previewVersion != Version || evaluator == null)
            {
                EnsureStandIn();
                try
                {
                    var data = LevelData.Parse(Doc.ToJson(false, forPreview: true));
                    foreach (var kv in Scripts.ScriptsService.I.LevelSources) data.ScriptSources[kv.Key] = kv.Value;
                    evaluator = new LevelEvaluator(data);
                    PreviewError = null;
                }
                catch (Exception e)
                {
                    PreviewError = e.Message;
                    evaluator ??= new LevelEvaluator(LevelData.Parse(new LevelDocument().ToJson(false, forPreview: true)));
                }
                previewVersion = Version;
                evaluator.Players = StandIn;
                evaluator.Seed = RandomSeed;
            }
            return evaluator;
        }
    }
}

internal static class DocExtensions
{
    /// <summary>The grid starts at song time 0 (the audio offset is already applied to song time).</summary>
    public static double OffsetMsGrid(this LevelDocument d) => 0;
}
