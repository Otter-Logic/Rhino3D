# Geometry QA — example data

A two-storey steel frame with slabs, and every kind of problem Geometry QA looks
for planted in it. Units are millimetres. Each file has a header row, then one
row per item.

| File | What it is |
|---|---|
| `lines.csv` | 80 lines as `x1,y1,z1,x2,y2,z2` |
| `surfaces.csv` | 7 slab panels, four corners each as `x1,y1,z1,…,x4,y4,z4` |
| `supports.csv` | 13 support points as `x,y,z` |

## Loading them in Grasshopper

```
Lines:    Read File ─> Cull Index 0 ─> Text Split "," ─> Number
          ─> List Item 0,1,2 ─> Construct Point ─┐
          ─> List Item 3,4,5 ─> Construct Point ─┴─> Line ─────────> Lines
Surfaces: … Number ─> four Construct Points (items 0–2, 3–5, 6–8, 9–11)
          ─> 4Point Surface ───────────────────────────────────────> Surfaces
Supports: … Number ─> List Item 0,1,2 ─> Construct Point ──────────> Supports
```

Wire all three into **Geometry QA** (Structural Design panel) and read it like a
model check in analysis software:

- **Issue Types** and **Counts** — what kinds of problem there are, most serious
  first, and how many of each.
- **Geometry** — one branch per issue type holding every element involved. A slider
  into the Path of a **Tree Branch** steps through the types; wire the branch into a
  Custom Preview in a strong colour to see every element with that problem at once.
  **Explode Tree** gives each type its own preview instead.
- **Locations** and **Findings** — the same branches, with a point and a sentence
  per finding, for tags.
- **Element Issues** — per element, what is wrong with it ("near miss; off level"),
  empty where nothing is. **Element Issue Count** is the same as a number, to colour
  the whole model by.
- **Summary** — the whole check in words, for a panel.

## The frame

A 4 × 3 grid at 8 m × 6 m, floors at 4.0 and 8.0 m, columns split at every floor,
beams both ways on both floors, a slab panel in every first-floor bay. On its own it
is clean: Geometry QA reports nothing.

## What is planted, and what you should see

Expected: **40 issues among 87 elements and 59 nodes, in 6 connected parts.** The
line numbers below are the row numbers in `lines.csv`, counting from 0.

| Planted | Found as |
|---|---|
| The upper column at X 24000, Y 12000 starts **12 mm above** the floor | **Near miss** — 12 from the floor node, but 20 m round along the structure (1,666×). Also **off level**. |
| The slab panel in the last bay stops **5 mm short** of its beam line | **Near misses** at both corners (3,199×) — and **ends bearing with no node**, since those corners rest on the beam 5 mm from its end |
| A first-floor beam on line C modelled **25 mm high** | **Near misses between parts** (touches nothing), a **separate part**, and **off level** |
| A mezzanine beam at 1.8 m resting on two **column faces** | **Ends bearing with no node** at both ends, and a **separate part** — to a solver it floats |
| A slab bay **split in two** panels whose shared corners land mid-beam | **Ends bearing with no node** on the beams and on the neighbouring panel's edge — a non-conforming mesh |
| **X-braces** in both end bays, uncrossed at their middles | **Unjoined crossings** — "one of 2 … a repeated pattern, often deliberate" |
| Two secondary roof beams crossing each other, resting on beams, with no nodes | **Unjoined crossing** — "the only unjoined Level–Level crossing"; **ends bearing**; each a **separate part** |
| A small frame floating 1 m clear of the building | **Separate part** — "no support, so on its own it is a mechanism" |
| A braced canopy held off the roof by **two cantilevers** | **Weakly attached** — "held to the rest by 2 elements — fewer than the 3 that meet at its own typical node" |
| A roof beam **drawn twice** | **Duplicate** |
| A roof beam drawn **over two others** | **Overlaps** with both |
| A line with **no length** | **No length** |
| A **3 mm sliver** on a column top | **Short element** — 2000× shorter than the elements it meets |
| A support at **no node** | **Support at no node** |

Nothing in the clean part of the frame is reported: the columns, beams and slabs
that are joined as they should be produce no issue at all.

## How it decides, without a rulebook

The only distance given is the document tolerance, and only to say when two points
*are* one node. Everything else is read from the model:

- **Near misses** are pairs that are *both* close in space for the elements around
  them *and* far apart along the structure — the detour ratio, route length over
  straight distance — or not connected at all. Connected neighbours in this model
  detour up to 7×; the near misses detour 1,666× and more.
- **Short elements** are on a smaller *scale* than the elements they meet, not
  merely shorter: columns two-thirds the length of their beams are ordinary, a 3 mm
  sliver is not.
- **Weak attachments** come from the lowest eigenvectors of the connectivity graph,
  swept for regions held by fewer elements than meet at their own typical node.
- **Crossings** are reported with whether the model repeats them — the model's own
  habit, not a rule about which crossings should be joined.

What it cannot know is intent. A deliberate movement joint is reported like an
accidental gap; the job is to show it, and the reading is yours.
