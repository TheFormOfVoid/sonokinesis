using System;
using System.Collections.Generic;

namespace Beatform.Core;

/// <summary>
/// Beatform's own stroke font: every glyph is a few polylines on a 4 × 6 grid (x right, y down),
/// drawn as thick segments. Lowercase letters use the uppercase glyphs; characters without a glyph
/// leave a blank space. A text shape fills its object's box like every other shape: the line is
/// squeezed to [-0.5, 0.5]², so a box `Width(text)` times wider than tall shows undistorted letters
/// (see spec/level-format.md).
/// </summary>
public static class StrokeFont
{
    public const double GlyphWidth = 4, GlyphHeight = 6, Spacing = 1.4;

    private static readonly Dictionary<char, double[][]> glyphs = new()
    {
        ['A'] = new[] { P(0, 6, 0, 2, 2, 0, 4, 2, 4, 6), P(0, 3.5, 4, 3.5) },
        ['B'] = new[] { P(0, 0, 0, 6, 3, 6, 4, 5, 4, 4, 3, 3, 0, 3), P(0, 0, 3, 0, 4, 1, 4, 2, 3, 3) },
        ['C'] = new[] { P(4, 0, 1, 0, 0, 1, 0, 5, 1, 6, 4, 6) },
        ['D'] = new[] { P(0, 0, 0, 6, 2.5, 6, 4, 4.5, 4, 1.5, 2.5, 0, 0, 0) },
        ['E'] = new[] { P(4, 0, 0, 0, 0, 6, 4, 6), P(0, 3, 3, 3) },
        ['F'] = new[] { P(4, 0, 0, 0, 0, 6), P(0, 3, 3, 3) },
        ['G'] = new[] { P(4, 1, 3, 0, 1, 0, 0, 1, 0, 5, 1, 6, 3, 6, 4, 5, 4, 3, 2, 3) },
        ['H'] = new[] { P(0, 0, 0, 6), P(4, 0, 4, 6), P(0, 3, 4, 3) },
        ['I'] = new[] { P(1, 0, 3, 0), P(2, 0, 2, 6), P(1, 6, 3, 6) },
        ['J'] = new[] { P(4, 0, 4, 5, 3, 6, 1, 6, 0, 5) },
        ['K'] = new[] { P(0, 0, 0, 6), P(4, 0, 0, 3.5), P(1.4, 2.6, 4, 6) },
        ['L'] = new[] { P(0, 0, 0, 6, 4, 6) },
        ['M'] = new[] { P(0, 6, 0, 0, 2, 3, 4, 0, 4, 6) },
        ['N'] = new[] { P(0, 6, 0, 0, 4, 6, 4, 0) },
        ['O'] = new[] { P(1, 0, 3, 0, 4, 1, 4, 5, 3, 6, 1, 6, 0, 5, 0, 1, 1, 0) },
        ['P'] = new[] { P(0, 6, 0, 0, 3, 0, 4, 1, 4, 2, 3, 3, 0, 3) },
        ['Q'] = new[] { P(1, 0, 3, 0, 4, 1, 4, 5, 3, 6, 1, 6, 0, 5, 0, 1, 1, 0), P(2.5, 4.5, 4, 6) },
        ['R'] = new[] { P(0, 6, 0, 0, 3, 0, 4, 1, 4, 2, 3, 3, 0, 3), P(2, 3, 4, 6) },
        ['S'] = new[] { P(4, 1, 3, 0, 1, 0, 0, 1, 0, 2, 1, 3, 3, 3, 4, 4, 4, 5, 3, 6, 1, 6, 0, 5) },
        ['T'] = new[] { P(0, 0, 4, 0), P(2, 0, 2, 6) },
        ['U'] = new[] { P(0, 0, 0, 5, 1, 6, 3, 6, 4, 5, 4, 0) },
        ['V'] = new[] { P(0, 0, 2, 6, 4, 0) },
        ['W'] = new[] { P(0, 0, 1, 6, 2, 3, 3, 6, 4, 0) },
        ['X'] = new[] { P(0, 0, 4, 6), P(4, 0, 0, 6) },
        ['Y'] = new[] { P(0, 0, 2, 3, 4, 0), P(2, 3, 2, 6) },
        ['Z'] = new[] { P(0, 0, 4, 0, 0, 6, 4, 6) },
        ['0'] = new[] { P(1, 0, 3, 0, 4, 1, 4, 5, 3, 6, 1, 6, 0, 5, 0, 1, 1, 0), P(4, 1, 0, 5) },
        ['1'] = new[] { P(1, 1, 2, 0, 2, 6), P(1, 6, 3, 6) },
        ['2'] = new[] { P(0, 1, 1, 0, 3, 0, 4, 1, 4, 2, 0, 6, 4, 6) },
        ['3'] = new[] { P(0, 0, 4, 0, 2, 2.5, 3, 2.5, 4, 3.5, 4, 5, 3, 6, 1, 6, 0, 5) },
        ['4'] = new[] { P(3, 6, 3, 0, 0, 4, 4, 4) },
        ['5'] = new[] { P(4, 0, 0, 0, 0, 2.5, 3, 2.5, 4, 3.5, 4, 5, 3, 6, 0, 6) },
        ['6'] = new[] { P(3.5, 0, 1, 0, 0, 1, 0, 5, 1, 6, 3, 6, 4, 5, 4, 4, 3, 3, 0, 3) },
        ['7'] = new[] { P(0, 0, 4, 0, 1.5, 6) },
        ['8'] = new[] { P(1, 3, 0, 2, 0, 1, 1, 0, 3, 0, 4, 1, 4, 2, 3, 3, 1, 3, 0, 4, 0, 5, 1, 6, 3, 6, 4, 5, 4, 4, 3, 3) },
        ['9'] = new[] { P(0.5, 6, 3, 6, 4, 5, 4, 1, 3, 0, 1, 0, 0, 1, 0, 2, 1, 3, 4, 3) },
        ['.'] = new[] { P(2, 5.6, 2, 6) },
        [','] = new[] { P(2, 5.4, 1.5, 6.6) },
        ['!'] = new[] { P(2, 0, 2, 4), P(2, 5.6, 2, 6) },
        ['?'] = new[] { P(0, 1, 1, 0, 3, 0, 4, 1, 4, 2, 2, 3.5, 2, 4), P(2, 5.6, 2, 6) },
        ['\''] = new[] { P(2, 0, 2, 1.5) },
        ['"'] = new[] { P(1.3, 0, 1.3, 1.5), P(2.7, 0, 2.7, 1.5) },
        ['-'] = new[] { P(1, 3, 3, 3) },
        [':'] = new[] { P(2, 1.6, 2, 2), P(2, 4.6, 2, 5) },
        [';'] = new[] { P(2, 1.6, 2, 2), P(2, 4.6, 1.5, 5.8) },
        ['('] = new[] { P(2.5, 0, 1.5, 1.5, 1.5, 4.5, 2.5, 6) },
        [')'] = new[] { P(1.5, 0, 2.5, 1.5, 2.5, 4.5, 1.5, 6) },
        ['<'] = new[] { P(3.5, 1, 0.5, 3, 3.5, 5) },
        ['>'] = new[] { P(0.5, 1, 3.5, 3, 0.5, 5) },
        ['/'] = new[] { P(4, 0, 0, 6) },
        ['+'] = new[] { P(2, 1.5, 2, 4.5), P(0.5, 3, 3.5, 3) },
        ['='] = new[] { P(0.5, 2, 3.5, 2), P(0.5, 4, 3.5, 4) },
        ['_'] = new[] { P(0, 6, 4, 6) },
        ['*'] = new[] { P(2, 1, 2, 5), P(0.5, 2, 3.5, 4), P(3.5, 2, 0.5, 4) },
    };

    private static double[] P(params double[] xy) => xy;

    /// <summary>Total width of a line of text in units of cap height.</summary>
    public static double Width(string text)
    {
        int n = Math.Max(1, text?.Length ?? 0);
        return (n * (GlyphWidth + Spacing) - Spacing) / GlyphHeight;
    }

    /// <summary>
    /// Builds the text as thick segments (quads) in unit space. <paramref name="weight"/> is the stroke
    /// thickness as a fraction of the letter height. The collision outline is the whole box.
    /// </summary>
    public static ShapeGeometry Build(string text, double weight)
    {
        text ??= "";
        var verts = new List<Vec2>();
        var idx = new List<int>();
        double width = Width(text);
        double half = weight / 2;
        for (int c = 0; c < text.Length; c++)
        {
            char ch = char.ToUpperInvariant(text[c]);
            if (!glyphs.TryGetValue(ch, out var strokes)) continue;
            double ox = c * (GlyphWidth + Spacing);
            foreach (var s in strokes)
            {
                for (int i = 0; i + 3 < s.Length; i += 2)
                {
                    var a = new Vec2((ox + s[i]) / GlyphHeight - width / 2, s[i + 1] / GlyphHeight - 0.5);
                    var b = new Vec2((ox + s[i + 2]) / GlyphHeight - width / 2, s[i + 3] / GlyphHeight - 0.5);
                    AddSegment(verts, idx, a, b, half);
                }
            }
        }
        // Built with a letter height of 1, then squeezed horizontally into the unit box.
        var v = verts.ToArray();
        for (int i = 0; i < v.Length; i++) v[i].X /= width;
        var outline = new[] { new Vec2(-0.5, -0.5), new Vec2(0.5, -0.5), new Vec2(0.5, 0.5), new Vec2(-0.5, 0.5) };
        return new ShapeGeometry { Vertices = v, Indices = idx.ToArray(), Outline = outline };
    }

    /// <summary>A segment as a quad, extended by half the thickness at both ends (square caps).</summary>
    private static void AddSegment(List<Vec2> verts, List<int> idx, Vec2 a, Vec2 b, double half)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) { dx = 1; dy = 0; len = 1; }
        double ux = dx / len * half, uy = dy / len * half; // along the segment
        double nx = -uy, ny = ux;                           // across it
        int i0 = verts.Count;
        verts.Add(new Vec2(a.X - ux + nx, a.Y - uy + ny));
        verts.Add(new Vec2(b.X + ux + nx, b.Y + uy + ny));
        verts.Add(new Vec2(b.X + ux - nx, b.Y + uy - ny));
        verts.Add(new Vec2(a.X - ux - nx, a.Y - uy - ny));
        idx.Add(i0); idx.Add(i0 + 1); idx.Add(i0 + 2);
        idx.Add(i0); idx.Add(i0 + 2); idx.Add(i0 + 3);
    }
}
