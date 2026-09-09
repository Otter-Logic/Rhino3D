# Structural Design components

Tools that act on analysis results rather than producing geometry. Adaptors
only — the judgement lives in the
[Clustering Tool](https://github.com/Otter-Logic/Clustering_Tool) repo,
`OtterLogic.Clustering`, and the algorithms beneath it in
[MachineLearning](https://github.com/Otter-Logic/MachineLearning).

- **6DOF Behaviour Classifier** — one required input and no settings. Fits all
  three clustering methods over six-degree-of-freedom demand and picks the one
  the evidence supports, then says which and why.

## What earns a place here

A tool that carries a **structural** opinion — it knows what a bending moment is.
The 6DOF classifier qualifies on three counts: it expects columns that mean Fx,
Fy, Fz, Mx, My, Mz; it deliberately applies no log transform, because demand is
on a scale where the gap between two values carries the meaning; and it reduces
to three components because demand across six degrees of freedom is strongly
correlated. None of that is knowledge about clustering.

Sibling to **Structural Form**, which generates a structure, where this answers
questions about one that already exists. Deflection surrogates, section sizers
and capacity classifiers land here as they arrive, sharing the same
demand-column feature extraction.

Whatever method a tool here uses lives under **Machine Learning**, and a user in
this panel should never need to go looking for it. That is the whole point of the
split: these are named for jobs, those are named for techniques.

Grasshopper only. Wire-data tools with no document-level shape, so nothing here
gets a Rhino command or a toolbar button.
