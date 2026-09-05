# Machine learning

## Training is not live, and should not be

There are three phases, and only two of them happen in Rhino.

| Phase | Where | Language | Timescale |
|---|---|---|---|
| **1. Generate** | Rhino / Grasshopper | C# | minutes — a parameter sweep |
| **2. Train** | terminal | Python | seconds to hours, offline |
| **3. Infer** | Rhino / Grasshopper | C# via ONNX | sub-millisecond, every slider move |

Phase 2 is a batch job. You run a sweep, leave a training script going, drop the
resulting `.onnx` into `models/`, and the component picks it up. Turnaround is
minutes, not milliseconds — and that is fine, because the thing you actually want
interactive is phase 3.

You *can* run PyTorch inside Rhino 8's embedded CPython, and it is a reasonable
way to prototype. It is a bad way to ship: you inherit a heavyweight dependency
into the plugin, GPU driver problems become Rhino crashes, and a training loop
inside `SolveInstance` blocks the UI thread. Keep the boundary at ONNX.

## Why ONNX is the seam

An `.onnx` file is a frozen computation graph. Python writes it, C# reads it,
neither needs to know about the other. Concretely:

- The plugin has no Python dependency. Users install one `.yak`, not a conda env.
- Switching sklearn → PyTorch changes nothing on the C# side.
- Inference is a native forward pass — fast enough to sit behind a slider.

To wire it up, add the runtime to Core only:

```
dotnet add src/OtterLogic.Core package Microsoft.ML.OnnxRuntime
```

Leave `ExcludeAssets="runtime"` **off** that one — unlike RhinoCommon, it has
native binaries that genuinely must be copied next to the `.gha`.

The one thing that will bite you: **feature order is baked into the model.** If
Python trains on `[span, sag, rest_factor]`, C# must feed them in that order.
Write the column names into a sidecar `.json` next to the `.onnx` and assert on
them at load time.

## sklearn or PyTorch?

They solve different problems. This is not a matter of one being more advanced.

| | scikit-learn | PyTorch |
|---|---|---|
| Input shape | fixed-length vector of numbers | anything — meshes, graphs, images, sequences |
| What you get | ready-made algorithms | autodiff and a layer kit; you assemble the model |
| Training a surrogate on 5k rows | seconds, CPU, no tuning | minutes, and you tune it |
| Below ~10k tabular rows | usually **beats** a neural net | usually loses |
| GPU | no | yes |
| Differentiable end to end | no | **yes** |
| ONNX export | `skl2onnx` | `torch.onnx.export` |

**Reach for sklearn** when the input is a fixed vector of design parameters and
the output is a number or a class. "Given span, sag, rest factor and load,
predict max deflection" is exactly this, and gradient boosting will be both more
accurate and a hundred times faster to train than a neural net on the amount of
data a parameter sweep realistically produces. Start here.

**Reach for PyTorch** when one of three things is true:

1. **Your input has structure a vector cannot hold.** Predicting per-vertex
   stress over a mesh of varying topology is a graph problem; sklearn has no way
   to express it, a GNN does.
2. **You want to generate, not predict.** A VAE or diffusion model over a latent
   design space — sample new forms rather than score existing ones.
3. **You want gradients.** This is the one that matters most for form finding.
   Because the whole model is differentiable, you can invert it: fix the output
   you want and backpropagate to find the inputs that produce it. "What rest
   lengths give me *this* target surface" becomes an optimisation rather than a
   search. You can go further and write the relaxation solver itself in torch,
   then differentiate through the simulation with respect to design parameters.

The honest sequencing for this repo: build surrogates with sklearn first, because
they are cheap and they will teach you what your data actually looks like. Move
to torch when you hit a wall that is specifically about structure, generation, or
gradients — not before.

## Generating datasets

Phase 1 is the part people skip and then regret. A sweep component that writes
one CSV row per solver run — inputs and measured outputs — is the highest-value
thing to build after the solver itself. Put the writer in
`OtterLogic.Core/Learning` so both front-ends can drive it.

Record more than you think you need. Re-running a 5,000-sample sweep because you
forgot to log edge length is a slow afternoon.
