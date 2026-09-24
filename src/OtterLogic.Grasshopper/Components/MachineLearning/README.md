# Machine Learning components

One panel for all of machine learning, cut for the person who has samples and
wants an answer rather than for the person who wants to tune an algorithm.
Adaptors only: the clustering lives in
[Unsupervised](https://github.com/Otter-Logic/Unsupervised), the trainer and
the learners in [MachineLearning](https://github.com/Otter-Logic/MachineLearning),
the dataset contract in [Dataset](https://github.com/Otter-Logic/Dataset), and
every component here unpacks a tree, makes one call, and packs the answer back.
Getting the data ready — the table, the folder on disk, features from geometry —
is the [Dataset panel](../Dataset/README.md), one to the left.

| Tier (`GH_Exposure`) | Component | Library call |
|---|---|---|
| `primary` — the cores | **OtterCluster** | `ClusterRun.Fit` |
| | **OtterTrain** | `SampleTable.FromColumns`, `TrainerProcess.StartOnSamples`, `TrainerRuntime.Find` / `InstallAsync` / `InstallFromFile` |
| | **OtterPredict** | `OnnxModel.Load`, `OnnxModel.Predict` |
| `secondary` — cluster methods | **K-Means** | `new KMeansMethod` |
| | **Gaussian Mixture** | `new GaussianMixtureMethod` |
| | **HDBSCAN** | `new HdbscanMethod` |
| | **Spectral Clustering** | `new SpectralMethod` |
| | **Hierarchical Clustering** | `new HierarchicalMethod` |
| `tertiary` — learners | **Boosted Trees** | `new BoostedTreesLearner` |
| | **Neural Network** | `new NeuralNetworkLearner` |
| | **Linear Model** | `new LinearLearner` |
| | **Nearest Neighbours** | `new NearestNeighboursLearner` |
| `quarternary` — embedding methods | **Principal Components** | `new PrincipalComponentsMethod` |
| | **Multidimensional Scaling** | `new MultidimensionalScalingMethod` |
| | **Spectral Embedding** | `new SpectralEmbeddingMethod` |
| `quinary` — dropdowns | **Covariance Type**, **Linkage** | `EnumValueList`, in `Parameters/MachineLearning` |

**OtterEmbed** is the fourth core, added 2026-09: samples in — or a Graph, or
both — and one point per sample out, laid out so that alike sits near alike.
`EmbedRun.Fit` in MachineLearning standardises, lays out, and relates the axes
back to the columns. With nothing on Method, Data alone gets Multidimensional
Scaling and a Graph gets Spectral Embedding, with the reason in Report. The
embedding methods take the `quarternary` slot the data tier left empty, so no
surviving component's exposure moved. The Method wire is `GH_EmbeddingMethod`
over `OtterLogic.MachineLearning.Embedding.EmbeddingMethod`.

## A method on a wire

```
   Data ──────► OtterCluster ──► Labels, Groups, Confidence, Centres, Map, Unplaced, Report
   [K-Means] ─►

   Inputs ────► OtterTrain ────► model.onnx, Report ──► OtterPredict ──► Prediction, Confidence
   Target ────►                                                          Feature Names, Report
   [Boosted] ─►
```

The three cores take data and a method. A method component — K-Means, Boosted
Trees — has no data input at all: it takes its two or three settings and outputs
nothing but a **Method** or **Learner** wire (`GH_ClusterMethod`, `GH_Learner`
in `Types/`, carrying the immutable records the libraries define). This is the
Kangaroo pattern, goals into a solver, and it buys two things:

- **The zero-knowledge path is two wires.** With nothing on Method, OtterCluster
  fits K-Means, a Gaussian mixture and HDBSCAN and keeps the one the data
  supports, with the reason in Report (`AutoMethod` over `ClusterSelector`). With
  nothing on Learner, OtterTrain fits boosted trees. A first answer arrives before
  anything has been chosen.
- **Adding an algorithm never changes a core.** A new clustering is one record in
  Unsupervised and one small component under `Methods/`; a new learner is one
  record in MachineLearning, its Python counterpart, and one component under
  `Learners/`. OtterCluster and OtterTrain do not know which methods exist.

Each method component's description says what the algorithm assumes and which
other method to reach for when that does not hold, so the choice can be made from
the ribbon without looking anything up. Settings that are about the optimiser
rather than the data — restarts, seeds, tolerances — are not exposed; the records
carry sensible defaults.

## OtterCluster

Data in, one branch per sample; Method optional; **Standardise** on by default
because every method measures distances and a column in millimetres beside one
in radians is a fit that has only looked at the millimetres; **Map** off by
default because it costs a full distance matrix and is only for looking at.
`ClusterRun.Fit` does everything between the tree and the answer — standardise,
fit, read the centres back into input units, score the separation, describe what
sets each cluster apart — and its `Notes` come back as Remark and Warning
bubbles. Cluster Quality, Group Signature and Data Map folded into Report and Map,
because a grouping nobody can name or see is a grouping nobody acts on.

## OtterTrain

The rules, in the order a person meets them:

- **What is learned is decided by the Target wire.** Every item a number
  (`GH_Number` or `GH_Integer`) trains a number model; anything else is read as
  text and trains a class model. Classes are very often written 0 and 1, and as
  numbers those train a model that answers 0.37 — so write classes as text. The
  description says this out loud. Target accepts a flat list or one branch per
  sample.
- **Groups or random rows.** With Groups wired — Read Dataset's, or a job number
  per sample — whole groups are held out for the score, which is the honest way.
  Without them rows are held out at random and the score may be optimistic; a
  remark says so at the moment Run goes on, and the Report says so again.
  Requiring groups would have stopped every first-time user at a question they
  could not answer.
- **One `.onnx` out.** Under the wire, `TrainerProcess.StartOnSamples` writes a
  temporary dataset folder and runs the same Python trainer a folder would. Run
  is edge-driven — starts on the rising edge, cancels on the falling one, never
  restarts because something upstream changed — and the component re-solves every
  half second to show Status.
- **The runtime install.** When `TrainerRuntime.Find()` is null, Status says so
  and points at the right-click menu: *Install training runtime* downloads the
  release bundle on a background task (`InstallAsync`, progress into Status via
  scheduled re-solves, cancellable), *Install training runtime from file* takes a
  zip somebody already has (`InstallFromFile`), and *Where the runtime is looked
  for* shows `Describe()`. Nothing blocks the UI thread; the finish is marshalled
  back to it.

Read Dataset's outputs wire straight in: Features to Inputs, Targets to Target,
Groups to Groups, Feature Names to Feature Names.

## OtterPredict

The Predict component under a new name, same `ComponentGuid`. A file in, answers
out; with nothing but the file wired it says what the file expects. Everything it
needs is inside the `.onnx` — feature names, what it predicts, the classes, the
held-out score — so a model from a colleague works the same as one OtterTrain
wrote.

## What was deleted, and why not hidden

Three panels became one: Unsupervised Learning and Supervised Learning mirrored
the repos, which matters to the person writing the code and not to the person
using it, and left somebody who wanted to cluster something choosing between
panels before they had chosen a method.

Gone from the ribbon, kept as library code: the six raw clustering components,
Cluster Selector, Refine Labels, Message Passing, Consensus and Multi-View
Clustering, Cluster Agreement, Cluster Quality, Group Signature, Data Map,
Prepare Features, Principal Components, Neighbour Graph, Gaussian Affinity; the
four solve-time supervised methods and both Evaluate components (their
algorithms return as learners in the Python trainer, so "one `.onnx` out" holds
for every learner, and Rhino3D no longer references OtterLogic.Supervised);
Split By Group (OtterTrain holds out its own groups); the Clustering Model,
Model Type and Neighbour Weighting dropdowns.

Deleted rather than hidden. The `GH_Exposure.hidden` plus `[Obsolete]`
convention exists to keep distributed definitions opening, and OtterLogic has
none; a hidden component still turns up in the double-click search, which would
have undercut the whole cut. Once anything ships, hide-and-obsolete is back in
force for it.

Grasshopper only. Wire-data tools with no document-level shape, so nothing here
gets a Rhino command or a toolbar button.
