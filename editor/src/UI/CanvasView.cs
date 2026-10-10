using System;
using System.Collections.Generic;
using System.Linq;
using Beatform.Core;
using Beatform.Editor.Audio;
using Beatform.Editor.Model;
using Godot;

namespace Beatform.Editor.UI;

/// <summary>
/// The stage: draws the level at the playhead exactly as the game will (same evaluator), and lets the
/// creator select and transform objects. Moving a static object moves it; moving an animated object
/// at the playhead writes a keyframe there. Also hosts the playtest.
/// </summary>
public partial class CanvasView : Control
{
    private static EditorState S => EditorState.I;
    private const float GameW = 1280, GameH = 720;

    private SongPlayer song;
    private float scale = 1;
    private Vector2 origin; // control position of game (0, 0)
    private float zoom = 1;
    private Vector2 pan;

    private readonly List<DrawItem> items = new();
    private readonly List<Rgba> flashes = new();

    private enum DragMode { None, Move, Rotate, Scale, Marquee, Pan }
    private DragMode drag;
    private Vector2 dragStartMouse;
    private Vector2 dragStartGame;
    private readonly Dictionary<string, (double x, double y, double rot, double w, double h)> dragStart = new();
    private Vector2 scaleCorner;
    private bool dragMoved;

    public Playtest Playtest { get; private set; }

    public void Init(SongPlayer songPlayer)
    {
        song = songPlayer;
        Playtest = new Playtest(songPlayer);
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        S.DocChanged += QueueRedraw;
        S.SelectionChanged += QueueRedraw;
        S.DocReplaced += QueueRedraw;
    }

    public override void _Process(double delta)
    {
        if (Playtest.Active) Playtest.Step(delta, items);
        if (S.Playing || Playtest.Active || drag != DragMode.None) QueueRedraw();
    }

    // ---------------- coordinates ----------------

    private void Layout()
    {
        const float margin = 28;
        float fit = Math.Min((Size.X - margin * 2) / GameW, (Size.Y - margin * 2) / GameH);
        scale = Math.Max(0.05f, fit * zoom);
        origin = Size / 2 + pan;
    }

    public Vector2 GameToView(double x, double y) => origin + new Vector2((float)x, (float)y) * scale;
    public Vector2 GameToView(Vec2 p) => GameToView(p.X, p.Y);
    public Vector2 ViewToGame(Vector2 v) => (v - origin) / scale;

    // ---------------- drawing ----------------

    public override void _Draw()
    {
        Layout();
        var ev = S.Evaluator;
        double t = S.Playhead;
        ev.Collect(t, items);

        DrawRect(new Rect2(Vector2.Zero, Size), new Color("07080b"));
        var screen = new Rect2(GameToView(-GameW / 2, -GameH / 2), new Vector2(GameW, GameH) * scale);
        var bg = ev.BackgroundAt(t);
        DrawRect(screen, new Color(bg.R, bg.G, bg.B));

        DrawObjects();
        ev.Flashes(t, flashes);
        foreach (var f in flashes) DrawRect(screen, new Color(f.R, f.G, f.B, f.A));

        // Dim everything outside the visible play area.
        var dim = new Color(0.03f, 0.035f, 0.05f, 0.78f);
        DrawRect(new Rect2(0, 0, Size.X, screen.Position.Y), dim);
        DrawRect(new Rect2(0, screen.End.Y, Size.X, Size.Y - screen.End.Y), dim);
        DrawRect(new Rect2(0, screen.Position.Y, screen.Position.X, screen.Size.Y), dim);
        DrawRect(new Rect2(screen.End.X, screen.Position.Y, Size.X - screen.End.X, screen.Size.Y), dim);
        DrawRect(screen, new Color(1, 1, 1, 0.12f), false, 1);

        if (!Playtest.Active)
        {
            DrawSelection(t);
            if (drag == DragMode.Marquee) DrawRect(MarqueeRect(), new Color(UiTheme.Cyan, 0.15f));
        }
        else
        {
            Playtest.Draw(this);
        }

        var font = GetThemeDefaultFont();
        string hud = $"{S.FormatTime(t)}   bar {S.FormatBeats(t)}   {items.Count} visible";
        DrawString(font, new Vector2(12, Size.Y - 12), hud, HorizontalAlignment.Left, -1, 13, UiTheme.Dim);
        if (S.PreviewError != null)
            DrawString(font, new Vector2(12, 22), "Preview error: " + S.PreviewError, HorizontalAlignment.Left, Size.X - 24, 13, UiTheme.Bad);
    }

    private readonly List<Vector2> triPoints = new();
    private readonly List<Color> triColors = new();
    private readonly List<int> triIndices = new();

    private void DrawObjects()
    {
        triPoints.Clear();
        triColors.Clear();
        triIndices.Clear();
        foreach (var it in items)
        {
            var c = new Color(it.Color.R, it.Color.G, it.Color.B, it.Color.A);
            int b = triPoints.Count;
            foreach (var u in it.Geometry.Vertices)
            {
                triPoints.Add(GameToView(it.Transform.Apply(u)));
                triColors.Add(c);
            }
            foreach (var i in it.Geometry.Indices) triIndices.Add(b + i);
        }
        if (triIndices.Count == 0) return;
        RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), triIndices.ToArray(), triPoints.ToArray(), triColors.ToArray());
    }

    private void DrawSelection(double t)
    {
        foreach (var o in S.SelectedObjects)
        {
            var lo = Live(o);
            if (lo == null) continue;
            var m = LevelEvaluator.TransformAt(lo, t);
            bool present = LevelEvaluator.PhaseAt(lo, t) != Phase.Absent;
            var geo = ShapeGeometry.For(lo.Shape);
            var pts = geo.Outline.Select(u => GameToView(m.Apply(u))).ToList();
            pts.Add(pts[0]);
            DrawPolyline(pts.ToArray(), present ? UiTheme.Cyan : new Color(UiTheme.Cyan, 0.35f), 1.5f, true);

            // Motion path over the object's lifetime.
            var path = new List<Vector2>();
            for (double pt = lo.Start; pt <= lo.End; pt += Math.Max(16, (lo.End - lo.Start) / 120))
                path.Add(GameToView(LevelEvaluator.TransformAt(lo, pt).Apply(lo.PivotX, lo.PivotY)));
            if (path.Count > 1) DrawPolyline(path.ToArray(), new Color(UiTheme.Cyan, 0.35f), 1, true);

            if (S.Selected.Count == 1)
            {
                var pivot = GameToView(m.Apply(lo.PivotX, lo.PivotY));
                DrawCircle(pivot, 4, UiTheme.Cyan);
                var rotHandle = RotateHandle(m, lo);
                DrawLine(GameToView(m.Apply(0, -0.5)), rotHandle, new Color(UiTheme.Cyan, 0.6f), 1);
                DrawCircle(rotHandle, 6, UiTheme.Gold);
                foreach (var corner in Corners)
                    DrawRect(new Rect2(GameToView(m.Apply(corner.X, corner.Y)) - new Vector2(4, 4), new Vector2(8, 8)), Colors.White);
            }
        }
    }

    private static readonly Vector2[] Corners = { new(-0.5f, -0.5f), new(0.5f, -0.5f), new(0.5f, 0.5f), new(-0.5f, 0.5f) };

    private Vector2 RotateHandle(Affine m, LevelObject lo)
    {
        var top = GameToView(m.Apply(0, -0.5));
        var center = GameToView(m.Apply(0, 0));
        var dir = (top - center);
        dir = dir.Length() < 0.001f ? Vector2.Up : dir.Normalized();
        return top + dir * 28;
    }

    private LevelObject Live(EditObject o) => S.Evaluator.Level.Objects.FirstOrDefault(x => x.Id == o.Id);

    // ---------------- input ----------------

    public override void _GuiInput(InputEvent e)
    {
        if (Playtest.Active) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)
            {
                if (mb.Pressed)
                {
                    if (mb.CtrlPressed)
                    {
                        var before = ViewToGame(mb.Position);
                        zoom = Mathf.Clamp(zoom * (mb.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), 0.25f, 6f);
                        Layout();
                        pan += mb.Position - GameToView(before.X, before.Y);
                    }
                    else
                    {
                        double step = S.GridMs > 0 ? S.GridMs : S.Doc.BeatMs / 4;
                        S.Playhead = Math.Max(0, S.Playhead + (mb.ButtonIndex == MouseButton.WheelUp ? -step : step));
                        App.Instance.PlayheadMoved();
                    }
                    QueueRedraw();
                }
                AcceptEvent();
                return;
            }
            if (mb.ButtonIndex == MouseButton.Middle)
            {
                drag = mb.Pressed ? DragMode.Pan : DragMode.None;
                dragStartMouse = mb.Position;
                AcceptEvent();
                return;
            }
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed) { GrabFocus(); BeginDrag(mb); }
                else EndDrag();
                AcceptEvent();
            }
            if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            {
                App.Instance.ShowObjectMenu(GetScreenTransform() * mb.Position);
                AcceptEvent();
            }
        }
        else if (e is InputEventMouseMotion mm)
        {
            if (drag == DragMode.Pan) { pan += mm.Relative; QueueRedraw(); }
            else if (drag != DragMode.None) { UpdateDrag(mm); }
        }
    }

    private void BeginDrag(InputEventMouseButton mb)
    {
        dragStartMouse = mb.Position;
        dragStartGame = ViewToGame(mb.Position);
        dragMoved = false;
        double t = S.Playhead;

        // Handles of the single selected object come first.
        var primary = S.Primary;
        var lp = primary != null ? Live(primary) : null;
        if (lp != null)
        {
            var m = LevelEvaluator.TransformAt(lp, t);
            if (RotateHandle(m, lp).DistanceTo(mb.Position) < 9) { StartTransform(DragMode.Rotate); return; }
            foreach (var c in Corners)
                if (GameToView(m.Apply(c.X, c.Y)).DistanceTo(mb.Position) < 8) { scaleCorner = c; StartTransform(DragMode.Scale); return; }
        }

        var hit = HitTest(mb.Position);
        if (hit != null)
        {
            if (mb.ShiftPressed) S.Toggle(hit);
            else if (!S.Selected.Contains(hit)) S.Select(new[] { hit });
            if (S.Selected.Contains(hit)) StartTransform(DragMode.Move);
            return;
        }
        if (!mb.ShiftPressed) S.ClearSelection();
        drag = DragMode.Marquee;
    }

    private void StartTransform(DragMode mode)
    {
        drag = mode;
        dragStart.Clear();
        double t = S.Playhead;
        foreach (var o in S.SelectedObjects)
        {
            double lt = t - o.Start;
            dragStart[o.Id] = (Eval(o, "x", lt), Eval(o, "y", lt), Eval(o, "rot", lt), Eval(o, "w", lt), Eval(o, "h", lt));
        }
    }

    private void UpdateDrag(InputEventMouseMotion mm)
    {
        var game = ViewToGame(mm.Position);
        if (!dragMoved && mm.Position.DistanceTo(dragStartMouse) < 3) return;
        if (!dragMoved && drag != DragMode.Marquee)
            S.Snapshot(drag switch { DragMode.Rotate => "Rotate", DragMode.Scale => "Resize", _ => "Move" });
        dragMoved = true;
        double t = S.Playhead;

        switch (drag)
        {
            case DragMode.Move:
            {
                var d = game - dragStartGame;
                if (Input.IsKeyPressed(Key.Shift)) { if (Math.Abs(d.X) > Math.Abs(d.Y)) d.Y = 0; else d.X = 0; }
                bool wholePath = Input.IsKeyPressed(Key.Alt);
                foreach (var o in S.SelectedObjects)
                {
                    if (!dragStart.TryGetValue(o.Id, out var st)) continue;
                    var local = ToParentSpace(o, t, d);
                    double nx = st.x + local.X, ny = st.y + local.Y;
                    if (Input.IsKeyPressed(Key.Ctrl)) { nx = Math.Round(nx / 10) * 10; ny = Math.Round(ny / 10) * 10; }
                    if (wholePath)
                    {
                        Offset(o, "x", nx - Eval(o, "x", t - o.Start));
                        Offset(o, "y", ny - Eval(o, "y", t - o.Start));
                    }
                    else
                    {
                        SetAt(o, "x", t - o.Start, nx);
                        SetAt(o, "y", t - o.Start, ny);
                    }
                }
                break;
            }
            case DragMode.Rotate:
            {
                var o = S.Primary;
                var lo = o == null ? null : Live(o);
                if (lo == null) break;
                var m = LevelEvaluator.TransformAt(lo, t);
                var pivot = m.Apply(lo.PivotX, lo.PivotY);
                double a0 = Math.Atan2(dragStartGame.Y - pivot.Y, dragStartGame.X - pivot.X);
                double a1 = Math.Atan2(game.Y - pivot.Y, game.X - pivot.X);
                double rot = dragStart[o.Id].rot + (a1 - a0) * 180 / Math.PI;
                if (Input.IsKeyPressed(Key.Shift)) rot = Math.Round(rot / 15) * 15;
                SetAt(o, "rot", t - o.Start, rot);
                break;
            }
            case DragMode.Scale:
            {
                var o = S.Primary;
                var lo = o == null ? null : Live(o);
                if (lo == null) break;
                var m = LevelEvaluator.TransformAt(lo, t);
                double theta = Math.Atan2(m.B, m.A);
                var pivot = m.Apply(lo.PivotX, lo.PivotY);
                double dx = game.X - pivot.X, dy = game.Y - pivot.Y;
                double lx = Math.Cos(-theta) * dx - Math.Sin(-theta) * dy;
                double ly = Math.Sin(-theta) * dx + Math.Cos(-theta) * dy;
                double fx = scaleCorner.X - lo.PivotX, fy = scaleCorner.Y - lo.PivotY;
                double w = Math.Abs(fx) > 1e-6 ? Math.Max(1, lx / fx) : dragStart[o.Id].w;
                double h = Math.Abs(fy) > 1e-6 ? Math.Max(1, ly / fy) : dragStart[o.Id].h;
                if (Input.IsKeyPressed(Key.Shift)) { double s = Math.Max(w / dragStart[o.Id].w, h / dragStart[o.Id].h); w = dragStart[o.Id].w * s; h = dragStart[o.Id].h * s; }
                SetAt(o, "w", t - o.Start, Math.Round(w, 1));
                SetAt(o, "h", t - o.Start, Math.Round(h, 1));
                break;
            }
        }
        if (drag == DragMode.Marquee) { QueueRedraw(); return; }
        S.Touch();
    }

    private void EndDrag()
    {
        if (drag == DragMode.Marquee)
        {
            var r = MarqueeRect();
            if (r.Size.Length() > 4)
            {
                var ids = items.Where(it => r.HasPoint(GameToView(it.Transform.Apply(0, 0)))).Select(it => it.Object.Id).ToList();
                S.Select(ids, Input.IsKeyPressed(Key.Shift));
            }
        }
        drag = DragMode.None;
        QueueRedraw();
    }

    private Rect2 MarqueeRect()
    {
        var a = dragStartMouse;
        var b = GetLocalMousePosition();
        return new Rect2(new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), (a - b).Abs());
    }

    /// <summary>Top-most object under a view position: outline hit first, then small objects by center.</summary>
    private string HitTest(Vector2 view)
    {
        var g = ViewToGame(view);
        var p = new Vec2(g.X, g.Y);
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var it = items[i];
            var outline = it.Geometry.Outline.Select(u => it.Transform.Apply(u)).ToArray();
            if (Collision.PointInPolygon(p, outline, outline.Length))
            {
                if (it.Geometry.Hole != null)
                {
                    var hole = it.Geometry.Hole.Select(u => it.Transform.Apply(u)).ToArray();
                    if (Collision.PointInPolygon(p, hole, hole.Length)) continue;
                }
                return it.Object.Id;
            }
            if (GameToView(it.Transform.Apply(0, 0)).DistanceTo(view) < 7) return it.Object.Id;
        }
        return null;
    }

    // ---------------- drag and drop from the palette ----------------

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().ContainsKey("beatform");

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var dict = data.AsGodotDictionary();
        var g = ViewToGame(atPosition);
        App.Instance.Palette.DropAt((string)dict["beatform"], g.X, g.Y);
    }

    // ---------------- track helpers ----------------

    public static double Eval(EditObject o, string track, double lt)
    {
        var keys = o.Track(track);
        if (keys.Count == 0) return EditObject.DefaultValue(track);
        var tr = new Track { Keys = keys.OrderBy(k => k.T).Select(k => new Keyframe { T = k.T, V = k.V, Ease = Ease.Parse(k.Ease) }).ToArray() };
        return tr.Evaluate(lt);
    }

    /// <summary>
    /// Writes a value at a local time. A track with at most one key is "static": its single value is
    /// changed. Animated tracks get a key at that time (or the existing key there is changed).
    /// </summary>
    public static void SetAt(EditObject o, string track, double lt, double value)
    {
        var keys = o.Track(track);
        if (keys.Count == 0) { keys.Add(new EditKey { T = 0, V = value }); return; }
        if (keys.Count == 1) { keys[0].V = value; return; }
        var existing = keys.FirstOrDefault(k => Math.Abs(k.T - lt) < 0.5);
        if (existing != null) { existing.V = value; return; }
        keys.Add(new EditKey { T = Math.Round(lt, 3), V = value });
        keys.Sort((a, b) => a.T.CompareTo(b.T));
    }

    public static void AddKeyAt(EditObject o, string track, double lt)
    {
        var keys = o.Track(track);
        double v = Eval(o, track, lt);
        if (keys.Any(k => Math.Abs(k.T - lt) < 0.5)) return;
        if (keys.Count == 0) keys.Add(new EditKey { T = 0, V = v });
        if (Math.Abs(lt) > 0.5 || keys.Count == 0) keys.Add(new EditKey { T = Math.Round(lt, 3), V = v });
        keys.Sort((a, b) => a.T.CompareTo(b.T));
    }

    private static void Offset(EditObject o, string track, double delta)
    {
        var keys = o.Track(track);
        if (keys.Count == 0) keys.Add(new EditKey { T = 0, V = EditObject.DefaultValue(track) });
        foreach (var k in keys) k.V += delta;
    }

    /// <summary>Converts a world-space move into the object's parent space (parents can rotate).</summary>
    private Vector2 ToParentSpace(EditObject o, double t, Vector2 d)
    {
        if (string.IsNullOrEmpty(o.Parent)) return d;
        var lo = Live(o);
        if (lo == null) return d;
        var m = LevelEvaluator.TransformAt(lo, t);
        double total = Math.Atan2(m.B, m.A);
        double own = Eval(o, "rot", t - o.Start) * Math.PI / 180;
        double parent = total - own;
        double c = Math.Cos(-parent), s = Math.Sin(-parent);
        return new Vector2((float)(c * d.X - s * d.Y), (float)(s * d.X + c * d.Y));
    }
}
