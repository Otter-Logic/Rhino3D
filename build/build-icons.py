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


def readdataset():
    i = table(Icon(), 2, 14)
    return i.arrow((15.5, 12), (23, 12), 2, B, 4)


def writedataset():
    i = table(Icon(), 10, 22)
    return i.arrow((1, 12), (8.5, 12), 2, B, 4)


def splitbygroup():
    i = Icon()
    i.fill(rounded_rect(1.5, 8, 13.5, 16, 1.5), B)
    i.fill(rounded_rect(17.5, 8, 22.5, 16, 1.5), O)
    for x in (5, 10):
        i.dot(x, 12, 1.5, "#FFFFFF")
    i.dot(20, 12, 1.5, "#FFFFFF")
    i.stroke([(15.5, 3), (15.5, 21)], 1.5, K, dash=(1.6, 2.6))
    return i


def shapesignature():
    i = Icon()
    i.stroke([(3, 8), (6, 3), (13, 2.5), (20.5, 5), (18, 10), (11, 9), (7, 12.5)], 1.7, K, closed=True)
    for x, h in ((3.5, 5), (8.5, 2.5), (13.5, 6.5), (18.5, 4)):
        i.fill([(x, 22), (x + 3, 22), (x + 3, 22 - h), (x, 22 - h)], B)
    return i


def preparefeatures():
    i = Icon()
    for y, x in ((5.5, 8), (12, 16), (18.5, 11)):
        i.stroke([(3, y), (21, y)], 1.8, K)
        i.node(x, y, 2.5, B)
    return i


def principalcomponents():
    i = Icon()
    for x, y in ((5, 17), (7.5, 19.5), (8, 14), (11, 16.5), (10, 11), (14, 13), (13.5, 8), (17, 10), (16.5, 5.5), (19.5, 7)):
        i.dot(x, y, 1.15, G)
    i.arrow((12, 12), (7.2, 7.2), 1.6, O, 3)
    i.arrow((4.5, 19.5), (21, 3), 1.9, B, 3.8)
    return i


def _graph(i, nodes, edges, w=1.4, c=K):
    for a, b in edges:
        i.stroke([nodes[a], nodes[b]], w, c)
    return i


def neighbourgraph():
    nodes = [(4, 6), (12, 4), (20, 8), (7, 17), (16, 19), (12.5, 11.5)]
    i = _graph(Icon(), nodes, [(0, 1), (1, 5), (0, 5), (5, 2), (5, 3), (5, 4), (3, 4), (2, 4)])
    for x, y in nodes:
        i.node(x, y, 2.1, B)
    return i


def gaussianaffinity():
    i = Icon()
    a, b, c = (4.5, 18.5), (12, 5), (19.5, 18.5)
    i.stroke([a, c], 1.0, G, dash=(1.0, 2.2))
    i.stroke([b, c], 1.8, K)
    i.stroke([a, b], 3.4, B)
    for x, y in (a, b, c):
        i.node(x, y, 2.3, "#FFFFFF")
    return i


def datamap():
    i = axes(Icon())
    for x, y in ((8, 8), (11.5, 6.5), (11, 10.5)):
        i.dot(x, y, 1.7, B)
    for x, y in ((15, 16), (18.5, 14.5), (18, 18)):
        i.dot(x, y, 1.7, O)
    i.dot(13.5, 13, 1.4, G)
    return i


def shortestpaths():
    nodes = [(3.5, 18), (8, 7), (12, 15), (16, 5), (20.5, 12), (17, 20)]
    i = _graph(Icon(), nodes, [(0, 1), (1, 3), (3, 4), (2, 5), (5, 4), (1, 2)], 1.2, G)
    i.stroke([nodes[0], nodes[2], nodes[4]], 2.4, B)
    for k, (x, y) in enumerate(nodes):
        i.node(x, y, 2.0, B if k in (0, 2, 4) else "#FFFFFF")
    return i


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


def consensusclustering():
    i = Icon()
    for (x, y), c in (((12, 8.2), B), ((8.2, 14.8), O), ((15.8, 14.8), N)):
        i.ring(x, y, 5.6, 1.6, c)
    return i.dot(12, 12.6, 2.1, K)


def multiviewclustering():
    i = Icon()
    target = (19, 12)
    for y in (4.5, 12, 19.5):
        i.stroke([(7.5, y), target], 1.4, K)
    i.stroke([(2, 2), (7, 2), (7, 7), (2, 7)], 1.5, B, closed=True)
    i.ring(4.5, 12, 2.6, 1.5, O)
    i.stroke([(4.5, 16.8), (7.3, 21.8), (1.7, 21.8)], 1.5, N, closed=True)
    return i.node(*target, 3.2, B)


def messagepassing():
    i = Icon()
    centre = (12, 13)
    outer = [(3.5, 5), (20.5, 5), (12, 22)]
    for ox, oy in outer:
        d = math.hypot(centre[0] - ox, centre[1] - oy)
        ux, uy = (centre[0] - ox) / d, (centre[1] - oy) / d
        i.arrow((ox, oy), (centre[0] - ux * 4.2, centre[1] - uy * 4.2), 1.5, K, 3)
    for ox, oy in outer:
        i.node(ox, oy, 2.0, "#FFFFFF")
    return i.node(*centre, 3.2, B)


def clusterselector():
    i = axes(Icon())
    pts = [(6, 17.5), (9.5, 11), (13, 6), (16.5, 10.5), (20, 13)]
    i.stroke(pts, 1.7, K)
    for k, (x, y) in enumerate(pts):
        if k != 2:
            i.dot(x, y, 1.5, K)
    return i.node(13, 6, 2.8, B)


def refinelabels():
    i = Icon()
    for r, y in enumerate((5, 12, 19)):
        for c, x in enumerate((5, 12, 19)):
            if (r, c) == (1, 1):
                i.dot(x, y, 3.4, O).dot(x, y, 2.0, B)
            else:
                i.dot(x, y, 2.0, B if (r, c) not in ((2, 2), (1, 2), (2, 1)) else G)
    return i


def groupsignature():
    i = Icon()
    for y, x0, x1, c in ((4.5, 12, 21.5, B), (9.5, 4.5, 12, O), (14.5, 12, 17.5, B), (19.5, 8, 12, O)):
        i.fill([(x0, y - 1.7), (x1, y - 1.7), (x1, y + 1.7), (x0, y + 1.7)], c)
    return i.stroke([(12, 1.5), (12, 22.5)], 1.6, K)


def clusterquality():
    i = Icon()
    i.stroke(arc(12, 17, 9.5, 180, 360, 32), 2, K)
    for a in (180, 225, 270, 315, 360):
        r = math.radians(a)
        i.stroke([(12 + 7 * math.cos(r), 17 + 7 * math.sin(r)),
                  (12 + 8.2 * math.cos(r), 17 + 8.2 * math.sin(r))], 1.3, K)
    r = math.radians(305)
    i.stroke([(12, 17), (12 + 6.8 * math.cos(r), 17 + 6.8 * math.sin(r))], 2.2, B)
    return i.dot(12, 17, 2.2, B)


def clusteragreement():
    i = Icon()
    r, ax, bx, cy = 6.8, 8.6, 15.4, 12
    half = math.degrees(math.acos((bx - ax) / 2 / r))
    lens = arc(ax, cy, r, -half, half, 12) + arc(bx, cy, r, 180 - half, 180 + half, 12)
    i.fill(lens, B)
    return i.ring(ax, cy, r, 1.6, K).ring(bx, cy, r, 1.6, K)


def _fit(i, pts, line):
    axes(i)
    return i.stroke(line, 1.9, B), pts


def ridge():
    i, pts = _fit(Icon(), [(6, 16), (9, 17.5), (11, 12), (14, 13.5), (16.5, 8.5), (19.5, 9)], [(4.5, 17.5), (21, 8)])
    for x, y in pts:
        i.dot(x, y, 1.5, K)
    return i


def evaluateregression():
    line = [(4.5, 19.5), (21, 4)]
    slope = (line[1][1] - line[0][1]) / (line[1][0] - line[0][0])
    i, pts = _fit(Icon(), [(7, 12.5), (10, 18.5), (13, 7), (16, 12.5), (19, 3.5)], line)
    for x, y in pts:
        i.stroke([(x, y), (x, line[0][1] + slope * (x - line[0][0]))], 1.3, O)
    for x, y in pts:
        i.dot(x, y, 1.5, K)
    return i


def evaluateclassification():
    i = Icon()
    i.fill([(3, 3), (12, 3), (12, 12), (3, 12)], B).fill([(12, 12), (21, 12), (21, 21), (12, 21)], B)
    i.fill([(14.5, 5.5), (18.5, 5.5), (18.5, 9.5), (14.5, 9.5)], O)
    i.stroke([(3, 3), (21, 3), (21, 21), (3, 21)], 1.6, K, closed=True)
    return i.stroke([(12, 3), (12, 21)], 1.4, K).stroke([(3, 12), (21, 12)], 1.4, K)


def logistic():
    i = Icon()
    i.stroke([(2, 12), (22, 12)], 1.0, G, dash=(1.2, 2.2))
    i.stroke([(2 + 20 * k / 40, 19 - 14 / (1 + math.exp(-(k - 20) / 3.2))) for k in range(41)], 2, B)
    for x in (3.5, 7, 10.5):
        i.dot(x, 21.5, 1.4, K)
    for x in (13.5, 17, 20.5):
        i.dot(x, 2.5, 1.4, O)
    return i


def knnclassifier():
    i = Icon()
    i.ring(11.5, 12, 7.6, 1.3, K, dash=(1.4, 2.0))
    for x, y in ((7.5, 8.5), (15.5, 9)):
        i.dot(x, y, 1.8, B)
    i.dot(12.5, 17, 1.8, O)
    for x, y, c in ((21, 4, O), (21.5, 19.5, O), (2.5, 20.5, B)):
        i.dot(x, y, 1.5, c)
    return i.stroke([(9.7, 12), (13.3, 12)], 1.7, K).stroke([(11.5, 10.2), (11.5, 13.8)], 1.7, K)


def knnregressor():
    i = axes(Icon())
    i.stroke([(12.5, 4), (12.5, 21)], 1.2, K, dash=(1.2, 2.0))
    for x, y in ((5.5, 17.5), (19.5, 5.5)):
        i.dot(x, y, 1.4, G)
    for x, y in ((9, 14), (12, 8.5), (16, 9.5)):
        i.dot(x, y, 1.6, B)
    return i.node(12.5, 11.2, 2.3, "#FFFFFF")


def covariancetype():
    i = dropdown(Icon())
    return i.stroke(ellipse(8.5, 12, 5, 2.5, -30), 1.6, B, closed=True).dot(8.5, 12, 1.1, B)


def linkage():
    i = dropdown(Icon())
    i.stroke([(6, 12), (11.5, 12)], 1.5, K)
    return i.dot(5, 12, 2, B).dot(12.5, 12, 2, O)


def clusteringmodel():
    i = dropdown(Icon())
    return i.dot(5, 13.8, 1.7, B).dot(9, 9.8, 1.7, O).dot(12.5, 14, 1.7, N)


def neighbourweighting():
    i = dropdown(Icon())
    return i.dot(5.3, 12, 2.6, B).dot(10, 12, 1.7, B).dot(13.4, 12, 1.0, B)


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


ICONS = {f.__name__: f for f in (
    flattruss, branchpicker,
    readdataset, writedataset, splitbygroup, shapesignature,
    preparefeatures, principalcomponents, neighbourgraph, gaussianaffinity, datamap,
    shortestpaths, betweenness, cutvertices,
    kmeans, gaussianmixture, hdbscan, hierarchicalclustering, spectralclustering,
    consensusclustering, multiviewclustering, messagepassing, clusterselector,
    refinelabels, groupsignature, clusterquality, clusteragreement,
    ridge, logistic, knnclassifier, knnregressor, evaluateregression, evaluateclassification,
    covariancetype, linkage, clusteringmodel, neighbourweighting, unplacedpolicy,
    geometryqa, gridlevelinference, sixdofclassifier, structuralinsight,
    paneltypology, connectiontypology, jointsignature,
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
