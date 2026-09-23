# Connection Typology — example data

Two line models as plain text, with the answer each should give. Units are
millimetres. Every file has a header row, then one row per line or point.

| File | What it is |
|---|---|
| `frame_lines.csv` | A 3-storey steel frame, 93 lines as `x1,y1,z1,x2,y2,z2` |
| `frame_supports.csv` | Its 12 column bases as `x,y,z` |
| `truss_lines.csv` | A pitched Pratt roof truss, 33 lines |
| `truss_supports.csv` | Its 2 bearings |

## Loading them in Grasshopper

```
File Path ─> Read File ─> Cull Index (index 0, drops the header)
          ─> Text Split (separator ",")        one branch per row
          ─> Number                            text to numbers
          ─> List Item (indices 0,1,2) ─> Construct Point ─┐
          ─> List Item (indices 3,4,5) ─> Construct Point ─┴─> Line ─> Lines
```

Supports are the same, with one Construct Point and no Line. Wire both into
**Connection Typology** (Fabrication panel). Everything comes out grouped, one
branch per connection type and the one-offs as the last group:

- **Types** — each group's name and members in words: "Type 0 — 12 joints: 4 arms —
  1 plumb up, 1 plumb down, 2 level", …, "One-offs — 1 joint".
- **Joints** — the joint points of each group, `{type}`. A slider into the Path of a
  Tree Branch steps through the types; Explode Tree colours each its own.
- **Joint Lines** — the lines meeting at each joint, `{type;joint}`, as Joint
  Signature gives them.
- **Exemplars** — one point per type: the joint to detail on behalf of the rest.
- **Summary** — the whole typology in words, for a panel.

Also worth trying:

- **Joint Signature → OtterCluster** with Map on, coloured by type (Joint Index
  says which joints are in each type): each type is a point where all its joints
  sit on top of each other; the one-off sits alone.
- The same, with Feature Names wired, and read its **Report**: it says what sets
  each cluster of joints apart, in the signature's own column names.
- **Minimum Type Size** at 3 on the frame: the two brace pairs become one-offs
  (8 types, 5 one-offs), and only connections made three times or more remain
  types.

## The frame — what you should see

A 4 × 3 grid at 8 m × 6 m, floors at 4.0 and 8.0 m, roof at 12.3 m, columns
split at every floor, beams both ways on every floor. Planted in it:

- a four-beam **mezzanine** at 1.8 m, bearing on the faces of four columns;
- the ground-storey column at **X 16000, Y 6000 built 12 mm off grid** at its top,
  with the first-floor beams framing into it there — so the column above does not
  meet it;
- one first-floor beam on line C, bay 1–2, modelled **25 mm high**;
- two **braces**, one in bay 1–2 and its mirror image in bay 3–4.

Expected: **10 types and 1 one-off among 55 joints.**

| Type | Joints | Members, in words | What it is |
|---|---|---|---|
| 0 | 12 | 1 plumb up, 1 plumb down, 2 level | Floor corners (8) **and** the mezzanine corners (4) — the same geometry: a column passing a floor with two beams at right angles |
| 1 | 10 | 1 plumb up, supported | Column bases |
| 2 | 9 | 1 plumb up, 1 plumb down, 3 level | Floor edges — **near-identical variants**: one has a beam skewed 0.1° by the off-grid column |
| 3 | 6 | 1 plumb down, 3 level | Roof edges |
| 4 | 4 | 1 plumb down, 2 level | Roof corners |
| 5 | 3 | 1 plumb up, 1 plumb down, 1 level | **Should not exist.** The high beam: it misses the column top, so a corner lost a beam, and the beam's two ends bear on column faces 25 mm up |
| 6 | 3 | 1 plumb up, 1 plumb down, 4 level | Floor interiors |
| 7 | 3 | 1 plumb down, 4 level | Roof interiors (2) **and the off-grid column's top at 4.0 m** — a roof connection at a floor is the red flag |
| 8 | 2 | 1 plumb up, 1 pitched up, supported | Brace bases |
| 9 | 2 | 1 plumb up, 1 plumb down, 3 level, 1 pitched down | Brace tops — **1 left-, 1 right-handed**: the mirrored pair shares a type |

**One-off:** the joint at (16000, 6000, 4000) — 1 plumb up, 2 level: the upper
column's foot, standing on the middle of a beam 12 mm from where the column below
it ends.

Both planted errors show up without being looked for — as a type that should not
exist (5), a roof connection at a floor (7), and a one-off. Grid and Level
Inference, run on the same file, reports the same two errors from its own side:
"Column … is 12 off gridline 3" and "Line … is 25 above Level 02".

## The truss — what you should see

A 24 m span in 8 panels, 3.0 m deep at the ends rising to 4.5 m at midspan,
diagonals falling towards the middle. Its two halves are mirror images.

Expected: **8 types of 2 joints each, and 2 one-offs.**

- Every type is a **left–right pair**: the joint at 3 m from one end and the joint at
  3 m from the other, and so on along both chords.
- The **one-offs** are the two joints on the centreline — the apex (1 plumb down,
  2 level) and the midspan bottom-chord joint (1 plumb up, 2 level, 2 pitched up) —
  the only joints with no partner.
- **Handedness is 0 throughout**, and that is right. A flat truss's left-hand joint
  turned 180° about the vertical *is* its right-hand joint: the pair are not mirror
  images in three dimensions, so they need no left and right versions.

Worth noticing: types 1, 4 and 6 all read "1 plumb up, 2 level, 1 pitched up" —
the same members, but at different angles, because the truss deepens towards
midspan and every panel's diagonal is steeper than the last. The tool is telling
you there are eight joint geometries. Whether a fabricator treats them as one
detail with eight setting-out angles is a judgement for them — Joint Signature
into Hierarchical Clustering, cut at fewer groups, shows what that coarser
grouping would be.
