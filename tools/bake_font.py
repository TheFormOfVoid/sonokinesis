"""Bakes the text font into shared/Core/TextFontData.cs.

Beatform draws text as filled triangles like every other shape, so the font's glyph outlines are
flattened (quadratic curves become short lines) and triangulated here, once, and stored in the code.
The font is Montserrat (Bold and Medium), SIL Open Font License 1.1 (see shared/Fonts/OFL.txt).

Run with a Python that has fontTools:
    python tools/bake_font.py <Montserrat-Bold.ttf> <Montserrat-Medium.ttf> [metrics.json]
The optional metrics file (advances, ink extents and kerning per character) is for level scripts that
need to size boxes around text.
"""
import base64, json, math, os, struct, sys

from fontTools.pens.basePen import BasePen
from fontTools.ttLib import TTFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "shared", "Core", "TextFontData.cs")
CHARS = [chr(c) for c in range(32, 127)] + list("‘’“”…–—×")
TOLERANCE = 2.5  # max distance of a flattened curve from the real one, in font units (1000 per em)


class FlattenPen(BasePen):
    """Collects closed contours as point lists, with curves flattened."""

    def __init__(self, glyph_set):
        super().__init__(glyph_set)
        self.contours, self.cur = [], None

    def _moveTo(self, p):
        self.cur = [p]

    def _lineTo(self, p):
        self.cur.append(p)

    def _qCurveToOne(self, p1, p2):
        p0 = self.cur[-1]
        dev = math.hypot(p0[0] - 2 * p1[0] + p2[0], p0[1] - 2 * p1[1] + p2[1]) / 4
        n = max(1, math.ceil(math.sqrt(dev / TOLERANCE)))
        for i in range(1, n + 1):
            t = i / n
            a, b, c = (1 - t) ** 2, 2 * t * (1 - t), t * t
            self.cur.append((a * p0[0] + b * p1[0] + c * p2[0], a * p0[1] + b * p1[1] + c * p2[1]))

    def _curveToOne(self, p1, p2, p3):
        p0 = self.cur[-1]
        n = 8
        for i in range(1, n + 1):
            t = i / n
            a, b, c, d = (1 - t) ** 3, 3 * t * (1 - t) ** 2, 3 * t * t * (1 - t), t ** 3
            self.cur.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))

    def _closePath(self):
        if self.cur and len(self.cur) > 2:
            self.contours.append(self.cur)
        self.cur = None

    _endPath = _closePath


def cross(o, a, b):
    return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])


def in_triangle(p, a, b, c):
    return cross(a, b, p) >= 0 and cross(b, c, p) >= 0 and cross(c, a, p) >= 0


def clean(poly):
    """Rounds to whole font units and drops repeated points."""
    pts = []
    for p in poly:
        q = (round(p[0]), round(p[1]))
        if not pts or pts[-1] != q:
            pts.append(q)
    while len(pts) > 1 and pts[0] == pts[-1]:
        pts.pop()
    return pts


def winding(p, edges):
    """Nonzero winding number of a point (edges as (x0, y0, x1, y1, w) with y0 < y1)."""
    w = 0
    for x0, y0, x1, y1, d in edges:
        if y0 <= p[1] < y1 and p[0] < x0 + (p[1] - y0) * (x1 - x0) / (y1 - y0):
            w += d
    return w


def triangulate(contours):
    """Fills the contours with the nonzero rule, as TrueType does, and returns (vertices, triangles).

    Glyphs may be built from overlapping pieces (the bar of an H over its stems), so the outlines are
    cut into horizontal slabs at every vertex and crossing; in a slab the edges do not cross, and the
    stretches with a nonzero winding number are trapezoids. Trapezoids between the same two edges in
    consecutive slabs are joined. The result covers the glyph exactly once."""
    edges = []
    for c in contours:
        pts = clean(c)
        for i in range(len(pts)):
            (xa, ya), (xb, yb) = pts[i - 1], pts[i]
            if ya != yb:
                edges.append((xa, ya, xb, yb, 1) if ya < yb else (xb, yb, xa, ya, -1))
    if not edges:
        return [], []
    ys = {e[1] for e in edges} | {e[3] for e in edges}
    for i in range(len(edges)):
        ax0, ay0, ax1, ay1, _ = edges[i]
        for j in range(i + 1, len(edges)):
            bx0, by0, bx1, by1, _ = edges[j]
            if by0 >= ay1 or ay0 >= by1:
                continue
            dax, day, dbx, dby = ax1 - ax0, ay1 - ay0, bx1 - bx0, by1 - by0
            den = dax * dby - day * dbx
            if den == 0:
                continue
            t = ((bx0 - ax0) * dby - (by0 - ay0) * dbx) / den
            u = ((bx0 - ax0) * day - (by0 - ay0) * dax) / den
            if 0 < t < 1 and 0 < u < 1:
                ys.add(ay0 + t * day)
    ys = sorted(ys)

    def x_at(e, y):
        x0, y0, x1, y1, _ = e
        return x0 + (y - y0) * (x1 - x0) / (y1 - y0)

    traps, open_ = [], {}
    for ya, yb in zip(ys, ys[1:]):
        if yb - ya < 1e-6:
            continue
        ym = (ya + yb) / 2
        live = sorted((i for i, e in enumerate(edges) if e[1] <= ya and e[3] >= yb), key=lambda i: x_at(edges[i], ym))
        spans, w, left = [], 0, None
        for i in live:
            before = w
            w += edges[i][4]
            if before == 0 and w != 0:
                left = i
            elif before != 0 and w == 0:
                spans.append((left, i))
        nxt = {}
        for key in spans:
            prev = open_.pop(key, None)
            if prev and prev[1] == ya:
                nxt[key] = (prev[0], yb)
            else:
                if prev:
                    traps.append((key, prev))
                nxt[key] = (ya, yb)
        traps.extend(open_.items())
        open_ = nxt
    traps.extend(open_.items())

    verts, index, tris = [], {}, []

    def vid(x, y):
        k = (round(x), round(y))
        if k not in index:
            index[k] = len(verts)
            verts.append(k)
        return index[k]
    for (l, r), (ya, yb) in traps:
        a, b = vid(x_at(edges[l], ya), ya), vid(x_at(edges[r], ya), ya)
        c, d = vid(x_at(edges[r], yb), yb), vid(x_at(edges[l], yb), yb)
        for t in ((a, b, c), (a, c, d)):
            if len(set(t)) < 3:
                continue
            k = cross(verts[t[0]], verts[t[1]], verts[t[2]])
            if k:
                tris.append(t if k > 0 else (t[0], t[2], t[1]))

    # Check against the outlines: sample points must be covered exactly where the winding is nonzero.
    xs = [v[0] for v in verts]
    bad = total = 0
    for gx in range(min(xs) + 3, max(xs), 17):
        for gy in range(round(ys[0]) + 3, round(ys[-1]), 17):
            p = (gx + 0.37, gy + 0.41)
            if any(abs(cross((e[0], e[1]), (e[2], e[3]), p)) / math.hypot(e[2] - e[0], e[3] - e[1]) < 2.5 for e in edges):
                continue
            want = winding(p, edges) != 0
            cover = sum(1 for t in tris if in_triangle(p, verts[t[0]], verts[t[1]], verts[t[2]]))
            total += 1
            if cover != (1 if want else 0):
                bad += 1
    if bad:
        raise ValueError(f"{bad} of {total} sample points wrongly covered")
    return verts, tris


def kerning(font, cmap_rev):
    """Pair kerning of the 'kern' feature (GPOS pair adjustments), for the baked characters only."""
    pairs = {}
    if "GPOS" not in font:
        return pairs
    gpos = font["GPOS"].table
    wanted = set()
    for fr in gpos.FeatureList.FeatureRecord:
        if fr.FeatureTag == "kern":
            wanted.update(fr.Feature.LookupListIndex)
    glyphs = set(cmap_rev)
    for li in sorted(wanted):
        lookup = gpos.LookupList.Lookup[li]
        for st in lookup.SubTable:
            kind = lookup.LookupType
            if kind == 9:
                kind, st = st.ExtensionLookupType, st.ExtSubTable
            if kind != 2:
                continue
            cov = st.Coverage.glyphs
            if st.Format == 1:
                for gi, first in enumerate(cov):
                    if first not in glyphs:
                        continue
                    for rec in st.PairSet[gi].PairValueRecord:
                        v = getattr(rec.Value1, "XAdvance", 0) if rec.Value1 else 0
                        if rec.SecondGlyph in glyphs and v:
                            pairs.setdefault((first, rec.SecondGlyph), v)
            elif st.Format == 2:
                c1 = st.ClassDef1.classDefs
                c2 = st.ClassDef2.classDefs
                for first in cov:
                    if first not in glyphs:
                        continue
                    row = st.Class1Record[c1.get(first, 0)]
                    for second in glyphs:
                        rec = row.Class2Record[c2.get(second, 0)]
                        v = getattr(rec.Value1, "XAdvance", 0) if rec.Value1 else 0
                        if v:
                            pairs.setdefault((first, second), v)
    return pairs


def bake(path):
    font = TTFont(path)
    upem = font["head"].unitsPerEm
    scale = 1000 / upem
    cap = font["OS/2"].sCapHeight * scale
    cmap = font.getBestCmap()
    gs = font.getGlyphSet()
    hmtx = font["hmtx"]
    glyphs, names = [], {}
    for ch in CHARS:
        name = cmap.get(ord(ch))
        if name is None:
            continue
        names[name] = ch
        pen = FlattenPen(gs)
        gs[name].draw(pen)
        contours = [[(x * scale, y * scale) for x, y in c] for c in pen.contours]
        verts, tris = triangulate(contours) if contours else ([], [])
        # Stored y down (baseline at 0, capitals reaching -cap).
        verts = [(x, -y) for x, y in verts]
        xs = [v[0] for v in verts]
        glyphs.append(dict(ch=ch, adv=round(hmtx[name][0] * scale), xmin=min(xs) if xs else 0,
                           xmax=max(xs) if xs else 0, verts=verts, tris=tris))
    kern = {(names[a], names[b]): round(v * scale) for (a, b), v in kerning(font, names).items()}
    return dict(cap=round(cap), glyphs=glyphs, kern=kern)


def pack(face):
    """Little-endian: glyph count, then per glyph: char, advance, ink min x, ink max x, vertex count,
    index count, vertices (x, y), indices; then kerning count and (left, right, adjustment) triples."""
    b = bytearray(struct.pack("<H", len(face["glyphs"])))
    for g in face["glyphs"]:
        b += struct.pack("<HhhhHH", ord(g["ch"]), g["adv"], g["xmin"], g["xmax"], len(g["verts"]), len(g["tris"]) * 3)
        for x, y in g["verts"]:
            b += struct.pack("<hh", x, y)
        for t in g["tris"]:
            b += struct.pack("<HHH", *t)
    b += struct.pack("<H", len(face["kern"]))
    for (l, r), v in sorted(face["kern"].items()):
        b += struct.pack("<HHh", ord(l), ord(r), v)
    return base64.b64encode(bytes(b)).decode("ascii")


def literal(s, width=110):
    lines = [s[i:i + width] for i in range(0, len(s), width)]
    return "\n".join(lines)


def main():
    bold, medium = bake(sys.argv[1]), bake(sys.argv[2])
    assert bold["cap"] == medium["cap"]
    code = f'''// Generated by tools/bake_font.py; do not edit.
// Glyphs of Montserrat Bold and Montserrat Medium, Copyright 2011 The Montserrat Project Authors
// (https://github.com/JulietaUla/Montserrat), licensed under the SIL Open Font License 1.1
// (shared/Fonts/OFL.txt). Outlines flattened and triangulated; units of 1/1000 em, y down.
namespace Beatform.Core;

internal static class TextFontData
{{
    public const int CapHeight = {bold["cap"]};

    // Base64 (whitespace is ignored when decoding); layout in tools/bake_font.py, pack().
    public const string Bold = @"
{literal(pack(bold))}";

    public const string Medium = @"
{literal(pack(medium))}";
}}
'''
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(code)
    n_tris = sum(len(g["tris"]) for g in bold["glyphs"])
    print(f"{len(bold['glyphs'])} glyphs, {n_tris} triangles (bold), {len(bold['kern'])} kerning pairs, "
          f"{os.path.getsize(OUT) // 1024} KB -> {os.path.normpath(OUT)}")
    if len(sys.argv) > 3:
        def metrics(face):
            return dict(cap=face["cap"], glyphs={g["ch"]: [g["adv"], g["xmin"], g["xmax"], bool(g["verts"])]
                                                 for g in face["glyphs"]},
                        kern={l + r: v for (l, r), v in face["kern"].items()})
        with open(sys.argv[3], "w", encoding="utf-8") as f:
            json.dump(dict(bold=metrics(bold), medium=metrics(medium)), f)


if __name__ == "__main__":
    main()
