# Structural Design components

Multipurpose tools for structural engineering. Adaptors only — the engines live
in the [StructuralDesign](https://github.com/Otter-Logic/StructuralDesign) repo,
`OtterLogic.StructuralDesign`; the clustering they run on lives one layer down in
[Unsupervised](https://github.com/Otter-Logic/Unsupervised), and the feature
preparation and graphs below that in
[MachineLearning](https://github.com/Otter-Logic/MachineLearning).

- **6DOF Behaviour Classifier** — six lists in, Fx to Mz, one value per element;
  the elements grouped by behaviour out, with each group's centre, minimum and
  maximum in the units that came in. It fits K-Means, a Gaussian Mixture and
  HDBSCAN and picks the one the data supports. Nothing in it knows whether the six
  values are member end forces, support reactions or displacements: the envelope
  over combinations, forces taken by size, parts that may never share a group are
  all prepared upstream in the user's own definition. Plug **Unassigned** into its
  Unassigned input for what happens to elements that fit no group — leave them at
  -1, give each a group of its own, or file them with the nearest.
- **Structural Insight Engine** — lines, surfaces (Breps, surfaces, meshes — one
  element per face) and supports in; the natural groups of the structure the
  geometry describes out, with each element's agreement between views, every
  view's own grouping, the raw features and element graph, and QA issues:
  duplicates, degenerate elements, isolated and disconnected pieces, no route to a
  support, free ends, single elements holding a large part on, near misses,
  outliers and low agreement. No structural type is hard-coded — frames, shells,
  bridges, stadium bowls and gridshells go through the same engine, and groups
  are described by what was measured, never named. The model is read as physical
  members before it is grouped, and from the supports up: **Level** is how many
  hand-overs stand between an element and the ground, **Hierarchy** sorts the
  groups within the levels in branches {level; group}, and **Member**,
  **Assembly** and **Flow** are what those were read from. These outputs sit after
  Report because outputs are wired by position — putting them ahead of it would
  have moved every saved wire.
- **Grid and Level Inference** — lines in; the levels and structural grid they
  imply, named, and the elements not quite on them out. Levels, grid directions and
  gridlines are each where the model's own values gather; only the names are
  conventions.
- **Geometry QA** — lines, surfaces and supports in; a first-pass health check
  before analysis out: near misses, ends resting with no node, separate and weakly
  attached parts, unjoined crossings, duplicates, overlaps, slivers and
  misalignment, each with the geometry to look at. Read as strictly as a solver
  reads it, and judged against the model's own habits rather than a rulebook.

Joint Signature and Connection Typology read joints with the same machinery but
sit in the **Fabrication** panel, because connection detailing is the fabricator's
question.

Features and Connectivity from the Insight Engine plug straight into the
Unsupervised Learning components, so a user can take the grouping further with
their own choices.

## What earns a place here

A tool that knows what structural data is — a stick model and its supports, six
degrees of freedom — without hard-coding what any one structure or job makes of
it. Tools that read foundations one way and end plates another were removed for
exactly that reason: each served one job, where these serve any job a user can
prepare the data for.

Whatever method a tool here uses lives under **Unsupervised Learning** (or its
sibling paradigm panels, as they arrive), and a user in this panel should never
need to go looking for it.

Grasshopper only. Wire-data tools with no document-level shape, so nothing here
gets a Rhino command or a toolbar button.
