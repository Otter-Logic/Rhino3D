"""
Draws the placeholder component icons into assets/icons.

Every icon is defined once, as vector primitives on a 24-unit grid, and written
twice: an SVG master under assets/icons/svg for anyone who wants to refine it in
a drawing tool, and the 24px PNG that the csproj embeds. Curves are sampled into
polylines before either is written, so the two cannot disagree about a shape.

The PNG is rasterised at 16x and box-filtered down rather than drawn at 24px:
Pillow's own line drawing has no anti-aliasing and mitres badly at this size.

Hand-drawn artwork (otterlogic, trusstype, beaminfill, boxtruss, surfacegrid,
gridpattern, layerpicker) is not listed here and is never overwritten.

Usage:
    python build/build-icons.py              # writes assets/icons
    python build/build-icons.py --sheet x.png  # also a contact sheet for review

Needs Pillow. After changing a toolbar icon, run build/build-rui-icons.ps1.
"""
from __future__ import annotations

import argparse
import math
from pathlib import Path

from PIL import Image, ImageDraw

GRID = 24
SCALE = 16

K = "#000000"   # structure, axes, outlines
B = "#2473C0"   # the thing the component is about
O = "#E8872B"   # the second group, or the thing being flagged
G = "#8C8C8C"   # context: noise, unselected, background edges
N = "#2E9E6B"   # a third group, where two are not enough


# --------------------------------------------------------------------------
# Geometry helpers — all return lists of (x, y) on the 24-unit grid, y down.
# --------------------------------------------------------------------------

def arc(cx, cy, r, a0, a1, n=24):
    """Angles in degrees, measured clockwise on screen from +x."""
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def ellipse(cx, cy, rx, ry, rot=0.0, n=36):
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        x, y = rx * math.cos(t), ry * math.sin(t)
        pts.append((cx + x * c - y * s, cy + x * s + y * c))
    return pts


def rounded_rect(x0, y0, x1, y1, r):
    return (arc(x1 - r, y0 + r, r, -90, 0, 6) + arc(x1 - r, y1 - r, r, 0, 90, 6)
            + arc(x0 + r, y1 - r, r, 90, 180, 6) + arc(x0 + r, y0 + r, r, 180, 270, 6))


def bell(cx, base, height, sigma, x0, x1, n=28):
    return [(x, base - height * math.exp(-((x - cx) ** 2) / (2 * sigma ** 2)))
            for x in (x0 + (x1 - x0) * i / n for i in range(n + 1))]


def split_dashes(pts, closed, on, off):
    """Walk a polyline and cut it into the 'on' runs of a dash pattern."""
    if closed:
        pts = pts + [pts[0]]
    runs, run, drawing, left = [], [pts[0]], True, on
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        seg = math.hypot(bx - ax, by - ay)
        pos = 0.0
        while seg - pos > left:
            pos += left
            p = (ax + (bx - ax) * pos / seg, ay + (by - ay) * pos / seg)
            if drawing:
                run.append(p)
                runs.append(run)
            run, drawing = [p], not drawing
            left = on if drawing else off
        left -= seg - pos
        if drawing:
            run.append((bx, by))
    if drawing and len(run) > 1:
        runs.append(run)
    return runs


# --------------------------------------------------------------------------
# An icon is an ordered list of primitives, painted back to front.
# --------------------------------------------------------------------------

class Icon:
    def __init__(self):
        self.ops = []

    def stroke(self, pts, w=2.0, c=K, closed=False, dash=None):
        self.ops.append(("stroke", list(pts), w, c, closed, dash))
        return self

    def fill(self, pts, c=K):
        self.ops.append(("fill", list(pts), c))
        return self

    def dot(self, x, y, r=1.6, c=K):
        self.ops.append(("dot", x, y, r, c))
        return self

    def ring(self, x, y, r, w=1.5, c=K, dash=None):
        return self.stroke(ellipse(x, y, r, r), w, c, closed=True, dash=dash)

    def node(self, x, y, r=2.2, c=B, w=1.2):
        """A filled dot with a black rim — reads as a graph node on any canvas colour."""
        return self.dot(x, y, r + w / 2, K).dot(x, y, r - w / 2, c)

    def arrow(self, p0, p1, w=1.8, c=K, head=3.4):
        (ax, ay), (bx, by) = p0, p1
        d = math.hypot(bx - ax, by - ay)
        ux, uy = (bx - ax) / d, (by - ay) / d
        neck = (bx - ux * head * 0.85, by - uy * head * 0.85)
        self.stroke([p0, neck], w, c)
        hw = head * 0.62
        base = (bx - ux * head, by - uy * head)
        return self.fill([(bx, by), (base[0] - uy * hw, base[1] + ux * hw),
                          (base[0] + uy * hw, base[1] - ux * hw)], c)

    # ---- raster ----------------------------------------------------------

    def png(self):
        size = GRID * SCALE
        img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        s = lambda p: (p[0] * SCALE, p[1] * SCALE)

        def cap(p, r, c):
            x, y = s(p)
            d.ellipse([x - r * SCALE, y - r * SCALE, x + r * SCALE, y + r * SCALE], fill=c)

        def run(pts, w, c):
            for a, b in zip(pts, pts[1:]):
                dx, dy = b[0] - a[0], b[1] - a[1]
                n = math.hypot(dx, dy)
                if n == 0:
                    continue
                ox, oy = -dy / n * w / 2, dx / n * w / 2
                d.polygon([s((a[0] + ox, a[1] + oy)), s((b[0] + ox, b[1] + oy)),
                           s((b[0] - ox, b[1] - oy)), s((a[0] - ox, a[1] - oy))], fill=c)
            for p in pts:
                cap(p, w / 2, c)

        for op in self.ops:
            if op[0] == "stroke":
                _, pts, w, c, closed, dash = op
                if dash:
                    for r in split_dashes(pts, closed, *dash):
                        run(r, w, c)
                else:
                    run(pts + [pts[0]] if closed else pts, w, c)
            elif op[0] == "fill":
                d.polygon([s(p) for p in op[1]], fill=op[2])
            else:
                _, x, y, r, c = op
                cap((x, y), r, c)

        # Premultiplied, or the transparent black bleeds into coloured edges.
        return img.convert("RGBa").resize((GRID, GRID), Image.BOX).convert("RGBA")

    # ---- vector ----------------------------------------------------------

    def svg(self):
        f = lambda v: f"{v:.2f}".rstrip("0").rstrip(".")
        path = lambda pts: " ".join(f"{f(x)},{f(y)}" for x, y in pts)
        out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {GRID} {GRID}" '
               f'width="{GRID}" height="{GRID}">']
        for op in self.ops:
            if op[0] == "stroke":
                _, pts, w, c, closed, dash = op
                tag = "polygon" if closed else "polyline"
                extra = f' stroke-dasharray="{f(dash[0])} {f(dash[1])}"' if dash else ""
                out.append(f'  <{tag} points="{path(pts)}" fill="none" stroke="{c}" '
                           f'stroke-width="{f(w)}" stroke-linecap="round" '
                           f'stroke-linejoin="round"{extra}/>')
            elif op[0] == "fill":
                out.append(f'  <polygon points="{path(op[1])}" fill="{op[2]}"/>')
            else:
                _, x, y, r, c = op
                out.append(f'  <circle cx="{f(x)}" cy="{f(y)}" r="{f(r)}" fill="{c}"/>')
        out.append("</svg>")
        return "\n".join(out) + "\n"


# --------------------------------------------------------------------------
# Shared motifs
# --------------------------------------------------------------------------

def axes(i, c=K):
    return i.stroke([(3, 3), (3, 21), (21, 21)], 1.6, c)


def dropdown(i):
    """The frame every enum value list shares, so they read as one family."""
    i.stroke(rounded_rect(1.5, 5.5, 22.5, 18.5, 2), 1.5, K, closed=True)
    i.stroke([(16.9, 10.9), (19, 13.3), (21.1, 10.9)], 1.5, K)
    return i


def table(i, x0, x1):
    i.fill([(x0, 4), (x1, 4), (x1, 8), (x0, 8)], K)
    i.stroke([(x0, 4), (x1, 4), (x1, 20), (x0, 20)], 1.5, K, closed=True)
    mid = (x0 + x1) / 2
    i.stroke([(mid, 8), (mid, 20)], 1.2, K)
    for y in (12, 16):
        i.stroke([(x0, y), (x1, y)], 1.2, K)
    return i


# --------------------------------------------------------------------------
# The icons
# --------------------------------------------------------------------------

def gridcolumns():
    """A grid seen in perspective, in grey, with a column standing up from every
    crossing. The crossings are the dots: they are what the tool finds."""
    i = Icon()
    for y in (15, 21):
        i.stroke([(2, y), (22, y)], 1.2, G)
    i.stroke([(6, 21), (10, 15)], 1.2, G).stroke([(14, 21), (18, 15)], 1.2, G)
    for x, y in ((6, 21), (14, 21), (10, 15), (18, 15)):
        i.stroke([(x, y), (x, y - 11)], 2.0, B)
        i.dot(x, y, 1.5, K)
    return i


def gridbeams():
    """Grid Columns' grid and columns, faint, with the beams between the column
    heads drawn heavy: pieces of gridline between columns, and nothing past
    the last one."""
    i = Icon()
    for y in (15, 21):
        i.stroke([(2, y), (22, y)], 1.0, G)
    i.stroke([(6, 21), (10, 15)], 1.0, G).stroke([(14, 21), (18, 15)], 1.0, G)
    heads = ((6, 13), (14, 13), (10, 7), (18, 7))
    for (x, y) in heads:
        i.stroke([(x, y + 8), (x, y)], 1.2, G)
    i.stroke([(6, 13), (14, 13)], 2.2, B).stroke([(10, 7), (18, 7)], 2.2, B)
    i.stroke([(6, 13), (10, 7)], 2.2, B).stroke([(14, 13), (18, 7)], 2.2, B)
    for (x, y) in heads:
        i.dot(x, y, 1.5, K)
    return i


def rectangulargrid():
    """Gridlines both ways at unequal bays, running on past the outer ones, with
    a node at every crossing. The unequal bays are the point: Rhino's own array
    cannot make them, and a square grid would read as the CPlane grid."""
    i = Icon()
    xs = (5, 11, 19)     # a 6 then an 8
    ys = (5, 12, 17)     # a 7 then a 5
    for x in xs:
        i.stroke([(x, 2), (x, 21)], 1.2, G)
    for y in ys:
        i.stroke([(2, y), (22, y)], 1.2, G)
    for x in xs:
        for y in ys:
            i.dot(x, y, 1.5, B)
    return i


def radialgrid():
    """A quarter of a radial grid from a centre at the bottom left: rays past
    two rings, a node at every crossing. A quarter rather than a circle because
    stopping short of the circle is what Rhino's polar array cannot do."""
    i = Icon()
    cx, cy = 4, 20
    for r in (8, 15):
        i.stroke(arc(cx, cy, r, -90, 0), 1.2, G)
    for a in (-90, -60, -30, 0):
        c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
        i.stroke([(cx, cy), (cx + 18 * c, cy + 18 * s)], 1.2, G)
        for r in (8, 15):
            i.dot(cx + r * c, cy + r * s, 1.5, B)
    i.dot(cx, cy, 1.5, K)
    return i


def spacetrusstype():
    """The dropdown frame with a pyramid in it: what runs between the layers of
    a space truss, which is what the list chooses."""
    i = dropdown(Icon())
    i.stroke([(4, 15.5), (14, 15.5)], 1.5, K)
    i.stroke([(4, 15.5), (9, 8.5), (14, 15.5)], 1.5, K)
    i.stroke([(9, 8.5), (9, 15.5)], 1.2, K)
    i.dot(9, 8.5, 1.6, B)
    return i


def flattruss():
    i = Icon()
    i.stroke([(2, 18), (7, 6), (12, 18), (17, 6), (22, 18)], 1.5, K)
    i.stroke([(2, 6), (22, 6)], 2, K).stroke([(2, 18), (22, 18)], 2, K)
    i.stroke([(2, 6), (2, 18)], 2, K).stroke([(22, 6), (22, 18)], 2, K)
    for x, y in ((7, 6), (17, 6), (12, 18)):
        i.node(x, y, 1.8, B)
    return i


def branchpicker():
    i = Icon()
    i.stroke([(4, 3), (4, 19), (8, 19)], 1.6, K)
    for y in (5, 12):
        i.stroke([(4, y), (8, y)], 1.6, K)
    for y, picked in ((5, False), (12, True), (19, False)):
        box = [(9.5, y - 2.5), (14.5, y - 2.5), (14.5, y + 2.5), (9.5, y + 2.5)]
        if picked:
            i.fill(box, B).stroke(box, 1.4, B, closed=True)
            i.stroke([(10.6, y), (11.7, y + 1.2), (13.5, y - 1.2)], 1.2, "#FFFFFF")
        else:
            i.stroke(box, 1.4, K, closed=True)
        i.stroke([(17.5, y), (21.5, y)], 1.8, B if picked else G)
    return i


def datatable():
    i = table(Icon(), 2, 22)
    return i.fill([(12.6, 12.6), (21.4, 12.6), (21.4, 15.4), (12.6, 15.4)], B)


def readdataset():
    i = table(Icon(), 2, 14)
    return i.arrow((15.5, 12), (23, 12), 2, B, 4)


def writedataset():
    i = table(Icon(), 10, 22)
    return i.arrow((1, 12), (8.5, 12), 2, B, 4)


def shapesignature():
    i = Icon()
    i.stroke([(3, 8), (6, 3), (13, 2.5), (20.5, 5), (18, 10), (11, 9), (7, 12.5)], 1.7, K, closed=True)
    for x, h in ((3.5, 5), (8.5, 2.5), (13.5, 6.5), (18.5, 4)):
        i.fill([(x, 22), (x + 3, 22), (x + 3, 22 - h), (x, 22 - h)], B)
    return i


def _graph(i, nodes, edges, w=1.4, c=K):
    for a, b in edges:
        i.stroke([nodes[a], nodes[b]], w, c)
    return i


def shortestpaths():
    nodes = [(3.5, 18), (8, 7), (12, 15), (16, 5), (20.5, 12), (17, 20)]
    i = _graph(Icon(), nodes, [(0, 1), (1, 3), (3, 4), (2, 5), (5, 4), (1, 2)], 1.2, G)
    i.stroke([nodes[0], nodes[2], nodes[4]], 2.4, B)
    for k, (x, y) in enumerate(nodes):
        i.node(x, y, 2.0, B if k in (0, 2, 4) else "#FFFFFF")
    return i


def astar():
    i = Icon()
    a, b = (4, 18.5), (20, 5.5)
    # What was looked at: a lobe stretched towards the target, not a disc round the source.
    i.fill(ellipse(11.5, 12.5, 11.0, 4.6, rot=-39), "#CFE2F6")
    for x, y in ((9, 18), (8, 11.5), (14.5, 14.5), (13, 7.5), (17.5, 10.5)):
        i.dot(x, y, 1.0, G)
    i.stroke([a, (9.5, 13.5), (15, 9.5), b], 2.2, B)
    i.node(*a, 2.1, B)
    i.node(*b, 2.1, O)
    # The star.
    cx, cy, ro, ri = 5.5, 6.0, 3.6, 1.5
    star = []
    for k in range(10):
        r = ro if k % 2 == 0 else ri
        t = math.radians(-90 + 36 * k)
        star.append((cx + r * math.cos(t), cy + r * math.sin(t)))
    return i.fill(star, O)


def betweenness():
    nodes = [(3.5, 5), (3.5, 19), (20.5, 5), (20.5, 19), (12, 12)]
    i = _graph(Icon(), nodes, [(0, 4), (1, 4), (2, 4), (3, 4), (0, 1), (2, 3)])
    for x, y in nodes[:4]:
        i.node(x, y, 1.9, "#FFFFFF")
    return i.node(12, 12, 3.6, B)


def cutvertices():
    nodes = [(3, 6), (3, 18), (8, 12), (12, 12), (16, 12), (21, 6), (21, 18)]
    i = _graph(Icon(), nodes, [(0, 1), (0, 2), (1, 2), (2, 3), (3, 4), (4, 5), (4, 6), (5, 6)])
    i.stroke([(12, 2), (12, 8)], 1.5, O, dash=(1.2, 2.2))
    i.stroke([(12, 16), (12, 22)], 1.5, O, dash=(1.2, 2.2))
    for k, (x, y) in enumerate(nodes):
        i.node(x, y, 2.6 if k == 3 else 1.8, O if k == 3 else "#FFFFFF")
    return i


def graph():
    nodes = [(5, 6), (18.5, 4.5), (12, 12), (4.5, 19), (19.5, 18.5)]
    i = _graph(Icon(), nodes, [(0, 1), (0, 2), (1, 2), (2, 3), (2, 4), (3, 4)], 1.6)
    for x, y in nodes:
        i.node(x, y, 2.3, B)
    return i


def _obstacle(i, x0, y0, x1, y1):
    box = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    i.fill(box, "#F3C9A0")
    return i.stroke(box, 1.3, O, closed=True)


def graphfrompoints():
    i = _obstacle(Icon(), 9, 8.5, 15, 15.5)
    nodes = [(4, 4.5), (12, 3.5), (20, 5), (3.5, 12.5), (20.5, 12), (5, 20), (12.5, 20.5), (20, 19.5)]
    _graph(i, nodes, [(0, 1), (1, 2), (0, 3), (2, 4), (3, 5), (4, 7), (5, 6), (6, 7)], 1.3)
    for x, y in nodes:
        i.node(x, y, 1.7, B)
    return i


def visibilitygraph():
    i = _obstacle(Icon(), 8.5, 8, 15.5, 16)
    a, b = (2.8, 13), (21.2, 11)
    corners = [(8.5, 8), (15.5, 8), (15.5, 16), (8.5, 16)]
    for c in (corners[0], corners[3]):
        i.stroke([a, c], 1.0, G)
    for c in (corners[1], corners[2]):
        i.stroke([b, c], 1.0, G)
    i.stroke([a, corners[0], corners[1], b], 2.2, B)
    for x, y in corners:
        i.node(x, y, 1.5, "#FFFFFF")
    i.node(*a, 2.1, B)
    return i.node(*b, 2.1, B)


def graphfromconnectivity():
    i = Icon()
    for y in (5, 12, 19):
        i.stroke([(2, y), (8, y)], 1.8, G)
    i.arrow((9.5, 12), (14, 12), 1.6, K, 3.0)
    nodes = [(17, 4.5), (21, 12), (16.5, 19.5)]
    _graph(i, nodes, [(0, 1), (1, 2), (0, 2)], 1.4)
    for x, y in nodes:
        i.node(x, y, 1.9, B)
    return i


def deconstructgraph():
    i = Icon()
    nodes = [(3.5, 5), (8, 12), (3, 19.5)]
    _graph(i, nodes, [(0, 1), (1, 2), (0, 2)], 1.4)
    for x, y in nodes:
        i.node(x, y, 1.9, B)
    i.arrow((10.5, 12), (15, 12), 1.6, K, 3.0)
    i.stroke([(17, 5), (22, 8)], 1.8, K)
    i.stroke([(17, 17), (22, 20)], 1.8, K)
    i.node(19.5, 12.5, 1.7, O)
    return i


def breadthfirst():
    i = Icon()
    i.ring(12, 12, 9.5, 1.0, G, dash=(1.0, 2.2))
    i.ring(12, 12, 5.2, 1.0, G, dash=(1.0, 2.2))
    inner = [(12, 6.8), (16.6, 14.4), (7.4, 14.4)]
    outer = [(12, 2.5), (20.2, 16.8), (3.8, 16.8), (19.8, 6.6), (4.2, 6.6)]
    for x, y in inner:
        i.stroke([(12, 12), (x, y)], 1.3, K)
    for (a, b) in ((inner[0], outer[0]), (inner[1], outer[1]), (inner[2], outer[2]), (inner[0], outer[3]), (inner[0], outer[4])):
        i.stroke([a, b], 1.3, K)
    for x, y in outer:
        i.node(x, y, 1.6, N)
    for x, y in inner:
        i.node(x, y, 1.8, B)
    return i.node(12, 12, 2.3, O)


def connectedpieces():
    nodes = [(4, 5), (11, 4), (7.5, 11), (14, 17), (20.5, 13), (19, 20.5), (4.5, 19.5)]
    i = _graph(Icon(), nodes, [(0, 1), (1, 2), (0, 2), (3, 4), (4, 5), (3, 5)])
    for k, (x, y) in enumerate(nodes):
        i.node(x, y, 2.0, B if k < 3 else O if k < 6 else G)
    return i


def dependencylevels():
    i = Icon()
    for y in (5, 12, 19):
        i.stroke([(2, y), (22, y)], 1.0, G, dash=(1.0, 2.2))
    top, mid, low = [(12, 5)], [(6.5, 12), (17.5, 12)], [(4, 19), (12, 19), (20, 19)]
    for a, b in ((top[0], mid[0]), (top[0], mid[1]), (mid[0], low[0]), (mid[0], low[1]), (mid[1], low[1]), (mid[1], low[2])):
        i.arrow(a, (b[0] + (a[0] - b[0]) * 0.28, b[1] + (a[1] - b[1]) * 0.28), 1.3, K, 2.6)
    for pts, c in ((top, O), (mid, B), (low, N)):
        for x, y in pts:
            i.node(x, y, 2.0, c)
    return i


def potentialflow():
    nodes = [(3.5, 12), (12, 5), (12, 19), (20.5, 12)]
    i = Icon()
    i.stroke([nodes[1], nodes[2]], 1.0, G)
    for a, b, w in ((0, 1, 3.0), (1, 3, 3.0), (0, 2, 1.6), (2, 3, 1.6)):
        i.stroke([nodes[a], nodes[b]], w, B)
    i.node(*nodes[0], 2.3, O)
    i.node(*nodes[1], 1.9, "#FFFFFF")
    i.node(*nodes[2], 1.9, "#FFFFFF")
    i.node(*nodes[3], 2.3, K)
    return i


def kmeans():
    i = Icon()
    for x, y in ((3.5, 6), (9, 3.5), (10.5, 10), (4, 11.5)):
        i.dot(x, y, 1.6, B)
    for x, y in ((14, 14), (20, 12.5), (20.5, 19), (14.5, 20.5)):
        i.dot(x, y, 1.6, O)
    for cx, cy in ((7, 7.5), (17.3, 16.5)):
        i.stroke([(cx - 1.8, cy - 1.8), (cx + 1.8, cy + 1.8)], 1.7, K)
        i.stroke([(cx - 1.8, cy + 1.8), (cx + 1.8, cy - 1.8)], 1.7, K)
    return i


def gaussianmixture():
    i = Icon()
    i.stroke(bell(8.5, 19.5, 14.5, 2.6, 2, 22), 1.9, B)
    i.stroke(bell(15.5, 19.5, 9.5, 3.0, 2, 22), 1.9, O)
    return i.stroke([(2, 20.5), (22, 20.5)], 1.6, K)


def hdbscan():
    i = Icon()
    i.stroke(ellipse(9, 9.5, 7, 5.6, -20), 1.3, K, closed=True, dash=(1.4, 2.0))
    for x, y in ((5.5, 10), (8, 7), (9, 11.5), (11.5, 8.5), (12.5, 11.5), (7.5, 9.5)):
        i.dot(x, y, 1.45, B)
    for x, y in ((17, 19), (19.5, 16.5), (20, 20)):
        i.dot(x, y, 1.45, O)
    for x, y in ((20, 4.5), (4.5, 20), (12, 19.5), (19, 10.5)):
        i.dot(x, y, 1.0, G)
    return i


def hierarchicalclustering():
    i = Icon()
    i.stroke([(4, 21), (4, 14), (9, 14), (9, 21)], 1.7, K)
    i.stroke([(15, 21), (15, 11), (20, 11), (20, 21)], 1.7, K)
    i.stroke([(6.5, 14), (6.5, 4), (17.5, 4), (17.5, 11)], 1.7, K)
    return i.stroke([(1.5, 8), (22.5, 8)], 1.5, B, dash=(1.6, 2.4))


def spectralclustering():
    i = Icon()
    i.stroke([(2, 8), (22, 8)], 1.0, G)
    i.stroke([(2 + 20 * k / 40, 8 - 5 * math.sin(2 * math.pi * k / 40)) for k in range(41)], 1.9, K)
    for k, x in enumerate((3.5, 7.5, 10.5, 13.5, 16.5, 20.5)):
        i.dot(x, 18.5, 1.7, B if k < 3 else O)
    return i


def covariancetype():
    i = dropdown(Icon())
    return i.stroke(ellipse(8.5, 12, 5, 2.5, -30), 1.6, B, closed=True).dot(8.5, 12, 1.1, B)


def linkage():
    i = dropdown(Icon())
    i.stroke([(6, 12), (11.5, 12)], 1.5, K)
    return i.dot(5, 12, 2, B).dot(12.5, 12, 2, O)


def unplacedpolicy():
    i = dropdown(Icon())
    i.dot(5, 12, 1.8, B)
    return i.ring(11.5, 12, 2.3, 1.2, O, dash=(0.9, 1.5))


def geometryqa():
    i = Icon()
    # A member that stops just short of the one it looks meant to rest on.
    i.stroke([(5.5, 13), (14.5, 13)], 1.9, K).stroke([(10, 5.5), (10, 9.4)], 1.9, K)
    i.dot(10, 11.2, 1.3, O)
    i.ring(10, 10, 7.2, 2, K)
    return i.stroke([(15.4, 15.4), (21, 21)], 3, K)


def gridlevelinference():
    i = Icon()
    for y in (12.5, 19.5):
        i.stroke([(1.5, y), (22.5, y)], 1.6, B)
    for x in (7, 17):
        i.stroke([(x, 7.2), (x, 22.5)], 1.3, K, dash=(2.2, 1.9))
        i.ring(x, 4.3, 2.8, 1.5, K)
    return i


def sixdofclassifier():
    i = Icon()
    o = (9, 15)
    i.arrow(o, (9, 1.5), 1.8, K, 3.8)
    i.arrow(o, (22.5, 15), 1.8, K, 3.8)
    i.arrow(o, (1.5, 22.5), 1.8, K, 3.8)
    # One rotation drawn, standing for the three: a turn about the upright axis.
    sweep = [(9 + 5.2 * math.cos(math.radians(a)), 8 + 2.2 * math.sin(math.radians(a))) for a in range(-55, 236, 10)]
    i.stroke(sweep, 1.5, B)
    (ax, ay), (bx, by) = sweep[-2], sweep[-1]
    d = math.hypot(bx - ax, by - ay)
    return i.arrow((bx, by), (bx + (bx - ax) / d * 2.8, by + (by - ay) / d * 2.8), 1.5, B, 3)


def structuralinsight():
    i = Icon()
    bulb = arc(12, 9.5, 7, 125, 415, 30)
    i.stroke([(9.6, 18)] + bulb + [(14.4, 18)], 1.8, K)
    i.stroke([(9.6, 18.2), (14.4, 18.2)], 1.8, K).stroke([(10.2, 21.3), (13.8, 21.3)], 1.8, K)
    i.stroke([(8.4, 12), (12, 5.6), (15.6, 12)], 1.5, B, closed=True)
    return i.stroke([(10.2, 8.8), (13.8, 8.8)], 1.3, B)


def paneltypology():
    i = Icon()
    kinds = ((B, O, B), (O, B, B))
    for r in range(2):
        for c in range(3):
            x0, y0 = 3.2 + c * 6.6, 4 + r * 8.4
            quad = [(x0, y0), (x0 + 5.6, y0), (x0 + 5.6, y0 + 7.4), (x0, y0 + 7.4)]
            i.fill([(x - (y - 12) * 0.22, y) for x, y in quad], kinds[r][c])
    return i


def _joint(i, centre, ends, w=2.2):
    for e in ends:
        i.stroke([centre, e], w, K)
    return i


def connectiontypology():
    c = (12, 13)
    i = _joint(Icon(), c, [(12, 2), (12, 22), (2, 13), (22, 13), (20.5, 4.5)])
    i.ring(*c, 5.6, 1.5, B, dash=(1.8, 2.0))
    return i.node(*c, 2.4, B)


def jointsignature():
    c = (5, 19)
    i = _joint(Icon(), c, [(5, 2.5), (21.5, 19), (18.5, 5.5)])
    i.stroke(arc(*c, 9, -45, 0, 10), 1.5, O)
    i.stroke(arc(*c, 6, -90, -45, 10), 1.5, B)
    return i.node(*c, 2.3, "#FFFFFF")


def erectionsequence():
    """A frame going up: what stands is black, the piece landing is blue, with the
    hook it comes down on. The dotted line is the ground."""
    i = Icon()
    i.stroke([(2, 21), (22, 21)], 1.0, G, dash=(1.0, 2.2))
    i.stroke([(6, 21), (6, 12)], 1.8, K).stroke([(16, 21), (16, 12)], 1.8, K)
    i.stroke([(6, 12), (16, 12)], 1.8, K)
    i.stroke([(6, 12), (6, 7)], 1.8, K).stroke([(16, 12), (16, 7)], 1.8, K)
    i.stroke([(6, 5), (16, 5)], 2.0, B)
    i.stroke([(11, 1.5), (11, 3.4)], 1.2, K)
    i.stroke(arc(11, 4.4, 1.0, 180, 360, 8), 1.2, K)
    i.arrow((20.5, 5), (20.5, 10.5), 1.3, O, 2.6)
    return i


def dropanimation():
    """A piece in the air on its way down to the outline waiting for it, with the
    slider that drives it along the bottom."""
    i = Icon()
    i.stroke([(5, 15), (19, 15)], 1.6, G, dash=(1.4, 1.6))
    i.stroke([(5, 6), (19, 6)], 2.2, B)
    for x in (9, 15):
        i.stroke([(x, 8.5), (x, 11.5)], 1.0, G)
    i.arrow((12, 8), (12, 13), 1.4, K, 2.8)
    i.stroke([(3, 20.5), (21, 20.5)], 1.2, K)
    i.node(11, 20.5, 2.0, O)
    return i


def _model(i, x0, y0, x1, y1):
    """A trained model: a chip holding a small network, the same on Train and Predict."""
    i.stroke(rounded_rect(x0, y0, x1, y1, 1.5), 1.5, K, closed=True)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    a, b, c = (cx - 3, cy + 2.5), (cx, cy - 2.5), (cx + 3, cy + 2.5)
    i.stroke([a, b, c], 1.1, K)
    return i.dot(*a, 1.3, B).dot(*b, 1.3, B).dot(*c, 1.3, B)


def ottercluster():
    """Samples in two colours inside one rounded outline — the otter's back — and
    one outside it: the unplaced sample the report will name."""
    i = Icon()
    i.stroke(rounded_rect(2.5, 4.5, 21.5, 19.5, 6), 1.5, K, closed=True)
    for x, y in ((6.5, 9), (9.5, 12.5), (6, 14.5)):
        i.dot(x, y, 1.7, B)
    for x, y in ((14.5, 8.5), (17.5, 11), (15, 14.5)):
        i.dot(x, y, 1.7, O)
    return i.dot(20.5, 21.5, 1.2, G)


def otterpredict():
    i = _model(Icon(), 6.5, 6, 17.5, 18)
    i.arrow((0.5, 12), (5.5, 12), 1.8, K, 3)
    return i.arrow((18.5, 12), (23.5, 12), 1.8, O, 3)


def ottertrain():
    i = table(Icon(), 1.5, 11.5)
    i.arrow((12.5, 12), (15, 12), 1.6, K, 2.6)
    return _model(i, 15.5, 6, 23, 18)


def _tree(i, x, top, bottom, c):
    """One little decision tree: a root and two leaves."""
    i.stroke([(x - 3, bottom), (x, top), (x + 3, bottom)], 1.3, K)
    return i.dot(x, top, 1.4, c).dot(x - 3, bottom, 1.2, c).dot(x + 3, bottom, 1.2, c)


def boostedtrees():
    """Three trees, each a little taller: boosting adds them one after another."""
    i = Icon()
    _tree(i, 5, 14, 19.5, G)
    _tree(i, 12, 10, 17, B)
    return _tree(i, 19, 5.5, 13.5, B)


def randomforest():
    """Many trees the same height, side by side: a forest votes, it does not build."""
    i = Icon()
    _tree(i, 4.5, 8, 15, B)
    _tree(i, 12, 8, 15, B)
    _tree(i, 19.5, 8, 15, B)
    return i.stroke([(2, 19.5), (22, 19.5)], 1.3, G)


def neuralnetwork():
    i = Icon()
    layers = [[(4, 8), (4, 16)], [(12, 5), (12, 12), (12, 19)], [(20, 8), (20, 16)]]
    for a, b in zip(layers, layers[1:]):
        for p in a:
            for q in b:
                i.stroke([p, q], 1.0, G)
    for k, layer in enumerate(layers):
        for x, y in layer:
            i.node(x, y, 1.9, B if k == 1 else "#FFFFFF")
    return i


def linearmodel():
    i = axes(Icon())
    i.stroke([(4.5, 17.5), (21, 8)], 1.9, B)
    for x, y in ((6, 16), (9, 17.5), (11, 12), (14, 13.5), (16.5, 8.5), (19.5, 9)):
        i.dot(x, y, 1.5, K)
    return i


def clustermethod():
    """The Method wire: a method's few settings, and the wire they leave on."""
    i = Icon()
    i.stroke(rounded_rect(2, 5, 15, 19, 2), 1.5, K, closed=True)
    i.dot(6, 10, 1.6, B).dot(11, 10, 1.6, O).dot(8.5, 14.5, 1.6, B)
    return i.arrow((15.5, 12), (23, 12), 1.8, K, 3)


def learner():
    """The Learner wire: the model chip a learner will fill, and the wire out."""
    i = _model(Icon(), 2, 6, 15, 18)
    return i.arrow((15.5, 12), (23, 12), 1.8, K, 3)


def otterpath():
    """A route through a few nodes inside one rounded outline — the otter's back —
    from a source to a target, the way OtterCluster's samples sit in theirs."""
    i = Icon()
    i.stroke(rounded_rect(2.5, 4.5, 21.5, 19.5, 6), 1.5, K, closed=True)
    for x, y in ((8, 8.5), (13, 15.5), (16.5, 8)):
        i.dot(x, y, 1.1, G)
    i.stroke([(5.5, 15.5), (9.5, 11.5), (14, 11), (18.5, 8.5)], 2.2, B)
    i.node(5.5, 15.5, 2.0, B)
    return i.node(18.5, 8.5, 2.0, O)


def graphmethod():
    """The Method wire of the Graphs panel: a small graph's few settings, and the wire they leave on."""
    i = Icon()
    i.stroke(rounded_rect(2, 5, 15, 19, 2), 1.5, K, closed=True)
    i.stroke([(5.5, 14.5), (8.5, 9), (11.5, 14.5)], 1.4, K)
    i.dot(5.5, 14.5, 1.5, B).dot(8.5, 9, 1.5, O).dot(11.5, 14.5, 1.5, B)
    return i.arrow((15.5, 12), (23, 12), 1.8, K, 3)


def graphfromlines():
    """Drawn lines whose ends meet: the lines in grey, the welded nodes in blue."""
    i = Icon()
    i.stroke([(3, 19), (9, 8), (16, 14), (21, 4)], 1.6, G)
    i.stroke([(9, 8), (16, 3)], 1.6, G)
    i.stroke([(16, 14), (21, 20)], 1.6, G)
    for x, y in ((3, 19), (9, 8), (16, 14), (21, 4), (16, 3), (21, 20)):
        i.node(x, y, 1.7, B)
    return i



def _rows(i, x0, x1, ys=(6, 12, 18), c=B):
    """Three rows of a table: the thing a describer hands on."""
    for y in ys:
        i.fill([(x0, y - 1.4), (x1, y - 1.4), (x1, y + 1.4), (x0, y + 1.4)], c)
    return i


def describegeometry():
    """A curve, an outline and a box on the left; the rows they become on the right."""
    i = Icon()
    i.stroke([(2, 6.5), (4.5, 3), (8.5, 5.5)], 1.7, G)
    i.stroke([(2, 9.5), (8.5, 9.5), (8.5, 14), (2, 14)], 1.4, G, closed=True)
    i.stroke([(2, 17.5), (7, 17.5), (7, 22), (2, 22)], 1.4, G, closed=True)
    i.stroke([(2, 17.5), (4, 16), (9, 16), (7, 17.5)], 1.1, G)
    i.stroke([(9, 16), (9, 20.5), (7, 22)], 1.1, G)
    i.arrow((10.5, 12), (13.5, 12), 1.5, K, 2.8)
    return _rows(i, 15.5, 22)


def nodefeatures():
    """A small graph on the left; one row per node on the right."""
    i = Icon()
    nodes = [(3.5, 5), (8, 12), (3, 19.5)]
    _graph(i, nodes, [(0, 1), (1, 2), (0, 2)], 1.4)
    for x, y in nodes:
        i.node(x, y, 1.9, B)
    i.arrow((10.5, 12), (13.5, 12), 1.5, K, 2.8)
    return _rows(i, 15.5, 22, ys=(5, 12, 19.5))


def describemember():
    """A column and a beam on the left; the rows they become on the right."""
    i = Icon()
    i.stroke([(3, 21), (3, 5)], 2.2, K)
    i.stroke([(3, 5), (10, 5)], 2.2, K)
    i.stroke([(6.5, 5), (6.5, 12)], 1.4, G)
    i.node(3, 5, 1.8, B)
    i.node(3, 21, 1.6, "#FFFFFF")
    i.arrow((11, 12), (13.5, 12), 1.5, K, 2.8)
    return _rows(i, 15.5, 22)


def otterembed():
    """Samples laid out along a curve inside one rounded outline — the otter's back."""
    i = Icon()
    i.stroke(rounded_rect(1.5, 3.5, 22.5, 20.5, 5), 1.6, K, closed=True)
    i.stroke([(4.5, 16), (8, 9), (12, 13), (16, 7), (19.5, 10)], 1.0, G, dash=(1.0, 1.6))
    for x, y in ((4.5, 16), (8, 9), (12, 13), (16, 7), (19.5, 10)):
        i.node(x, y, 1.9, B)
    return i


def principalcomponents():
    """A cloud stretched along one direction, with its long and short axes drawn."""
    i = Icon()
    for x, y in ((5, 17), (7.5, 14.5), (9, 16), (11, 12), (13, 13.5), (14.5, 9.5), (17, 8), (18.5, 10), (10, 9.5), (16, 12)):
        i.dot(x, y, 1.4, G)
    i.arrow((4, 19), (20.5, 5.5), 1.8, B, 3.4)
    i.arrow((10.5, 10.5), (14, 14.5), 1.6, O, 3.0)
    return i


def multidimensionalscaling():
    """Points and the distances between them, kept as they were."""
    i = Icon()
    pts = [(4.5, 18.5), (9.5, 5.5), (19, 9), (15, 18)]
    for a in range(len(pts)):
        for b in range(a + 1, len(pts)):
            i.stroke([pts[a], pts[b]], 1.0, G, dash=(1.0, 1.6))
    for x, y in pts:
        i.node(x, y, 2.1, B)
    return i


def spectralembedding():
    """Two knots of nodes joined by one connection, laid out apart."""
    i = Icon()
    left = [(4, 8), (8, 5), (8.5, 11), (4.5, 13)]
    right = [(15.5, 13), (19.5, 11), (20, 17), (16, 19.5)]
    for group in (left, right):
        for a in range(len(group)):
            for b in range(a + 1, len(group)):
                i.stroke([group[a], group[b]], 1.2, K)
    i.stroke([left[2], right[0]], 1.2, G, dash=(1.0, 1.4))
    for x, y in left:
        i.node(x, y, 1.7, B)
    for x, y in right:
        i.node(x, y, 1.7, O)
    return i


def embeddingmethod():
    """The Method wire of OtterEmbed: a little map's settings, and the wire they leave on."""
    i = Icon()
    i.stroke(rounded_rect(2, 4, 15, 20, 2), 1.5, K, closed=True)
    for x, y in ((5.5, 15.5), (8.5, 9), (11.5, 13)):
        i.node(x, y, 1.6, B)
    i.stroke([(15, 12), (22, 12)], 2.0, B)
    return i


def _layers(i, x0, x1, colours):
    """A stack of layer bars, one colour each."""
    for k, c in enumerate(colours):
        y = 6 + 5.5 * k
        i.fill([(x0, y - 1.6), (x1, y - 1.6), (x1, y + 1.6), (x0, y + 1.6)], c)
    return i


def bakebygroup():
    """Pieces falling onto a stack of coloured layers, one per group."""
    i = Icon()
    _layers(i, 9, 22, (B, O, N))
    i.arrow((4.5, 3.5), (4.5, 19), 1.8, K, 3.4)
    return i


def _tag(i, x0, y0, x1, y1):
    """A label tag: a rectangle with a notched end and two lines of text."""
    i.stroke([(x0, y0), (x1 - 3, y0), (x1, (y0 + y1) / 2), (x1 - 3, y1), (x0, y1)], 1.5, K, closed=True)
    for y in ((y0 + y1) / 2 - 2, (y0 + y1) / 2 + 2):
        i.stroke([(x0 + 2.5, y), (x1 - 5.5, y)], 1.3, B)
    return i


def writeattributes():
    """Text going onto a tag."""
    i = _tag(Icon(), 8, 6, 22.5, 18)
    return i.arrow((1.5, 12), (6.5, 12), 1.8, K, 3.2)


def readattributes():
    """Text coming off a tag."""
    i = _tag(Icon(), 1.5, 6, 16, 18)
    return i.arrow((17.5, 12), (22.5, 12), 1.8, K, 3.2)


ICONS = {f.__name__: f for f in (
    flattruss, gridcolumns, gridbeams, rectangulargrid, radialgrid, spacetrusstype, branchpicker,
    ottercluster, ottertrain, otterpredict,
    kmeans, gaussianmixture, hdbscan, spectralclustering, hierarchicalclustering,
    boostedtrees, randomforest, neuralnetwork, linearmodel,
    datatable, readdataset, writedataset, shapesignature,
    covariancetype, linkage, clustermethod, learner, unplacedpolicy,
    graph, graphfrompoints, graphfromlines, visibilitygraph, otterpath, graphmethod, graphfromconnectivity, deconstructgraph,
    shortestpaths, astar, breadthfirst, betweenness, cutvertices, connectedpieces, dependencylevels, potentialflow,
    geometryqa, gridlevelinference, sixdofclassifier, structuralinsight,
    paneltypology, connectiontypology, jointsignature,
    erectionsequence, dropanimation,
    describegeometry, nodefeatures, describemember,
    otterembed, principalcomponents, multidimensionalscaling, spectralembedding, embeddingmethod,
    bakebygroup, writeattributes, readattributes,
)}


def contact_sheet(rendered, path, cols=9, zoom=6):
    """Each icon at 6x beside itself at true size, on the two canvas greys Grasshopper uses."""
    cell = GRID * zoom + 16
    rows = math.ceil(len(rendered) / cols)
    sheet = Image.new("RGBA", (cols * cell, rows * (cell + GRID + 12)), "#D6D6D6")
    for k, (name, img) in enumerate(rendered.items()):
        x, y = (k % cols) * cell + 8, (k // cols) * (cell + GRID + 12) + 8
        big = img.resize((GRID * zoom, GRID * zoom), Image.NEAREST)
        sheet.paste(big, (x, y), big)
        sheet.paste(img, (x, y + GRID * zoom + 6), img)
        ImageDraw.Draw(sheet).text((x + GRID + 6, y + GRID * zoom + 10), name[:18], fill="#333333")
    sheet.save(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--sheet", help="also write a contact sheet PNG here, for review")
    parser.add_argument("--only", nargs="*", help="limit to these icon names")
    args = parser.parse_args()

    icon_dir = Path(__file__).resolve().parent.parent / "assets" / "icons"
    svg_dir = icon_dir / "svg"
    svg_dir.mkdir(exist_ok=True)

    rendered = {}
    for name, draw in ICONS.items():
        if args.only and name not in args.only:
            continue
        icon = draw()
        rendered[name] = icon.png()
        rendered[name].save(icon_dir / f"{name}.png", optimize=True)
        (svg_dir / f"{name}.svg").write_text(icon.svg(), encoding="utf-8")

    if args.sheet:
        contact_sheet(rendered, args.sheet)
    print(f"Wrote {len(rendered)} icons to {icon_dir}")


if __name__ == "__main__":
    main()
