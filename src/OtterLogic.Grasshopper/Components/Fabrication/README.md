# Fabrication components

Tools for making the structure. Adaptors only. Panel Typology's engine is in the
[Fabrication](https://github.com/Otter-Logic/Fabrication) repo; the connection
tools' engines are still in
[StructuralDesign](https://github.com/Otter-Logic/StructuralDesign), beside the
joint reading they share with the structural tools. The panel is cut by who
reaches for a tool, not by where its code lives.

- **Panel Typology** — surfaces and the largest rectangle a machine can cast in;
  the panels to make out, grouped per surface and per type. The surface is laid out
  in rows of that rectangle and cut where its edge runs through one, so a curve is
  followed as closely as it is drawn, a slope comes out as triangles, nothing
  crosses the outline and nothing is left bare but the joints. Where the cuts go is
  a shortest path across the places a cut could fall, and **Standardisation** is
  what prices it: hold to the machine's full panel and take a sliver at the end of
  each row, or share the leftover out and take fewer, bespoke sizes instead. The
  panels from every surface are grouped together, so a shape that turns up four
  times is one type made four times. Type Tolerance is the rationalisation lever:
  no two panels of a type differ by more than it.

- **Joint Signature** — lines, supports and optional per-line attributes in; every
  joint described by the same row of numbers out. The row does not change when a
  joint is moved, turned, mirrored, or drawn with its lines split differently, so
  joints that are the same connection have the same row wherever they are. Wire it
  into OtterCluster, under Machine Learning, to group joints your own way.
- **Connection Typology** — the same inputs; the connection types the model repeats,
  an exemplar joint of each to detail, what sets each type apart, and the one-offs.
  A type is whatever the model makes more than once — no catalogue of corners,
  splices or bases — and one-offs are often modelling errors.

Example data with the expected answers is in `examples/connection-typology`.
