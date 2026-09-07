# OtterLogic

[![build](https://github.com/Otter-Logic/Rhino3D/actions/workflows/build.yml/badge.svg)](https://github.com/Otter-Logic/Rhino3D/actions/workflows/build.yml)

A Rhino 8 playground for structural form, fabrication and machine learning.
Requires Rhino 8 on Windows (any 8.x).

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

```bash
pwsh build/link-dev.ps1
```

That points a junction at `dist/` from
`%APPDATA%\McNeel\Rhinoceros\packages\8.0\OtterLogic\0.1.0`. Rhino loads any
`.rhp` it finds in a package folder and Grasshopper scans the same folders for
`.gha`, so one link covers both front-ends — no `PlugInManager` step, no
`GrasshopperDeveloperSettings` step. Because it is a junction, every later
`dotnet build` is live on the next Rhino start.

Rhino holds both files open while running, so **close Rhino before rebuilding.**
`pwsh build/link-dev.ps1 -Unlink` removes it.

**Debugging.** `src/OtterLogic.Rhino/Properties/launchSettings.json` launches
Rhino under the debugger. Breakpoints in `Core` hit from either front-end.

## Find the tools

An `OtterLogic` toolbar arrives with the plug-in, carrying an icon per tool. It
loads itself from the package folder — no install step. On a machine that has
not seen it before it appears floating; drag it into the tab strip beside
Standard and Curve Tools and Rhino remembers it there.

Adding a tool means a macro and a button in `src/OtterLogic.Rhino/UI/OtterLogic.rui`,
plus an icon in `assets/icons` and a run of `build/build-rui-icons.ps1`.

Every command is also prefixed `Otter`, so typing that in the command line
filters the autocomplete down to this plug-in.

## Try it

**Flat Truss.** Draw two curves, one above the other.

- Rhino: `OtterFlatTruss` walks you through it — pick the top chords, the bottom
  chords, a bracing pattern, whether to flip it, whether to cap the ends, a
  division count, any extra snap points and how far each of them reaches, and a
  panel spacing. It then previews the trusses and lets you keep changing type,
  flip, divisions, spacing, snap distance and end posts before anything is added
  to the document.
- Grasshopper: *OtterLogic → Structural Form → Flat Truss*, the same inputs as ports.
  *Truss Type* sits beside it in the same panel: drop it on the canvas for a
  dropdown of the bracing patterns and wire it into the Type input. The input's
  own right-click menu carries the same list, for when you would rather not have
  a second object on the canvas.

Both chord picks take a **set**, so a whole bay of trusses goes up in one run:
pick the top chords, press Enter, pick the bottom chords in the same order, press
Enter. Top chord *i* pairs with bottom chord *i*, so pick order is truss order.

The counts have to match. Pick three top chords and two bottom ones and the
command says so and stops, rather than guessing which chord went with which —
pick order is the only thing that says what you meant, so a miscount is
something to see and redo, not something to have quietly patched up.

Snap points are picked once and applied to **every** truss in the run, and they
are the *secondary* rule. The vertices and kinks of the chords themselves are
checked first and take every node they can reach, because a node anywhere but a
kink leaves a chord member cutting that corner; the picked points are then
offered whatever is left over, and each node takes the one point nearest to it.

**Snap distance** is how near a node has to come to a picked point for it to
snap — the radius of a sphere around that point. Leave it at zero and there is
no cutoff at all: a point is projected square onto the chord however far to the
side it sits, so a point at *x* = 5 puts a node at *x* = 5 on every truss in the
bay. Across a bay of parallel trusses that is exactly what you want — the nodes
line up. Set a radius when you would rather a point only affect the trusses it
is actually near, which is what the case of non-parallel trusses asks for, since
then "the same place" on one is not the same place on another.

Where the two chords meet — the tip of a cantilever, the apex of a tapered truss
— that end panel gets no end post and no diagonal. Both its nodes are the same
point, so a diagonal out of it would only draw a chord member a second time.

Every setting applies to the whole set: one bay of trusses is one design
decision, and tuning them apart from each other is what the component is for.

Rhino bakes **the whole run** onto one layer tree: a root layer called
`OtterFlatTruss1` — `OtterFlatTruss2` for the next run — with a sub-layer per section
group beneath it: *Top chord*, *Bottom chord*, *Vertical*, *Diagonal*, *End post*
and *Node*. Trusses raised together share those layers, so four trusses of six
panels put all twenty-four of their top chord members on one *Top chord* layer,
and a section is assigned to the bay in one action rather than four. Nothing is
grouped: the layer tree already says what every member is and which run it came
from, and a group on top of that is only a second thing to select through.

A role with nothing in it gets no layer, so a Vierendeel run has no *Diagonal*.
The chord members are generated copies split at every node, which is what a
section wants; the curves you drew and picked are left untouched underneath them.

The Grasshopper component outputs the same six groups as ports — **T**, **B**,
**V**, **D**, **E**, **N** — in the same order and under the same names, so a
section applied to a layer in Rhino and a section applied to a port on the canvas
are applied to the same thing.

`Flip` mirrors every diagonal within its own panel — Pratt becomes Howe, and the
Warren zigzag starts the other way up. Vierendeel has no diagonals to mirror and
cross-bracing already draws both, so neither is affected.

`Divisions` is the primary control, and sits right after the truss type in both
front-ends because the two of them are the whole shape of the truss. It fixes how
many verticals and diagonals there are, laying them out evenly **on plan**, after
which each node **snaps** onto a nearby snap point rather than adding to them.
Reach is half a panel, and no two nodes can claim the same point.

Panels are set out by plan distance, not distance along the chord, so a pitched
top chord over a level bottom one still gives verticals that stand up rather than
lean. Whatever does not snap is then **spread evenly between the nodes that did**,
so a snap point re-divides the truss around it instead of leaving one short panel
and one long one. Snap points anchor both chords at once — a panel point is where
the whole truss steps.

Leave `Divisions` at 0 and control inverts: every detected point becomes a node
in its own right. Two plain lines then give a single panel, which is the honest
answer rather than a guess.

Where the two chords meet at an end, no end post is generated there — it would
collapse onto the shared point and sit on top of the chords.

## Layout

```
src/OtterLogic.Core/            shared vocabulary and helpers — deliberately small
src/OtterLogic.StructuralForm/  domain: trusses, frames, discrete layouts
src/OtterLogic.FormFinding/     domain, planned — relaxation and equilibrium
src/OtterLogic.Fabrication/     domain, planned — unrolling, nesting, toolpaths
src/OtterLogic.MachineLearning/ layer, separate repo — clustering, inference, datasets
src/OtterLogic.Rhino/           adaptor: .rhp — commands, conduits, the Eto panel
src/OtterLogic.Grasshopper/     adaptor: .gha — components, GH_Goo types
tests/                          per domain (Rhino.Inside boots Rhino for geometry)
python/                       offline training → .onnx
models/                       exported .onnx, loaded at runtime
build/                        yak manifest, pack.ps1, link-dev.ps1
docs/                         architecture and ML notes
```

## Packaging

```bash
pwsh build/pack.ps1
```

See [docs/architecture.md](docs/architecture.md) for why the layers are split
this way, and [docs/machine-learning.md](docs/machine-learning.md) for the
training loop.

## Contributing

`dotnet build` is enough to check a change compiles, and CI does that on every
push. `dotnet test` needs Rhino installed — the tests boot it in-process through
Rhino.Inside, because anything touching `Mesh`, `Curve` or `Brep` needs the real
thing. That is also why CI builds but does not test.

Close Rhino before rebuilding; it holds the `.rhp` and `.gha` open, and the
build fails with `MSB3027` rather than silently doing nothing.

## License

[MIT](LICENSE).
