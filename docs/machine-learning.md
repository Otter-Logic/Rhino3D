# Machine learning

## Two kinds of model

Everything in the ML panel is one of two things, and one question tells them
apart: *are there numbers that had to be learned from data that is not on the
wire right now?*

| | Fitted at solve time | Trained, then used |
|---|---|---|
| Example | Gaussian mixture, k-means, PCA | a boosted-tree surrogate, a network |
| Where do the parameters come from? | the data on the wire, on every solve | a training run over a corpus, once |
| Anything to ship? | **no** | one `.onnx`, carrying its own metadata |
| Component | OtterCluster, OtterEmbed | OtterTrain writes it; OtterPredict runs it |
| Crosses the ONNX boundary? | **never** | always |

A Gaussian mixture is emphatically the first kind — its means, covariances and
weights are computed by EM on every solve, and pushing it through ONNX would
freeze a *fitted* model's predict step, which is not what the tool is for.

## Where training runs

Inside Rhino, in C#, on a background task. OtterTrain takes samples and a learner
on a wire and calls `TrainRun.Fit` in the Supervised repo, which holds out whole
groups, fits, scores, writes the `.onnx`, runs the file back through ONNX Runtime
and keeps it only if it answers what the fitted model answers. Turnaround is
seconds to a minute on a table of a few thousand rows; the canvas stays live and
Status says what is happening. Nothing needs installing beyond the `.yak`.

The four learners are the tabular case done well:

| Learner | Fits | Reach for it when |
|---|---|---|
| Boosted Trees | shallow trees, each correcting the last | first — the default, usually the most accurate on a few thousand rows |
| Random Forest | deep trees on resamples, averaged | boosting looks too good on the rows it trained on |
| Neural Network | a small fully-connected network | the relationship is smooth and the rows are many |
| Linear Model | ridge or logistic regression | to check whether the relationship was a straight line all along |

Until 2026-09-25 training ran in a separate Python process with a runtime the
plug-in downloaded on first use. It was taken out before it shipped: a bundle
pins package versions that drift against the reader, a hundred-megabyte download
meets every firm's proxy, and it was a release pipeline of its own. The decision
and its reasons are in MachineLearning's `docs/in-process-training.md`.

## Why ONNX is still the seam

An `.onnx` file is a frozen computation graph, and a model is one file whoever
wrote it. Concretely:

- OtterPredict runs a model from OtterTrain and a model from PyTorch the same
  way. The Inference tests open both kinds — scikit-learn exports carrying the
  OtterLogic metadata record, and a network in the shape `torch.onnx.export`
  writes, carrying none.
- The C# side writes ONNX itself now, through `OnnxGraph` in MachineLearning's
  Inference project — a few operators over a hand-written protobuf encoder,
  because ONNX Runtime does not write models and Google.Protobuf was not worth a
  dependency. Every export is run back through the runtime before the file is
  kept.
- Inference is a native forward pass, fast enough to sit behind a slider.

The runtime lives in the MachineLearning layer, in its own project, because it
is the one dependency with native binaries. Leave `ExcludeAssets="runtime"`
**off** it, unlike RhinoCommon: `onnxruntime.dll` genuinely must sit next to the
`.gha`, and the Grasshopper project copies it up from `runtimes/win-x64/native`.

**Feature order is baked into the model.** The column names, target, classes and
score are written *into* the `.onnx` under the `metadata_props` key `otterlogic`,
and OtterPredict asserts on them at load time. No sidecar: a model is one file.

## Deep learning

Deep-learning models are trained where their framework is — PyTorch, on whatever
machine has the GPU — and arrive as an `.onnx`. OtterPredict runs them. That is
the whole of the plug-in's deep-learning story, deliberately: training a network
of any size inside Rhino means a heavyweight dependency in the plug-in, GPU
driver problems that become Rhino crashes, and a loop that blocks the UI thread.
Keep the boundary at ONNX.

Reach for a framework rather than OtterTrain when the input has structure a
vector cannot hold (per-vertex stress over a mesh of varying topology is a graph
problem), when you want to generate rather than predict, or when you want
gradients to invert a model — fix the output you want and backpropagate to the
inputs that produce it. For a fixed vector of design parameters and a number or a
class out, OtterTrain's boosted trees will be more accurate and a hundred times
faster to train on the amount of data a parameter sweep realistically produces.

## Generating datasets

Phase one is the part people skip and then regret. A sweep component that writes
one row per generated design — inputs and measured outputs — through Write
Dataset is the highest-value thing to build once there is something worth
sweeping. Record more than you think you need: re-running a 5,000-sample sweep
because you forgot to log edge length is a slow afternoon.
