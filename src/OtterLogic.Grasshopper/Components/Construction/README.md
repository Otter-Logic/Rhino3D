# Construction components

Building the structure once it is designed: the order its pieces go up in, and
seeing that order happen. Adaptors only — the sequencing lives in the
[Construction](https://github.com/Otter-Logic/Construction) repo,
`OtterLogic.Construction.ErectionSequence`; the reading of the model it orders
by — joints, members, assemblies, load paths — lives one layer down in
[StructuralEngine](https://github.com/Otter-Logic/StructuralEngine), shared
with the Structural Design and Fabrication tools, and its load paths drain
through [Graphs](https://github.com/Otter-Logic/Graphs).

- **Erection Sequence** — lines and supports in; the lines in the order they go
  up out, with each line's place in that order, the stage it belongs to, what it
  lands on, and its level in the hand-over hierarchy. Two rules, true of any
  erection: a piece is lifted only onto something already standing, and what
  carries goes up before what it carries. What carries what is read the way the
  Structural Insight Engine reads it — joints welded, members chained, load
  drained to the supports — so the sequence and the analysis never disagree.
  Among the pieces that could go next, the lowest goes first, then the earliest
  drawn. A piece no support reaches still goes up, last, from its lowest element,
  and is reported as **Unsupported** so the missing support can be added.
- **Drop Animation** — geometry, its order and a moment in time in; the pieces
  that have left the hook out, each where it is on its way down. Drag a slider
  from 0 to 1 and the pieces drop into place in order; wire **Stage** instead of
  **Order** to drop each stage as one. Any integer list is an order, so a
  sequence from anywhere plays the same way.

## Why the reading is shared

Deciding what stands before what can be lifted onto it is a claim about
structures in general, made from the same joints, members, assemblies and load
paths the Structural Insight Engine reads. That reading is held once, in
StructuralEngine, and both toolkits call it: two copies would drift, and a
sequence that disagreed with the analysis about what rests on what would be
worse than none.

Grasshopper only. The animation is a slider-driven wire, and a sequence with no
picture is a list, so neither gets a Rhino command or a toolbar button.
