using System;
using System.Collections.Generic;

namespace Beatform.Core;

public struct Vec2
{
    public double X, Y;
    public Vec2(double x, double y) { X = x; Y = y; }
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
}

/// <summary>2D affine transform: world = (A*x + C*y + Tx, B*x + D*y + Ty).</summary>
public struct Affine
{
    public double A, B, C, D, Tx, Ty;

    public Vec2 Apply(double x, double y) => new(A * x + C * y + Tx, B * x + D * y + Ty);
    public Vec2 Apply(Vec2 p) => Apply(p.X, p.Y);

    /// <summary>Rotation by deg (clockwise on a y-down screen) followed by translation.</summary>
    public static Affine RotateTranslate(double deg, double x, double y)
    {
        double r = deg * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        return new Affine { A = cos, B = sin, C = -sin, D = cos, Tx = x, Ty = y };
    }

    /// <summary>this ∘ m: apply m first, then this.</summary>
    public Affine Then(Affine outer) => new()
    {
        A = outer.A * A + outer.C * B,
        B = outer.B * A + outer.D * B,
        C = outer.A * C + outer.C * D,
        D = outer.B * C + outer.D * D,
        Tx = outer.A * Tx + outer.C * Ty + outer.Tx,
        Ty = outer.B * Tx + outer.D * Ty + outer.Ty,
    };
}

/// <summary>
/// Unit-space geometry of a shape (outline and filled triangles), defined on [-0.5, 0.5]².
/// Built once per distinct shape definition and shared.
/// </summary>
public sealed class ShapeGeometry
{
    public const int CircleSegments = 48;

    /// <summary>Filled triangles: vertex list and index triples.</summary>
    public Vec2[] Vertices;
    public int[] Indices;
    /// <summary>Collision outline (simple polygon, any winding).</summary>
    public Vec2[] Outline;
    /// <summary>For rings: the inner hole outline. Null otherwise.</summary>
    public Vec2[] Hole;

    private static readonly Dictionary<string, ShapeGeometry> cache = new();

    public static ShapeGeometry For(ShapeDef def)
    {
        var key = def.GeometryKey;
        lock (cache)
        {
            if (!cache.TryGetValue(key, out var g)) cache[key] = g = Build(def);
            return g;
        }
    }

    private static ShapeGeometry Build(ShapeDef def)
    {
        switch (def.Type)
        {
            case ShapeType.Rect:
            {
                var outline = new[] { new Vec2(-0.5, -0.5), new Vec2(0.5, -0.5), new Vec2(0.5, 0.5), new Vec2(-0.5, 0.5) };
                return new ShapeGeometry { Vertices = outline, Indices = new[] { 0, 1, 2, 0, 2, 3 }, Outline = outline };
            }
            case ShapeType.Circle:
                return Fan(Regular(CircleSegments, 0.5, 0));
            case ShapeType.Polygon:
                return Fan(Regular(def.Sides, 0.5, -Math.PI / 2));
            case ShapeType.Star:
                return Fan(StarOutline(def.Points, def.Inner));
            case ShapeType.Spikes:
                return Fan(StarOutline(def.Spikes, def.Inner));
            case ShapeType.Ring:
            {
                var outer = Regular(CircleSegments, 0.5, 0);
                var inner = Regular(CircleSegments, 0.5 - def.Thickness, 0);
                var verts = new Vec2[CircleSegments * 2];
                var idx = new int[CircleSegments * 6];
                for (int i = 0; i < CircleSegments; i++)
                {
                    verts[i * 2] = outer[i];
                    verts[i * 2 + 1] = inner[i];
                }
                for (int i = 0; i < CircleSegments; i++)
                {
                    int o0 = i * 2, i0 = i * 2 + 1, o1 = ((i + 1) % CircleSegments) * 2, i1 = o1 + 1;
                    idx[i * 6 + 0] = o0; idx[i * 6 + 1] = o1; idx[i * 6 + 2] = i0;
                    idx[i * 6 + 3] = i0; idx[i * 6 + 4] = o1; idx[i * 6 + 5] = i1;
                }
                return new ShapeGeometry { Vertices = verts, Indices = idx, Outline = outer, Hole = inner };
            }
        }
        throw new ArgumentOutOfRangeException(nameof(def));
    }

    /// <summary>Regular polygon with n vertices at radius r, first vertex at angle start (radians, y-down).</summary>
    public static Vec2[] Regular(int n, double r, double start)
    {
        var pts = new Vec2[n];
        for (int i = 0; i < n; i++)
        {
            double a = start + 2 * Math.PI * i / n;
            pts[i] = new Vec2(Math.Cos(a) * r, Math.Sin(a) * r);
        }
        return pts;
    }

    public static Vec2[] StarOutline(int points, double inner)
    {
        var pts = new Vec2[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            double a = -Math.PI / 2 + Math.PI * i / points;
            double r = i % 2 == 0 ? 0.5 : 0.5 * inner;
            pts[i] = new Vec2(Math.Cos(a) * r, Math.Sin(a) * r);
        }
        return pts;
    }

    /// <summary>Triangle fan around the origin; valid for every outline that is star-shaped from (0,0).</summary>
    private static ShapeGeometry Fan(Vec2[] outline)
    {
        int n = outline.Length;
        var verts = new Vec2[n + 1];
        verts[0] = new Vec2(0, 0);
        Array.Copy(outline, 0, verts, 1, n);
        var idx = new int[n * 3];
        for (int i = 0; i < n; i++)
        {
            idx[i * 3] = 0;
            idx[i * 3 + 1] = 1 + i;
            idx[i * 3 + 2] = 1 + (i + 1) % n;
        }
        return new ShapeGeometry { Vertices = verts, Indices = idx, Outline = outline };
    }
}

public static class Collision
{
    /// <summary>True if a circle overlaps the (possibly concave) polygon made of the first n points.</summary>
    public static bool CircleOverlapsPolygon(Vec2 c, double r, Vec2[] poly, int n)
    {
        if (PointInPolygon(c, poly, n)) return true;
        double r2 = r * r;
        for (int i = 0, j = n - 1; i < n; j = i++)
            if (DistSqToSegment(c, poly[j], poly[i]) <= r2) return true;
        return false;
    }

    /// <summary>True if the circle lies entirely inside the polygon made of the first n points.</summary>
    public static bool CircleInsidePolygon(Vec2 c, double r, Vec2[] poly, int n)
    {
        if (!PointInPolygon(c, poly, n)) return false;
        double r2 = r * r;
        for (int i = 0, j = n - 1; i < n; j = i++)
            if (DistSqToSegment(c, poly[j], poly[i]) < r2) return false;
        return true;
    }

    public static bool PointInPolygon(Vec2 p, Vec2[] poly, int n)
    {
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    public static double DistSqToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        double t = len2 > 0 ? ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2 : 0;
        t = Math.Clamp(t, 0, 1);
        double ex = a.X + dx * t - p.X, ey = a.Y + dy * t - p.Y;
        return ex * ex + ey * ey;
    }
}
