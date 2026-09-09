# Clustering components

Grouping members by how they behave, from a table of numbers. Adaptors only —
the algorithms live in the
[MachineLearning](https://github.com/Otter-Logic/MachineLearning) repo,
`OtterLogic.MachineLearning`, and the judgement about which one to use lives in
the [Clustering Tool](https://github.com/Otter-Logic/Clustering_Tool) repo,
`OtterLogic.Clustering`.

- **6DOF Behaviour Classifier** — one required input and no settings. Fits all
  three methods below over six-degree-of-freedom demand and picks the one the
  evidence supports, then says which and why.
- **K-Means Clustering** — hard partition into a fixed number of clusters.
- **Gaussian Mixture** — soft assignment, with a probability per group.
- **HDBSCAN Clustering** — density-based, finds its own cluster count, and can
  leave a sample unassigned.

The raw three and the finished tool sit in one section on purpose. They are the
same job at two altitudes: the classifier for a structural engineer with
analysis results, who should not need an opinion about covariance shapes to find
out which members behave alike; the other three for somebody who wants to drive
the methods directly, or reproduce what the classifier does with their own
choices. Splitting them across two ribbon panels would only hide one half from
whoever went looking in the other.

The section is named for the technique and the classifier for the job it does.
That is deliberate: the section has to cover the raw methods too, and the
classifier is not general — it is specifically for six-degree-of-freedom demand
out of an analysis model.

They take a `Training Inputs` tree — one branch per sample — following
LunchBoxML, so a definition already wired for those components can be retargeted
here without rebuilding the data.

Grasshopper only. Wire-data tools with no document-level shape, so nothing here
gets a Rhino command or a toolbar button: the `.rhp` stays out of it.
