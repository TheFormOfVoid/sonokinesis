using System;
using System.Collections.Generic;
using System.Linq;
using Beatform.Editor.Audio;
using Beatform.Editor.Model;
using Godot;

namespace Beatform.Editor.UI;

/// <summary>
/// The timeline: ruler with bars and beats, the song waveform, checkpoints, scene events and every
/// object as a bar (warning, active and outro parts), packed into lanes. Drag to scrub, move, resize.
/// </summary>
public partial class TimelineView : Control
{
    private static EditorState S => EditorState.I;

    private const float RulerH = 26, WaveH = 46, CheckH = 22, EventH = 22, LaneH = 22, LabelW = 0;
    private float LanesTop => RulerH + WaveH + CheckH + EventH;

    private SongPlayer song;
    private double scrollMs = -500;
    private double pxPerMs = 0.12;
    private float laneScroll;

    private readonly Dictionary<string, int> laneOf = new();
    private int laneCount;
    private int lanesVersion = -1;

    private enum DragMode { None, Scrub, Move, ResizeStart, ResizeEnd, Marquee, Checkpoint, Event }
    private DragMode drag;
    private Vector2 dragStartPos;
    private double dragStartMs;
    private bool dragMoved;
    private string dragObject;
    private readonly Dictionary<string, (double start, double end)> dragOrig = new();
    private int dragIndex;
    private double dragOrigTime;

    public void Init(SongPlayer songPlayer)
    {
        song = songPlayer;
        ClipContents = true;
        FocusMode = FocusModeEnum.Click;
        MouseFilter = MouseFilterEnum.Stop;
        S.DocChanged += QueueRedraw;
        S.SelectionChanged += QueueRedraw;
        S.DocReplaced += () => { scrollMs = -500; laneScroll = 0; QueueRedraw(); };
    }

    private Waveform drawnWaveform;

    public override void _Process(double delta)
    {
        // The song is decoded in the background; redraw once its waveform is ready.
        if (song?.Waveform != drawnWaveform) { drawnWaveform = song?.Waveform; QueueRedraw(); }
        if (S.Playing)
        {
            // Keep the playhead in view while playing.
            double visible = Size.X / pxPerMs;
            if (S.Playhead > scrollMs + visible * 0.85 || S.Playhead < scrollMs) scrollMs = S.Playhead - visible * 0.15;
            QueueRedraw();
        }
    }

    private float X(double ms) => (float)((ms - scrollMs) * pxPerMs) + LabelW;
    private double Ms(float x) => (x - LabelW) / pxPerMs + scrollMs;

    private void PackLanes()
    {
        if (lanesVersion == S.Version) return;
        lanesVersion = S.Version;
        laneOf.Clear();
        var laneEnds = new List<double>();
        foreach (var o in S.Doc.Objects.OrderBy(o => o.Start).ThenBy(o => o.Layer))
        {
            int lane = laneEnds.FindIndex(end => end <= o.Start - 1);
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(0); }
            laneEnds[lane] = o.End;
            laneOf[o.Id] = lane;
        }
        laneCount = laneEnds.Count;
    }

    private Rect2 BarRect(EditObject o)
    {
        int lane = laneOf.TryGetValue(o.Id, out var l) ? l : 0;
        float y = LanesTop + 4 + lane * LaneH - laneScroll;
        return new Rect2(X(o.Start), y, Math.Max(3, (float)((o.End - o.Start) * pxPerMs)), LaneH - 4);
    }

    // ---------------- drawing ----------------

    public override void _Draw()
    {
        PackLanes();
        var font = GetThemeDefaultFont();
        DrawRect(new Rect2(Vector2.Zero, Size), UiTheme.Ink);

        DrawGrid(font);
        DrawWaveform();
        DrawCheckpoints(font);
        DrawEvents();

        // Lanes area.
        DrawRect(new Rect2(0, LanesTop, Size.X, 1), UiTheme.Line);
        var hazard = UiTheme.ParseHex(S.Doc.Hazard, UiTheme.Pink);
        foreach (var o in S.Doc.Objects)
        {
            var r = BarRect(o);
            if (r.End.X < 0 || r.Position.X > Size.X || r.End.Y < LanesTop || r.Position.Y > Size.Y) continue;
            bool sel = S.Selected.Contains(o.Id);
            var c = UiTheme.ParseHex(o.Color, hazard);
            float warnW = (float)(o.Warn * pxPerMs), outroW = (float)(o.Outro * pxPerMs);
            DrawRect(r, new Color(c, sel ? 0.95f : 0.7f));
            if (warnW > 0) DrawRect(new Rect2(r.Position, new Vector2(Math.Min(warnW, r.Size.X), r.Size.Y)), new Color(UiTheme.Ink, 0.55f));
            if (outroW > 0) DrawRect(new Rect2(r.End.X - Math.Min(outroW, r.Size.X), r.Position.Y, Math.Min(outroW, r.Size.X), r.Size.Y), new Color(UiTheme.Ink, 0.35f));
            if (!o.Hitbox) DrawRect(r, new Color(1, 1, 1, 0.25f), false, 1);
            if (sel) DrawRect(r.Grow(1), Colors.White, false, 1.5f);
            if (r.Size.X > 40)
            {
                string label = string.IsNullOrEmpty(o.Name) ? o.Shape.Type : o.Name;
                DrawString(font, new Vector2(r.Position.X + 4 + Math.Min(warnW, r.Size.X / 2), r.Position.Y + 13), label, HorizontalAlignment.Left, r.Size.X - 8, 11, new Color(0, 0, 0, 0.8f));
            }
            if (sel)
            {
                var times = o.Tracks.Values.SelectMany(k => k).Select(k => k.T).Distinct();
                foreach (var kt in times)
                {
                    float kx = X(o.Start + kt);
                    if (kx < r.Position.X - 1 || kx > r.End.X + 1) continue;
                    var cy = r.Position.Y + r.Size.Y / 2;
                    DrawColoredPolygon(new[] { new Vector2(kx, cy - 5), new Vector2(kx + 4, cy), new Vector2(kx, cy + 5), new Vector2(kx - 4, cy) }, Colors.White);
                }
            }
        }

        if (drag == DragMode.Marquee) DrawRect(MarqueeRect(), new Color(UiTheme.Cyan, 0.15f));

        // Playhead.
        float px = X(S.Playhead);
        DrawLine(new Vector2(px, 0), new Vector2(px, Size.Y), UiTheme.Pink, 2);
        DrawColoredPolygon(new[] { new Vector2(px - 6, 0), new Vector2(px + 6, 0), new Vector2(px, 9) }, UiTheme.Pink);

        if (S.Doc.LengthMs is double len)
        {
            float ex = X(len);
            DrawLine(new Vector2(ex, RulerH), new Vector2(ex, Size.Y), new Color(UiTheme.Gold, 0.7f), 1);
            DrawString(font, new Vector2(ex + 4, LanesTop - 4), "end", HorizontalAlignment.Left, -1, 11, UiTheme.Gold);
        }
    }

    private void DrawGrid(Font font)
    {
        var d = S.Doc;
        double beat = d.BeatMs;
        double bar = beat * d.BeatsPerBar;
        double first = Math.Floor(Ms(0) / beat) * beat;
        bool showSub = S.SnapDivision > 1 && beat * pxPerMs / S.SnapDivision > 8;
        DrawRect(new Rect2(0, 0, Size.X, RulerH), UiTheme.Panel);
        for (double t = first; t < Ms(Size.X); t += beat)
        {
            float x = X(t);
            bool isBar = Math.Abs(Math.IEEERemainder(t, bar)) < 0.5;
            DrawLine(new Vector2(x, isBar ? 0 : RulerH * 0.55f), new Vector2(x, Size.Y), isBar ? new Color(1, 1, 1, 0.13f) : new Color(1, 1, 1, 0.05f), 1);
            if (isBar && beat * d.BeatsPerBar * pxPerMs > 28)
                DrawString(font, new Vector2(x + 3, 12), (Math.Round(t / bar) + 1).ToString("0"), HorizontalAlignment.Left, -1, 11, UiTheme.Dim);
            if (showSub)
                for (int k = 1; k < S.SnapDivision; k++)
                {
                    float sx = X(t + beat * k / S.SnapDivision);
                    DrawLine(new Vector2(sx, LanesTop), new Vector2(sx, Size.Y), new Color(1, 1, 1, 0.025f), 1);
                }
        }
        // Seconds along the bottom of the ruler.
        double secStep = pxPerMs * 1000 > 60 ? 1000 : pxPerMs * 5000 > 60 ? 5000 : 10000;
        for (double t = Math.Floor(Ms(0) / secStep) * secStep; t < Ms(Size.X); t += secStep)
            DrawString(font, new Vector2(X(t) + 3, RulerH - 3), S.FormatTime(t)[..^4], HorizontalAlignment.Left, -1, 10, new Color(UiTheme.Dim, 0.7f));
    }

    private void DrawWaveform()
    {
        float top = RulerH, mid = top + WaveH / 2;
        DrawRect(new Rect2(0, top, Size.X, WaveH), new Color("0e1016"));
        var w = song?.Waveform;
        if (w == null)
        {
            DrawString(GetThemeDefaultFont(), new Vector2(8, mid + 4), song?.HasSong == true ? "reading song…" : "no song loaded", HorizontalAlignment.Left, -1, 11, UiTheme.Dim);
            return;
        }
        double offset = S.Doc.OffsetMs;
        var color = new Color(UiTheme.Cyan, 0.55f);
        for (int x = 0; x < (int)Size.X; x++)
        {
            double a = Ms(x) + offset, b = Ms(x + 1) + offset;
            if (b < 0 || a > w.LengthMs) continue;
            var (lo, hi) = w.Range(Math.Max(0, a), b);
            DrawLine(new Vector2(x, mid - hi * WaveH * 0.48f), new Vector2(x, mid - lo * WaveH * 0.48f + 1), color, 1);
        }
    }

    private void DrawCheckpoints(Font font)
    {
        float top = RulerH + WaveH;
        DrawRect(new Rect2(0, top, Size.X, CheckH), UiTheme.Panel);
        DrawString(font, new Vector2(4, top + 15), "checkpoints", HorizontalAlignment.Left, -1, 10, new Color(UiTheme.Dim, 0.6f));
        for (int i = 0; i < S.Doc.Checkpoints.Count; i++)
        {
            float x = X(S.Doc.Checkpoints[i]);
            var c = i == S.SelectedCheckpoint ? Colors.White : UiTheme.Good;
            DrawLine(new Vector2(x, top + 2), new Vector2(x, top + CheckH - 2), c, 2);
            DrawColoredPolygon(new[] { new Vector2(x, top + 3), new Vector2(x + 11, top + 7), new Vector2(x, top + 11) }, c);
        }
    }

    private static Color EventColor(string type) => type switch { "shake" => UiTheme.Gold, "flash" => Colors.White, _ => UiTheme.Cyan };

    private void DrawEvents()
    {
        float top = RulerH + WaveH + CheckH;
        DrawRect(new Rect2(0, top, Size.X, EventH), UiTheme.Panel2);
        DrawString(GetThemeDefaultFont(), new Vector2(4, top + 15), "events", HorizontalAlignment.Left, -1, 10, new Color(UiTheme.Dim, 0.6f));
        for (int i = 0; i < S.Doc.Events.Count; i++)
        {
            var e = S.Doc.Events[i];
            float x = X(e.T), w = Math.Max(2, (float)(e.Duration * pxPerMs));
            var c = EventColor(e.Type);
            DrawRect(new Rect2(x, top + 8, w, 6), new Color(c, 0.35f));
            float cy = top + EventH / 2;
            DrawColoredPolygon(new[] { new Vector2(x, cy - 7), new Vector2(x + 6, cy), new Vector2(x, cy + 7), new Vector2(x - 6, cy) }, i == S.SelectedEvent ? UiTheme.Pink : c);
        }
    }

    // ---------------- input ----------------

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && mb.Pressed)
            {
                float dir = mb.ButtonIndex == MouseButton.WheelUp ? -1 : 1;
                if (mb.CtrlPressed)
                {
                    double at = Ms(mb.Position.X);
                    pxPerMs = Math.Clamp(pxPerMs * (dir < 0 ? 1.2 : 1 / 1.2), 0.005, 3);
                    scrollMs = at - (mb.Position.X - LabelW) / pxPerMs;
                }
                else if (mb.ShiftPressed)
                {
                    laneScroll = Math.Clamp(laneScroll + dir * LaneH * 3, 0, Math.Max(0, laneCount * LaneH - (Size.Y - LanesTop) + 20));
                }
                else scrollMs += dir * 80 / pxPerMs;
                QueueRedraw();
                AcceptEvent();
                return;
            }
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed) Press(mb); else Release();
                AcceptEvent();
            }
            if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            {
                var hit = HitBar(mb.Position);
                if (hit != null && !S.Selected.Contains(hit)) S.Select(new[] { hit });
                App.Instance.ShowObjectMenu(GetScreenTransform() * mb.Position);
                AcceptEvent();
            }
        }
        else if (e is InputEventMouseMotion mm)
        {
            if (drag != DragMode.None) Motion(mm);
            else
            {
                // Resize cursor near bar edges.
                var hit = HitBar(mm.Position);
                MouseDefaultCursorShape = CursorShape.Arrow;
                if (hit != null)
                {
                    var r = BarRect(S.Doc.Find(hit));
                    if (Math.Abs(mm.Position.X - r.Position.X) < 6 || Math.Abs(mm.Position.X - r.End.X) < 6) MouseDefaultCursorShape = CursorShape.Hsize;
                }
            }
        }
    }

    private string HitBar(Vector2 p)
    {
        if (p.Y < LanesTop) return null;
        PackLanes();
        foreach (var o in S.Doc.Objects)
        {
            var r = BarRect(o).Grow(2);
            if (r.HasPoint(p)) return o.Id;
        }
        return null;
    }

    private void Press(InputEventMouseButton mb)
    {
        GrabFocus();
        dragStartPos = mb.Position;
        dragStartMs = Ms(mb.Position.X);
        dragMoved = false;
        float y = mb.Position.Y;

        if (y < RulerH + WaveH)
        {
            drag = DragMode.Scrub;
            Seek(dragStartMs, !mb.AltPressed);
            return;
        }
        if (y < RulerH + WaveH + CheckH)
        {
            int idx = S.Doc.Checkpoints.FindIndex(c => Math.Abs(X(c) - mb.Position.X) < 7);
            if (mb.DoubleClick && idx < 0)
            {
                double t = S.Snap(dragStartMs);
                S.Edit("Add checkpoint", () => { S.Doc.Checkpoints.Add(Math.Max(0, t)); S.Doc.Checkpoints.Sort(); });
                return;
            }
            if (idx >= 0)
            {
                S.SelectCheckpoint(idx);
                if (idx > 0) { drag = DragMode.Checkpoint; dragIndex = idx; dragOrigTime = S.Doc.Checkpoints[idx]; }
            }
            return;
        }
        if (y < LanesTop)
        {
            int idx = S.Doc.Events.FindIndex(ev => Math.Abs(X(ev.T) - mb.Position.X) < 7);
            if (idx >= 0)
            {
                S.SelectEvent(idx);
                drag = DragMode.Event;
                dragIndex = idx;
                dragOrigTime = S.Doc.Events[idx].T;
            }
            return;
        }

        var hit = HitBar(mb.Position);
        if (hit == null)
        {
            if (!mb.ShiftPressed) S.ClearSelection();
            drag = DragMode.Marquee;
            return;
        }
        if (mb.ShiftPressed) { S.Toggle(hit); return; }
        if (!S.Selected.Contains(hit)) S.Select(new[] { hit });
        dragObject = hit;
        var obj = S.Doc.Find(hit);
        var r = BarRect(obj);
        if (Math.Abs(mb.Position.X - r.Position.X) < 6) drag = DragMode.ResizeStart;
        else if (Math.Abs(mb.Position.X - r.End.X) < 6) drag = DragMode.ResizeEnd;
        else drag = DragMode.Move;
        dragOrig.Clear();
        foreach (var o in S.SelectedObjects) dragOrig[o.Id] = (o.Start, o.End);
    }

    private void Motion(InputEventMouseMotion mm)
    {
        double now = Ms(mm.Position.X);
        if (!dragMoved && mm.Position.DistanceTo(dragStartPos) < 3) return;
        bool first = !dragMoved;
        dragMoved = true;
        switch (drag)
        {
            case DragMode.Scrub:
                Seek(now, !Input.IsKeyPressed(Key.Alt));
                return;
            case DragMode.Marquee:
                QueueRedraw();
                return;
            case DragMode.Checkpoint:
                if (first) S.Snapshot("Move checkpoint");
                S.Doc.Checkpoints[dragIndex] = Math.Max(1, S.Snap(dragOrigTime + (now - dragStartMs)));
                S.Touch();
                return;
            case DragMode.Event:
                if (first) S.Snapshot("Move event");
                S.Doc.Events[dragIndex].T = Math.Max(0, S.Snap(dragOrigTime + (now - dragStartMs)));
                S.Touch();
                return;
        }

        var anchor = S.Doc.Find(dragObject);
        if (anchor == null || !dragOrig.TryGetValue(anchor.Id, out var a)) return;
        if (first)
        {
            if (drag == DragMode.Move && Input.IsKeyPressed(Key.Ctrl)) DuplicateSelectionForDrag();
            S.Snapshot(drag == DragMode.Move ? "Move in time" : "Change length");
        }
        double delta = now - dragStartMs;
        switch (drag)
        {
            case DragMode.Move:
            {
                double snappedStart = S.Snap(a.start + delta);
                double d = snappedStart - a.start;
                foreach (var o in S.SelectedObjects)
                {
                    if (!dragOrig.TryGetValue(o.Id, out var orig)) continue;
                    o.Start = orig.start + d;
                    o.End = orig.end + d;
                }
                break;
            }
            case DragMode.ResizeStart:
            {
                double ns = Math.Min(S.Snap(a.start + delta), a.end - 10);
                anchor.Start = ns;
                break;
            }
            case DragMode.ResizeEnd:
            {
                double ne = Math.Max(S.Snap(a.end + delta), a.start + 10);
                anchor.End = ne;
                break;
            }
        }
        S.Touch();
    }

    private void DuplicateSelectionForDrag()
    {
        S.Snapshot("Duplicate");
        var map = App.Instance.DuplicateObjects(S.SelectedObjects.ToList(), 0);
        dragOrig.Clear();
        foreach (var o in S.SelectedObjects) dragOrig[o.Id] = (o.Start, o.End);
        dragObject = map.TryGetValue(dragObject, out var newId) ? newId : dragObject;
    }

    private void Release()
    {
        if (drag == DragMode.Marquee && dragMoved)
        {
            var r = MarqueeRect();
            var ids = S.Doc.Objects.Where(o => BarRect(o).Intersects(r)).Select(o => o.Id);
            S.Select(ids, Input.IsKeyPressed(Key.Shift));
        }
        else if (drag == DragMode.Marquee && !dragMoved)
        {
            Seek(dragStartMs, true);
        }
        drag = DragMode.None;
        QueueRedraw();
    }

    private Rect2 MarqueeRect()
    {
        var a = dragStartPos;
        var b = GetLocalMousePosition();
        return new Rect2(new Vector2(Math.Min(a.X, b.X), Math.Max(LanesTop, Math.Min(a.Y, b.Y))), (a - b).Abs());
    }

    private void Seek(double ms, bool snap)
    {
        S.Playhead = Math.Max(snap && !S.Playing ? 0 : -1000, snap ? S.Snap(ms) : ms);
        if (S.Playhead < 0) S.Playhead = 0;
        App.Instance.PlayheadMoved();
        QueueRedraw();
    }

    public void ZoomBy(double factor)
    {
        double center = Ms(Size.X / 2);
        pxPerMs = Math.Clamp(pxPerMs * factor, 0.005, 3);
        scrollMs = center - Size.X / 2 / pxPerMs;
        QueueRedraw();
    }

    public void ShowRange(double from, double to)
    {
        pxPerMs = Math.Clamp(Size.X * 0.9 / Math.Max(1000, to - from), 0.005, 3);
        scrollMs = from - (to - from) * 0.05;
        QueueRedraw();
    }
}
