# Unsupervised Learning components

The raw unsupervised methods, for somebody assembling their own pipeline. Every
setting exposed, no opinion about the data. Adaptors only — the algorithms live
in the [Unsupervised](https://github.com/Otter-Logic/Unsupervised) repo,
`OtterLogic.Unsupervised`, and the graph type and feature preparation they share
with every other paradigm one layer below that in
[MachineLearning](https://github.com/Otter-Logic/MachineLearning).

| Tier (`GH_Exposure`) | Component | Library call |
|---|---|---|
| `secondary` — graphs | **Neighbour Graph** | `WeightedGraph.NearestNeighbours` |
| | **Gaussian Affinity** | `Affinity.Gaussian` |
| `tertiary` — methods | **K-Means Clustering** | `KMeans.Fit` |
| | **Gaussian Mixture** | `GaussianMixture.Fit` |
| | **HDBSCAN Clustering** | `Hdbscan.Fit` |
| | **Spectral Clustering** | `SpectralClustering.Fit` — all three overloads |
| | **Hierarchical Clustering** | `HierarchicalClustering.Fit`, then `Cut` or `CutAtDistance` |
| | **Message Passing Clustering** | `MessagePassing.Fit` |
| `quarternary` — refinement | **Refine Labels** | `MessagePassing.Refine` |
| `quinary` — dropdowns | **Covariance Type**, **Linkage** | `EnumValueList`, in `Parameters/UnsupervisedLearning` |

`primary` is left free for dataset capture, when it arrives.

## Why a panel per paradigm

These used to sit under a single **Machine Learning** panel meant for every raw
method whatever its paradigm. They moved here because the person who wants a raw
method thinks of a clustering as an unsupervised question first, and the panel
should be where they look first — and because it mirrors the repos exactly, so a
component's panel says where its algorithm lives. **Machine Learning** stays
reserved for the steps every paradigm shares: dataset capture, feature
preparation, inference.

Moving a component between panels never breaks a saved definition — Grasshopper
serialises it by `ComponentGuid`, and none of those changed.

## Why the design groupings are not here

Foundation and Beam End Plate Design Grouping are named for a job and carry a
structural opinion — they know the columns are forces and moments, and what
governs each design — so they live under **Structural Design**, where a
structural engineer with analysis results looks for a job rather than a method.
Anyone wanting to reproduce or vary what they do wires these components up
themselves.

## Shape

**Training Inputs** — one branch per sample, following LunchBoxML, so a definition
already wired for those components can be retargeted here without rebuilding the
data.

**Connectivity** — one branch per sample, listing the indices of the samples it
connects to. That is exactly what Grasshopper's own Proximity 3D puts out on its
Links output, and what Neighbour Graph and Gaussian Affinity put out, so native
neighbour-finding wires straight in. **Weights** is optional and matches
Connectivity item for item. An edge listed from both ends counts once.

Branch position is the sample index everywhere. Ragged or miscounted trees are
refused with an error rather than repaired, because a quiet repair ties results
to the wrong samples.

Every method numbers its clusters largest first — Gaussian Mixture by mixing
weight, the rest by count — so a small upstream change does not shuffle every
colour downstream. The exception is Refine Labels, which keeps the numbers it was
given, since it adjusts a labelling somebody already holds.

Grasshopper only. Wire-data tools with no document-level shape, so nothing here
gets a Rhino command or a toolbar button: the `.rhp` stays out of it.
