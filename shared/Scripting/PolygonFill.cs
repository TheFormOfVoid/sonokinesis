using System;
using System.Collections.Generic;

namespace Beatform.Core;

/// <summary>Fills a simple polygon (any winding, convex or not) by ear clipping, for scripts' shape().</summary>
internal static class PolygonFill
{
    /// <summary>
    /// The polygon as shape geometry in unit space around its bounding box, so it collides like any
    /// shape; the caller draws it with <see cref="Placement"/>. Null if it has no area.
    /// </summary>
    public static ShapeGeometry Build(List<Vec2> world)
    {
        var pts = new List<Vec2>();
        foreach (var p in world)
            if (pts.Count == 0 || Math.Abs(pts[pts.Count - 1].X - p.X) > 1e-9 || Math.Abs(pts[pts.Count - 1].Y - p.Y) > 1e-9)
                pts.Add(p);
        if (pts.Count > 1 && Math.Abs(pts[0].X - pts[pts.Count - 1].X) < 1e-9 && Math.Abs(pts[0].Y - pts[pts.Count - 1].Y) < 1e-9)
            pts.RemoveAt(pts.Count - 1);
        if (pts.Count < 3) return null;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in pts) { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
        double w = Math.Max(1e-6, maxX - minX), h = Math.Max(1e-6, maxY - minY);
        double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        var unit = new Vec2[pts.Count];
        for (int i = 0; i < pts.Count; i++) unit[i] = new Vec2((pts[i].X - cx) / w, (pts[i].Y - cy) / h);
        var idx = EarClip(unit);
        if (idx.Count == 0) return null;
        return new ShapeGeometry { Vertices = unit, Indices = idx.ToArray(), Outline = unit, Placement = (cx, cy, w, h) };
    }

    private static double Cross(Vec2 o, Vec2 a, Vec2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    private static List<int> EarClip(Vec2[] p)
    {
        int n = p.Length;
        double area = 0;
        for (int i = 0, j = n - 1; i < n; j = i++) area += p[j].X * p[i].Y - p[i].X * p[j].Y;
        var idx = new List<int>(n);
        // Work counter-clockwise (positive area).
        if (area >= 0) for (int i = 0; i < n; i++) idx.Add(i); else for (int i = n - 1; i >= 0; i--) idx.Add(i);
        var tris = new List<int>();
        int guard = 0;
        while (idx.Count > 3 && guard++ < 10000)
        {
            bool clipped = false;
            for (int k = 0; k < idx.Count; k++)
            {
                int i0 = idx[(k + idx.Count - 1) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                if (Cross(p[i0], p[i1], p[i2]) <= 1e-12) continue;
                bool inside = false;
                foreach (int j in idx)
                {
                    if (j == i0 || j == i1 || j == i2) continue;
                    if (Cross(p[i0], p[i1], p[j]) >= 0 && Cross(p[i1], p[i2], p[j]) >= 0 && Cross(p[i2], p[i0], p[j]) >= 0) { inside = true; break; }
                }
                if (inside) continue;
                tris.Add(i0); tris.Add(i1); tris.Add(i2);
                idx.RemoveAt(k);
                clipped = true;
                break;
            }
            if (!clipped) idx.RemoveAt(0); // degenerate (self-touching) corner
        }
        if (idx.Count == 3 && Cross(p[idx[0]], p[idx[1]], p[idx[2]]) > 0) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
        return tris;
    }
}
