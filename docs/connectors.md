# The connectors

Added 2026-09-24. Five small components — eleven with their methods — whose
whole job is to let one panel feed another. Before them, nothing turned
geometry or a graph into a table, the structural reading was only reachable
through the Insight Engine's clustering, the machine learning panel could
cluster but not draw, and no label ever left the canvas. This file records what
was decided and why.

## What was missing

| From | To | Component | Panel, tier |
|---|---|---|---|
| any geometry | a table | **Describe Geometry** | Dataset, `tertiary` |
| a Graph | a table | **Node Features** | Graphs, `tertiary` |
| a structural model | a table | **Describe Member** | Structural Design, `primary` |
| a table or a Graph | points | **OtterEmbed** + Principal Components, Multidimensional Scaling, Spectral Embedding | Machine Learning, `primary` + `quarternary` |
| labels | the Rhino document | **Bake By Group**, **Write Attributes**, **Read Attributes** | Document, `primary` |

Each is a thin adaptor over one library call, placed by the skill's rule of
where code lives:

| Component | Library call | Repo | Why there |
|---|---|---|---|
| Describe Geometry | `GeometryDescription.Describe` | Core | geometry with no model in it; both front-ends want the same row |
| Node Features | `NodeFeatures.Of` | Graphs | a reading of a graph with no learning in it |
| Describe Member | `ModelReading.Read`, `ElementFeatures`, `MemberFeatures` | StructuralEngine | a reading of a model every structural toolkit would want the same |
| OtterEmbed | `EmbedRun.Fit`, `EmbeddingMethod` records, `SpectralEmbedding` | MachineLearning | decompositions; mechanisms two paradigms share |
| Bake By Group, Write/Read Attributes | `GroupLayers`, `GroupPalette`, `ObjectText` | Document | reading and writing the Rhino document |

## Two moves down

Two things already existed one layer too high and moved, each with no expected
test value changing:

- The element feature table (`InsightFeatures.Raw`, 22 columns) moved from
  StructuralDesign to `StructuralEngine.ElementFeatures`, along with the
  support-distance search. StructuralDesign now delegates to it. The trigger was
  the skill's rule: a second toolkit wanted the same table, and a table held in
  one toolkit's private class is a table the next toolkit copies.
- The eigen step of spectral clustering — the shifted operator, the solve, the
  random-walk scaling — moved from `Unsupervised.SpectralClustering` to
  `MachineLearning.Decomposition.SpectralEmbedding`. Spectral clustering is now
  that embedding followed by k-means, which is what it always was; the
  scikit-learn parity fixture holds it to the same eigenvalues and partition.

`ModelReading` is new: the engine's whole pipeline in one call, because the
Insight engine, the sequencer and the connection tools each wrote the same six
calls in the same order.

## Decisions worth knowing

- **Describe Geometry keeps every row the same width.** Zero where a measure
  does not apply, never a missing cell: a ragged table is one nothing downstream
  can read. `Kind` and `Dimension` say what each piece is. Position is off by
  default because a grouping by kind should not be able to tell identical parts
  apart, and it is a switch rather than a second component because it is one
  decision, not two tools.
- **Node Features estimates past 2,000 nodes**, the way Betweenness does, from
  evenly spread starting points — closeness by the Eppstein–Wang estimate, since
  distance is symmetric and every node collects what the sampled sources found.
  Even rather than random so the answer is the same on every solve.
- **OtterEmbed reads a wired graph's connections as present or absent.** A
  graph's weights are whatever its builder gave them — lengths from Graph From
  Lines, costs from a route — and a length read as a strength would pull the
  furthest-apart nodes closest together. Strengths are what the method builds
  itself from values. With nothing on Method, values get scaling and a graph
  gets spectral; principal components is never chosen for the user because its
  axes are the point of it.
- **Writing to the document happens on the rising edge.** Bake By Group and
  Write Attributes act when their boolean goes from off to on, and not on every
  re-solve while it stays on — so a toggle writes once and a Button writes once
  per press, and an upstream slider cannot bake the model forty times. Each
  write is one Rhino undo step.
- **Bake By Group is idempotent by layer path.** Baking twice adds to the layers
  that are there rather than making `OtterGroups::0 (1)`, because a re-solve
  that changed one label should not leave a second tree to reconcile. Colours
  step round the wheel by the golden angle, so any run of group numbers stays
  spread whatever the count turns out to be.

## Still open

- **Not yet opened in Grasshopper.** Every connector compiles against the
  Grasshopper API and its library half is under test — Graphs 104, MachineLearning
  160, Unsupervised 190, StructuralEngine 11, StructuralDesign 62 — but the
  adaptors have not been exercised on a canvas. Describe Geometry in particular
  reads RhinoCommon classes, so its numbers are only checked by reading the code.
- **Core has no test project.** `GeometryDescription` is the first Core code
  that would need Rhino booted to test; a Rhino.Inside test project on the
  StructuralForm pattern is the way to give it one.
- **Icons are placeholders** drawn by `build/build-icons.py`, like the rest.
