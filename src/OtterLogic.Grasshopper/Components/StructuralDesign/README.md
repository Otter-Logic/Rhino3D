# Structural Design components

Tools that act on analysis results rather than producing geometry. Adaptors
only — the judgement lives in the
[StructuralDesign](https://github.com/Otter-Logic/StructuralDesign) repo,
`OtterLogic.StructuralDesign`; the clustering it runs on lives one layer down in
[Unsupervised](https://github.com/Otter-Logic/Unsupervised), and the feature
preparation below that in
[MachineLearning](https://github.com/Otter-Logic/MachineLearning).

- **Foundation Design Grouping** — column base nodes and their six forces under
  any number of load combinations in; the nodes grouped for design, each with its
  own envelope alongside — one set of seven forces per node, however many
  combinations. Fz is read with its sign and kept as its largest and smallest
  value, and a node in uplift under any combination never shares a group with
  one in compression throughout; the other five are designed either way and are
  enveloped by size.
- **Beam End Plate Design Grouping** — bars and the six forces at their ends,
  over any number of load combinations and either or both ends; the bars grouped
  for end plate design, each with its own envelope alongside. Types are cut so
  every bar carries at least the Efficiency input's share of its type's peak
  tension, |Fz| and |My| — 0.6 by default; the number of types follows from it.
  Fx is enveloped with its sign; the other five by size, so a bar's two ends,
  equal and opposite, read the same. See `StructuralDesign.BeamEndPlateGrouping`
  for why this is not the behaviour classifier the foundations use.

The two design grouping components carry the geometry through a grouping, for
the common case of "which of these can share a design". They share
`DesignGroupingComponent`, since they differ only in whether the geometry is a
point or a curve, which forces come back out, and which library call reads them
— `FoundationGrouping` or `BeamEndPlateGrouping`. The forces go in as six inputs,
Fx to Mz — a wire into Fz can only be Fz, where a branch of six per element could
be short or out of order and still look right.

Each input is one branch per element holding its values — every load
combination, and for a bar either or both ends — and a flat list is one value
each; if an analysis gives a branch per combination instead, Flip Matrix turns
it round. Out come seven outputs, each element's own envelope: for foundations
Fx, Fy, Fz Max, Fz Min, Mx, My, Mz, and for end plates Fx Max, Fx Min, Fy, Fz,
Mx, My, Mz. Every output is grouped exactly like the geometry — branch g, item i
belongs to item i of group g — so a value can be tagged at its element and pulled
out of a group without a List Item on the canvas. A group's design envelope is a
Bounds per branch of any output.

A design grouping has one rule a behaviour grouping does not: every element must
be designed, so every element lands in exactly one group. An element that fits no
behaviour family comes back as a group of its own at the end rather than filed
with its nearest family — usually it is the unusually loaded one, and grouping it
would either inflate that family's governing forces or under-design it. That rule
lives in `StructuralDesign.DesignGrouping`, not here.

## What earns a place here

A tool that carries a **structural** opinion — it knows what a bending moment is.
The design groupings qualify on every count: they expect columns that mean Fx,
Fy, Fz, Mx, My, Mz; they know which forces act either way and which one's sign
changes the design; and they know what governs a foundation or an end plate.
None of that is knowledge about clustering.

There was a **6DOF Behaviour Classifier** component here too, grouping members by
behaviour with no design reading on top. It was removed: the design groupings
answer the question a user here actually has, and the classifier's own groups
were not ones anybody would design to. The classifier itself stays in the
library, where foundation grouping is built on it.

Sibling to **Structural Form**, which generates a structure, where this answers
questions about one that already exists. Deflection surrogates, section sizers
and capacity classifiers land here as they arrive, sharing the same
demand-column feature extraction.

Whatever method a tool here uses lives under **Unsupervised Learning** (or its
sibling paradigm panels, as they arrive), and a user in
this panel should never need to go looking for it. That is the whole point of the
split: these are named for jobs, those are named for techniques.

Grasshopper only. Wire-data tools with no document-level shape, so nothing here
gets a Rhino command or a toolbar button.
