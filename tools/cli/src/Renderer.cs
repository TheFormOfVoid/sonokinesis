using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Sonokinesis.Core;

namespace Sonokinesis.Cli;

/// <summary>An RGB picture with float channels, drawn into with alpha blending.</summary>
public sealed class Canvas
{
    public readonly int W, H;
    public readonly float[] Px;

    public Canvas(int w, int h)
    {
        W = w;
        H = h;
        Px = new float[w * h * 3];
    }

    public void Clear(Rgba c)
    {
        for (int i = 0; i < W * H; i++) { Px[i * 3] = c.R; Px[i * 3 + 1] = c.G; Px[i * 3 + 2] = c.B; }
    }

    public void FillRect(double x0, double y0, double x1, double y1, Rgba c)
    {
        int a = Math.Max(0, (int)Math.Round(x0)), b = Math.Min(W, (int)Math.Round(x1));
        int top = Math.Max(0, (int)Math.Round(y0)), bottom = Math.Min(H, (int)Math.Round(y1));
        for (int y = top; y < bottom; y++)
            for (int x = a; x < b; x++) Blend(x, y, c);
    }

    private void Blend(int x, int y, Rgba c)
    {
        int i = (y * W + x) * 3;
        float a = c.A;
        Px[i] += (c.R - Px[i]) * a;
        Px[i + 1] += (c.G - Px[i + 1]) * a;
        Px[i + 2] += (c.B - Px[i + 2]) * a;
    }

    /// <summary>Fills a triangle (pixel centres inside it, either winding).</summary>
    public void FillTriangle(Vec2 p0, Vec2 p1, Vec2 p2, Rgba c)
    {
        if (c.A <= 0.002f) return;
        double area = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
        if (Math.Abs(area) < 1e-9) return;
        if (area < 0) (p1, p2) = (p2, p1);
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(p0.X, Math.Min(p1.X, p2.X))));
        int maxX = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(p0.X, Math.Max(p1.X, p2.X))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(p0.Y, Math.Min(p1.Y, p2.Y))));
        int maxY = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(p0.Y, Math.Max(p1.Y, p2.Y))));
        for (int y = minY; y <= maxY; y++)
        {
            double py = y + 0.5;
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                if (Edge(p0, p1, px, py) < 0 || Edge(p1, p2, px, py) < 0 || Edge(p2, p0, px, py) < 0) continue;
                Blend(x, y, c);
            }
        }
    }

    private static double Edge(Vec2 a, Vec2 b, double x, double y) => (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);

    public void FillGeometry(ShapeGeometry g, Affine toPixels, Rgba c)
    {
        var v = g.Vertices;
        var idx = g.Indices;
        var pts = new Vec2[v.Length];
        for (int i = 0; i < v.Length; i++) pts[i] = toPixels.Apply(v[i]);
        for (int i = 0; i + 2 < idx.Length; i += 3) FillTriangle(pts[idx[i]], pts[idx[i + 1]], pts[idx[i + 2]], c);
    }

    public void StrokeRect(double x0, double y0, double x1, double y1, double width, Rgba c)
    {
        FillRect(x0, y0, x1, y0 + width, c);
        FillRect(x0, y1 - width, x1, y1, c);
        FillRect(x0, y0 + width, x0 + width, y1 - width, c);
        FillRect(x1 - width, y0 + width, x1, y1 - width, c);
    }

    /// <summary>One line of text, capital height <paramref name="size"/> pixels, left edge at x, baseline at y.</summary>
    public double Text(string text, double x, double baseline, double size, Rgba c, bool bold = true)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var geo = ShapeGeometry.For(new ShapeDef { Type = ShapeType.Text, Text = text, Bold = bold });
        double w = TextFont.Width(text, bold) * size;
        // The unit square maps to the box: capitals fill its height, the baseline is its bottom edge.
        var m = new Affine { A = w, D = size, Tx = x + w / 2, Ty = baseline - size / 2 };
        FillGeometry(geo, m, c);
        return w;
    }

    /// <summary>A copy at half the size (2×2 box filter): how frames are antialiased.</summary>
    public Canvas Half()
    {
        var res = new Canvas(W / 2, H / 2);
        for (int y = 0; y < res.H; y++)
            for (int x = 0; x < res.W; x++)
                for (int ch = 0; ch < 3; ch++)
                {
                    float s = Px[((2 * y) * W + 2 * x) * 3 + ch] + Px[((2 * y) * W + 2 * x + 1) * 3 + ch]
                        + Px[((2 * y + 1) * W + 2 * x) * 3 + ch] + Px[((2 * y + 1) * W + 2 * x + 1) * 3 + ch];
                    res.Px[(y * res.W + x) * 3 + ch] = s / 4;
                }
        return res;
    }

    public void Paste(Canvas src, int ox, int oy)
    {
        for (int y = 0; y < src.H; y++)
        {
            int ty = oy + y;
            if (ty < 0 || ty >= H) continue;
            for (int x = 0; x < src.W; x++)
            {
                int tx = ox + x;
                if (tx < 0 || tx >= W) continue;
                Array.Copy(src.Px, (y * src.W + x) * 3, Px, (ty * W + tx) * 3, 3);
            }
        }
    }

    public byte[] ToPng() => Png.Encode(W, H, Px);
}

/// <summary>A minimal PNG writer (8-bit RGB).</summary>
public static class Png
{
    public static byte[] Encode(int w, int h, float[] rgb)
    {
        var raw = new byte[(w * 3 + 1) * h];
        for (int y = 0; y < h; y++)
        {
            int row = y * (w * 3 + 1);
            raw[row] = 0;
            for (int i = 0; i < w * 3; i++)
            {
                float v = rgb[y * w * 3 + i];
                raw[row + 1 + i] = (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
            }
        }
        byte[] idat;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
            idat = ms.ToArray();
        }
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, w);
        WriteBE(ihdr, 4, h);
        ihdr[8] = 8;
        ihdr[9] = 2;
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", idat);
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteBE(byte[] b, int at, int v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, data.Length);
        s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        Array.Copy(data, 0, td, 4, data.Length);
        s.Write(td);
        var crc = new byte[4];
        WriteBE(crc, 0, (int)Crc(td));
        s.Write(crc);
    }

    private static uint[] table;

    private static uint Crc(byte[] data)
    {
        if (table == null)
        {
            table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
        }
        uint crc = 0xFFFFFFFF;
        foreach (var b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}

/// <summary>Options for drawing frames of a level.</summary>
public sealed class RenderOptions
{
    public int Width = 960;
    public int Seed;
    public Vec2 Player = new(-300, 150);
    public bool ShowPlayer = true;
    public bool ShowCamera = true;
    public bool Label = true;
}

/// <summary>
/// Draws a level at given song times the way the game shows it: background, hazards (with the same
/// evaluator as the game), a stand-in player, camera and flashes. The player stands still; imposters
/// chase it and "@player" objects follow it.
/// </summary>
public sealed class LevelRenderer
{
    public readonly LevelEvaluator Ev;
    public readonly LevelData Data;
    private readonly RenderOptions opt;
    private readonly List<DrawItem> items = new();
    private readonly List<Rgba> flashes = new();
    private double lastT = double.NegativeInfinity;

    public LevelRenderer(LevelData data, RenderOptions options)
    {
        Data = data;
        opt = options;
        Ev = new LevelEvaluator(data);
        var players = new PlayerList();
        players.Players.Add(new PlayerInfo { Number = 1, Position = options.Player, Color = LevelData.DefaultPlayerColor, Alive = true });
        Ev.Players = players;
        Ev.Seed = options.Seed;
    }

    public double BeatMs => 60000 / Math.Max(1, Data.Meta.Bpm);

    /// <summary>"bar.beat" for a song time (1-based, like the editor).</summary>
    public string BarBeat(double t)
    {
        double beats = t / BeatMs;
        int per = Math.Max(1, Data.Meta.BeatsPerBar);
        int bar = (int)Math.Floor(beats / per) + 1;
        double beat = beats - (bar - 1) * per + 1;
        return $"{bar}.{beat:0.##}";
    }

    public Canvas Frame(double t)
    {
        // Scripts and imposters simulate forward; going back restarts them.
        if (t < lastT) Ev.ResetImposters();
        lastT = t;
        int w = opt.Width * 2, h = (int)Math.Round(opt.Width * 9.0 / 16) * 2;
        double s = w / 1280.0;
        var c = new Canvas(w, h);
        c.Clear(Ev.BackgroundAt(t));
        var cam = opt.ShowCamera ? Ev.CameraAt(t) : new CameraState { Scale = 1 };
        var camM = new Affine { A = cam.Scale, D = cam.Scale }.Then(Affine.RotateTranslate(cam.Rotation, cam.X, cam.Y));
        var toPx = new Affine { A = s, D = s, Tx = w / 2.0, Ty = h / 2.0 };
        var world = camM.Then(toPx);
        Ev.Collect(t, items);
        foreach (var it in items) if (!it.Front) c.FillGeometry(it.Geometry, it.Transform.Then(world), it.Color);
        if (opt.ShowPlayer)
        {
            var p = world.Apply(opt.Player);
            double r = 11 * s * cam.Scale;
            var pc = LevelData.DefaultPlayerColor;
            c.FillGeometry(ShapeGeometry.For(new ShapeDef { Type = ShapeType.Rect }), new Affine { A = 2 * r, D = 2 * r, Tx = p.X, Ty = p.Y }, pc);
        }
        foreach (var it in items) if (it.Front) c.FillGeometry(it.Geometry, it.Transform.Then(world), it.Color);
        Ev.Flashes(t, flashes);
        foreach (var f in flashes) c.FillRect(0, 0, w, h, f);
        if (opt.Label)
        {
            string label = $"{t / 1000:0.000}s  bar {BarBeat(t)}";
            c.FillRect(0, h - 40, 28 + TextFont.Width(label, false) * 20, h, new Rgba(0, 0, 0, 0.55f));
            c.Text(label, 14, h - 12, 20, new Rgba(1, 1, 1, 0.9f), false);
        }
        return c.Half();
    }

    /// <summary>Frames side by side in a grid: an overview of a stretch of the level in one picture.</summary>
    public Canvas Sheet(IList<double> times, int columns)
    {
        columns = Math.Clamp(columns, 1, times.Count);
        int rows = (times.Count + columns - 1) / columns;
        Canvas first = null;
        var frames = new List<Canvas>();
        foreach (var t in times) { var f = Frame(t); frames.Add(f); first ??= f; }
        int gap = 4;
        var sheet = new Canvas(columns * first.W + (columns + 1) * gap, rows * first.H + (rows + 1) * gap);
        sheet.Clear(new Rgba(0.2f, 0.22f, 0.27f));
        for (int i = 0; i < frames.Count; i++)
            sheet.Paste(frames[i], gap + (i % columns) * (first.W + gap), gap + (i / columns) * (first.H + gap));
        return sheet;
    }
}
