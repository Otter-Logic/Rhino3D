# Machine learning components: the layperson's cut

Decided 2026-09-23. Replaces the three learning panels (Machine Learning,
Unsupervised Learning, Supervised Learning) with one, and thirty-seven
components and dropdowns with sixteen. This file records what was decided and
why, so the next person who finds a decision inconvenient knows what it cost.

## Who this is for

The Rhino user base in AEC — engineers, fabricators, site teams — wants tools
that do a job. Most of them will only ever use the job-named panels
(Structural Design, Fabrication, Construction) and never see a method. The few
who train their own model want three things and no more: feed samples in, get
one `.onnx` file out, and use it to predict. They will look up what K-Means or
HDBSCAN or a GNN *is*; they will not learn how one is tuned.

The previous surface was cut for a different person — someone assembling a
pipeline by hand with every setting exposed. It had six clustering
components each with its own inputs and outputs, four supervised methods that
fitted at solve time and produced nothing to save, separate evaluate, quality,
signature and map components, two dataset splitters, and three panels to find
them in. Every one of those was a thing a layperson had to understand before
they could ignore it.

## The shape

```
                 ┌─────────────┐
   Data ───────► │ OtterCluster│ ──► Labels, Groups, Confidence, Centres, Map, Report
   [K-Means] ──► │             │
                 └─────────────┘
   K-Means, Gaussian Mixture, HDBSCAN, Spectral, Hierarchical ──► one "Method" wire

                 ┌─────────────┐                      ┌──────────────┐
   Inputs ─────► │ OtterTrain  │ ──► model.onnx ────► │ OtterPredict │ ──► Prediction, Confidence
   Target ─────► │             │     Report           │              │     Feature Names, Report
   [Boosted] ──► │             │                      └──────────────┘
                 └─────────────┘
   Boosted Trees, Neural Network, Linear Model, Nearest Neighbours ──► one "Learner" wire
```

Three core components take data and a method on a wire. A method component
has no data input at all — only its two or three settings — and outputs
nothing but that wire. This is the Kangaroo pattern, goals into a solver, and
it has two properties that matter here:

- **The zero-knowledge path is two wires.** With nothing wired to Method,
  OtterCluster fits K-Means, a Gaussian mixture and HDBSCAN and keeps the one
  the data supports, with the reason in Report. With nothing wired to
  Learner, OtterTrain fits boosted trees. A user gets a first answer before
  they have chosen anything.
- **Adding an algorithm never touches the core.** A new method is one record
  in the Unsupervised repo and one small component here; OtterCluster does not
  change. A graph neural network, when it arrives, is a learner that declares
  it needs a Graph wire rather than a table, and OtterTrain grows one input.

## The panel

One **Machine Learning** panel, ordered by `GH_Exposure`, which draws a divider
between tiers:

| Tier | Components |
|---|---|
| primary, the cores | OtterCluster, OtterTrain, OtterPredict |
| secondary, cluster methods | K-Means, Gaussian Mixture, HDBSCAN, Spectral Clustering, Hierarchical Clustering |
| tertiary, learners | Boosted Trees, Neural Network, Linear Model, Nearest Neighbours |
| quarternary, data | Write Dataset, Read Dataset, Shape Signature |
| quinary, dropdowns | Covariance Type, Linkage |

Graphs stays exactly as it was: its components are already named for what a
person looks up (Dijkstra, A*), and its user is not doing machine learning.

The panel-per-paradigm split was undone. It mirrored the repos, which is a
reason that matters to the person writing the code and not to the person using
it; and it left a user who wanted to cluster something choosing between three
panels before they had chosen a method.

## The cores

**OtterCluster** takes Data (one branch per sample), an optional Method, a
Standardise toggle that is on by default, and a Map toggle that is off. It
calls one library entry point, `ClusterRun.Fit` in Unsupervised, which
standardises, fits, reads the centres back into input units, scores the
separation, and describes what sets each cluster apart. Cluster Quality, Group
Signature and Data Map folded into it as the Report and Map outputs, because a
grouping nobody can name or see is a grouping nobody acts on, and a layperson
will not wire three more components to find out.

**OtterTrain** is the Train component with samples wired directly rather than a
folder path. It decides whether the target is a class or a number by the wire:
text is a class, numbers are a quantity, and the description says so — classes
are very often written 0 and 1, and a number wire of those trains a model that
answers 0.37. Groups are optional. With them, whole groups are held out for the
score, which is the honest way; without them, rows are held out at random and
the Report says the score may be optimistic. Requiring groups would have been
more honest and would have stopped every first-time user at a question they
could not answer. It always writes one `.onnx`: one runtime requirement, one
file type, one story.

Under the wire, OtterTrain writes a temporary dataset folder and runs the same
trainer process a folder would. Write Dataset and Read Dataset stay for the
person gathering data across months, and Read Dataset's outputs wire straight
into OtterTrain.

**OtterPredict** is the Predict component renamed. It already had the right
shape: a file in, answers out, and with nothing but the file wired it says what
the file expects.

## What was deleted, and why not hidden

Grasshopper keeps a saved definition working across renames and panel moves by
`ComponentGuid`, and the convention for a retired component is
`GH_Exposure.hidden` plus `[Obsolete]`, so old canvases still open. That
convention exists to protect distributed files. OtterLogic has none, so the
retired components were deleted rather than hidden — hidden components still
appear in the double-click search, which would have undercut the whole cut.
Once anything ships, hide-and-obsolete is back in force for it.

Gone from the ribbon, kept as library code (StructuralEngine and the parity
tests still use it):

- The six raw clustering components, Cluster Selector, Refine Labels, Message
  Passing, Consensus and Multi-View Clustering, Cluster Agreement, Cluster
  Quality, Group Signature, Data Map, Prepare Features, Principal Components,
  Neighbour Graph, Gaussian Affinity.
- The four solve-time supervised methods (nearest-neighbour classifier and
  regressor, ridge, logistic) and both Evaluate components. Their algorithms
  return as learners in the Python trainer, exported through skl2onnx, so the
  layperson's promise — one `.onnx` out — holds for every learner. The C#
  versions stay in the Supervised repo, which Rhino3D no longer references.
- Split By Group: OtterTrain holds out its own groups, and the Evaluate loop
  it served is gone.
- The Clustering Model, Model Type and Neighbour Weighting dropdowns.

Message Passing and Refine Labels went too. They are the least explainable of
the methods to someone who has looked the name up, and nothing in the job-named
panels needs them on the ribbon.

## Below the adaptor line

| Repo | Added |
|---|---|
| Core | `Note` and `NoteLevel`: a sentence with a level, so both front-ends say the same thing and each maps the level onto what it has. |
| Unsupervised | `ClusteringMethod` and one record per algorithm, `AutoMethod` over the selector, `ClusteringOutcome` as the common result, `ClusterRun` as the one entry point with `ClusterRunOptions` and `ClusterRunResult`. |
| MachineLearning | `Learner` and four records replacing the `ModelType` enum, `HoldoutBy`, `SampleFolder`, `TrainerProcess.StartOnSamples`, and `TrainerRuntime.InstallAsync` / `InstallFromFile` with a release manifest. The Python trainer gained the four learners, row holdout and `--version`; `build-bundle.ps1` makes the runtime bundle and its manifest. |
| Rhino3D | Two wire types and their parameters, the three cores, nine method and learner components, the panel cut, icons, and the deletions above. |

The seam is the one the skill already draws: a method record is a mechanism
and lives in the paradigm repo; the component only unpacks a tree and packs
the answer back.

## Still open

- **The release.** The runtime install reads `trainer-manifest.json` from the
  latest release of the MachineLearning repo. Until a release carries the
  bundle `build-bundle.ps1` produces, "Install training runtime" fails with a
  message saying the manifest could not be read, and a developer points
  `OTTERLOGIC_TRAINER` at a checkout's virtual environment.
- **Not yet opened in Grasshopper.** The polling re-solve, the install menu and
  the wire types are exercised by the build and by reading the code.
- **GNN.** Waits for the DeepLearning repo to hold a model. The Learner wire
  pattern means it is an addition, not a redesign.
