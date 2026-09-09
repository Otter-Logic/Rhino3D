# Planned domains

Each becomes its own repository alongside
[Core](https://github.com/Otter-Logic/Core) and [StructuralForm](https://github.com/Otter-Logic/StructuralForm), on the same pattern:
its own types and its own logic together, depending on Core and on nothing
else. Machine Learning is the exception — see below.

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

## Machine Learning

Decided, and it does not fit the domain pattern — so it is not a domain. Machine
learning is cross-cutting: every toolkit will want to capture datasets and run a
model over its own results.

It sits instead as an **intermediate layer** between Core and the toolkits, in
its own repo,
[MachineLearning](https://github.com/Otter-Logic/MachineLearning). Toolkits may
reference it; it references only Core.

It owns the ONNX plumbing, the dataset contract, and the model implementations.
Core keeps only the section vocabulary. Each toolkit still owns its own feature
extraction, because what a truss considers a feature is not what a nesting
problem does.

It is a toolkit in its own right too. Its clustering components ship under the
**Machine Learning** section, along with dataset capture and inference when those
exist — every raw method in one panel, ordered by `GH_Exposure` into pipeline
stages. The 6DOF Behaviour Classifier that chooses between them is not there: it
carries a structural opinion, so it ships under **Structural Design** with the
other tools named for a job. Training stays offline in that repo's `/python` —
see [machine-learning.md](machine-learning.md).

## Structural Design

Tools that act on analysis results rather than producing geometry: grouping
members by behaviour, sizing, predicting demand. Sibling to Structural Form,
which generates a structure where this answers questions about one that already
exists.

It is where every purpose-built model lands. The general rule: **a purpose-built
model lives in the toolkit for the domain it has an opinion about, never in the
layer that owns the algorithm.** The layer owns the mechanism; the toolkit owns
what the numbers mean. That is what lets Fabrication group panels with the same
clustering machinery and its own judgement, with no domain-to-domain reference
and no second copy of anything.

Today it is the 6DOF Behaviour Classifier, which lives in the
[Clustering Tool](https://github.com/Otter-Logic/Clustering_Tool) repo.
