# OtterLogic

A Rhino 8 playground for form finding, fabrication and machine learning.

Two front-ends, one brain:

- **`OtterLogic.rhp`** — Rhino commands with live, interruptible previews.
- **`OtterLogic.Grasshopper.gha`** — an `OtterLogic` ribbon tab.
- **`OtterLogic.Core.dll`** — every algorithm. Neither front-end contains logic.

## Build

```bash
dotnet build OtterLogic.slnx
```

Everything lands in `dist/`: the `.rhp`, the `.gha` and `OtterLogic.Core.dll`
side by side, so one build updates both front-ends at once.

```bash
dotnet test
```

## Hook it into Rhino (once)

**Rhino plug-in.** Run `PlugInManager` → *Install…* → pick `dist/OtterLogic.rhp`.
Rhino remembers the path, so later rebuilds load automatically.

**Grasshopper tab.** Run `GrasshopperDeveloperSettings` in Rhino, add the `dist`
folder to the search list, and **untick** *Memory load \*.GHA assemblies using
COFF byte arrays* — leaving it on locks the file and blocks debugging. Restart
Rhino.

Rhino holds both files open while running, so **close Rhino before rebuilding.**

**Debugging.** `src/OtterLogic.Rhino/Properties/launchSettings.json` launches
Rhino under the debugger. Breakpoints in `Core` hit from either front-end.

## Try it

1. Create a mesh with an open boundary (`Mesh` a surface, then `Delete` a few faces).
2. In Rhino: `OtterRelax`, pick the mesh, Enter to solve. Esc stops it and keeps
   whatever it had reached.
3. In Grasshopper: *OtterLogic → Form Finding → Relax Mesh*.

Both run the same solver. `Rest Factor` below 1.0 contracts the mesh toward a
minimal surface; add `Gravity` to make it sag instead.

## Layout

```
src/OtterLogic.Core/          algorithms — RhinoCommon only, no UI, no Grasshopper
  FormFinding/                goals, solvers, MeshRelaxation
  Fabrication/                unrolling, nesting, toolpaths
  Learning/                   ONNX inference, dataset capture
src/OtterLogic.Rhino/         .rhp — commands, conduits, Eto panels
src/OtterLogic.Grasshopper/   .gha — components, GH_Goo types
tests/                        headless solver tests
python/                       offline training → .onnx
models/                       exported .onnx, loaded at runtime
build/                        yak manifest + pack.ps1
docs/                         architecture and ML notes
```

## Packaging

```bash
pwsh build/pack.ps1
```

See [docs/architecture.md](docs/architecture.md) for why the layers are split
this way, and [docs/machine-learning.md](docs/machine-learning.md) for the
training loop.
