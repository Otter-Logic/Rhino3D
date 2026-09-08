# Machine Learning components

The clustering methods in their raw form, one component each, plus dataset
capture and ONNX inference to come. Adaptors only — logic lives in the
[MachineLearning](https://github.com/Otter-Logic/MachineLearning) repo,
`OtterLogic.MachineLearning`.

- **K-Means Clustering** — hard partition into a fixed number of clusters.
- **Gaussian Mixture** — soft assignment, with a probability per group.
- **HDBSCAN Clustering** — density-based, finds its own cluster count, and can
  leave a sample unassigned.

They take a `Training Inputs` tree — one branch per sample — following
LunchBoxML, so a definition already wired for those components can be retargeted
here without rebuilding the data.

These expose everything on purpose. Somebody who wants the settings decided for
them should use **6DOF Behaviour Classifier**, which runs all three over
structural demand data and picks between them; these are for driving the methods
directly, or for reproducing what it does with your own choices.

Grasshopper only. Nothing here gets a Rhino command or a toolbar button: these
are wire-data tools with no document-level shape, so the `.rhp` stays out of it.
