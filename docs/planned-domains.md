# Planned domains

Each becomes its own repository alongside
[Core](https://github.com/Otter-Logic/Core) and [StructuralForm](https://github.com/Otter-Logic/StructuralForm), on the same pattern:
its own types and its own logic together, depending on Core and on nothing else.

## Form Finding

Nothing built. A first pass lived in Core once — mesh relaxation on a spring
network — and was removed rather than left to rot as a half-thing nobody
planned.

When it returns it wants designing properly: what the goal library covers,
whether goals are composable on a Grasshopper wire, and how a stateful iterative
solver is driven from two front-ends that want different things from it.

## Fabrication

Unrolling, planarisation, nesting, joint generation, toolpaths, DXF export.

If it turns out to need something from StructuralForm, that something belongs in
Core — domains do not reference each other.

## Learning

The one that will not fit the pattern cleanly. Machine learning is
cross-cutting: every domain will want to capture datasets and evaluate
surrogates over its own results.

The likely split is ONNX inference plumbing and the dataset contract in
**Core**, with each domain owning its own feature extraction. That is a guess,
and it should be decided when there is a second domain to look at. Training
itself stays offline in `/python` — see [machine-learning.md](machine-learning.md).
