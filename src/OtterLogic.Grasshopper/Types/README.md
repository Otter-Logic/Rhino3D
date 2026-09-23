# Types

`GH_Goo<T>` wrappers for library objects that need to travel along a wire — a
graph, a clustering method, a learner.

The pattern, when you need it:

```csharp
public sealed class GH_Truss : GH_Goo<FlatTruss>
{
    public GH_Truss() { }
    public GH_Truss(FlatTruss truss) : base(truss) { }

    public override bool IsValid => Value is not null;
    public override string TypeName => "Truss";
    public override string TypeDescription => "An OtterLogic 2D truss";
    public override IGH_Goo Duplicate() => new GH_Truss(Value);
    public override string ToString() => Value is null ? "<null truss>" : $"Truss ({Value.PanelCount} panels)";
}
```

`GH_Graph` is the first real one: a `WeightedGraph` from the Graphs repo plus,
optionally, where its nodes are. The positions sit beside the graph in
`PlacedGraph` rather than inside it, because Graphs references nothing and knows
nothing of geometry — carrying them is what lets a component answer with a
polyline instead of a list of indices. It implements `IGH_PreviewData`, so a
placed graph draws itself in the viewport. Its parameter is
`Parameters/Graphs/GraphParameter`.

`GH_SurfaceGrid` carries a `SurfaceGrid` from Surface Grid to Space Truss: the
lattice, the members, the surface they sit on and the openings it was clipped
to, as one wire. It does not draw itself, because the component that made it
already put every member and node on an output of its own. Its parameter is
`Parameters/StructuralForm/SurfaceGridParameter`, hidden from the ribbon since a
grid is only ever made by a component.

`GH_GraphMethod` is the Graphs panel's method on a wire: a `GraphMethod` record
from Graphs — Dijkstra, A*, Potential Flow — on its way from a method component
to OtterPath. Same rules as the two below; its parameter is
`Parameters/Graphs/GraphMethodParameter`, hidden from the ribbon.

`GH_ClusterMethod` and `GH_Learner` are the two "method on a wire" types the
Machine Learning panel runs on. Each wraps an immutable record from the library —
`ClusteringMethod` in Unsupervised, `Learner` in MachineLearning — so `Duplicate`
shares the value rather than copying it, `ToString` is the record's `Describe()`,
and two wires carrying the same settings compare equal. Their parameters are
`Parameters/MachineLearning/ClusterMethodParameter` and `LearnerParameter`.

Rule of thumb: only wrap what a *user* would plug into another component. Plain
values (numbers, meshes, points) already have Grasshopper types — don't reinvent
them.
