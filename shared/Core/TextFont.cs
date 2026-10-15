using System;
using System.Collections.Generic;

namespace Sonokinesis.Core;

/// <summary>
/// Sonokinesis's text font: Montserrat Bold and Montserrat Medium, with the glyph outlines filled ahead of
/// time by tools/bake_font.py (TextFontData). A text shape fills its object's box like every other
/// shape: capital letters span the box height (baseline at the bottom edge, descenders below it) and the
/// ink of the line is squeezed to the box width, so a box <see cref="Width"/> times wider than tall
/// shows undistorted letters (see spec/level-format.md, "Text").
/// </summary>
public static class TextFont
{
    private sealed class Glyph
    {
        public double Advance, InkMin, InkMax;
        public Vec2[] Vertices;
        public int[] Indices;
    }

    private sealed class Face
    {
        public readonly Dictionary<char, Glyph> Glyphs = new();
        public readonly Dictionary<int, double> Kerning = new();
    }

    private static Face bold, medium;
    private static readonly object gate = new();

    private static Face Get(bool isBold)
    {
        lock (gate)
        {
            if (isBold) return bold ??= Decode(TextFontData.Bold);
            return medium ??= Decode(TextFontData.Medium);
        }
    }

    private static Face Decode(string data)
    {
        var b = Convert.FromBase64String(data);
        int o = 0;
        int U16() { int v = b[o] | (b[o + 1] << 8); o += 2; return v; }
        int S16() { return (short)U16(); }
        var f = new Face();
        int n = U16();
        for (int i = 0; i < n; i++)
        {
            char ch = (char)U16();
            var g = new Glyph { Advance = S16(), InkMin = S16(), InkMax = S16() };
            int nv = U16(), ni = U16();
            g.Vertices = new Vec2[nv];
            for (int k = 0; k < nv; k++) { double x = S16(); g.Vertices[k] = new Vec2(x, S16()); }
            g.Indices = new int[ni];
            for (int k = 0; k < ni; k++) g.Indices[k] = U16();
            f.Glyphs[ch] = g;
        }
        int nk = U16();
        for (int i = 0; i < nk; i++)
        {
            int l = U16(), r = U16();
            f.Kerning[(l << 16) | r] = S16();
        }
        return f;
    }

    /// <summary>Lays out a line in font units: each glyph with ink and its pen position.</summary>
    private static List<(Glyph g, double x)> Layout(string text, Face f, out double inkMin, out double inkMax)
    {
        var placed = new List<(Glyph, double)>();
        inkMin = double.MaxValue; inkMax = double.MinValue;
        double pen = 0;
        char prev = '\0';
        f.Glyphs.TryGetValue(' ', out var space);
        foreach (char ch in text ?? "")
        {
            if (!f.Glyphs.TryGetValue(ch, out var g)) g = space;
            if (g == null) continue;
            if (prev != '\0' && f.Kerning.TryGetValue((prev << 16) | ch, out var k)) pen += k;
            if (g.Indices.Length > 0)
            {
                placed.Add((g, pen));
                inkMin = Math.Min(inkMin, pen + g.InkMin);
                inkMax = Math.Max(inkMax, pen + g.InkMax);
            }
            pen += g.Advance;
            prev = ch;
        }
        return placed;
    }

    /// <summary>Width of the line's ink in capital heights; 0.5 for text without visible letters.</summary>
    public static double Width(string text, bool isBold = true)
    {
        var placed = Layout(text, Get(isBold), out double inkMin, out double inkMax);
        return placed.Count == 0 ? 0.5 : (inkMax - inkMin) / TextFontData.CapHeight;
    }

    /// <summary>The line as filled triangles in unit space. The collision outline is the whole box.</summary>
    public static ShapeGeometry Build(string text, bool isBold)
    {
        var placed = Layout(text, Get(isBold), out double inkMin, out double inkMax);
        double cap = TextFontData.CapHeight;
        double width = placed.Count == 0 ? 1 : inkMax - inkMin;
        var verts = new List<Vec2>();
        var idx = new List<int>();
        foreach (var (g, x) in placed)
        {
            int baseIndex = verts.Count;
            foreach (var v in g.Vertices)
                verts.Add(new Vec2((x + v.X - inkMin) / width - 0.5, v.Y / cap + 0.5));
            // Baked counter-clockwise with y up; flipped to wind like the other shapes with y down.
            for (int i = 0; i + 2 < g.Indices.Length; i += 3)
            {
                idx.Add(baseIndex + g.Indices[i]);
                idx.Add(baseIndex + g.Indices[i + 2]);
                idx.Add(baseIndex + g.Indices[i + 1]);
            }
        }
        var outline = new[] { new Vec2(-0.5, -0.5), new Vec2(0.5, -0.5), new Vec2(0.5, 0.5), new Vec2(-0.5, 0.5) };
        return new ShapeGeometry { Vertices = verts.ToArray(), Indices = idx.ToArray(), Outline = outline };
    }
}
