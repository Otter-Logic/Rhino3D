# Types

`GH_Goo<T>` wrappers for Core objects that need to travel along a wire — a
truss, a fabrication sheet, a goal.

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

Rule of thumb: only wrap what a *user* would plug into another component. Plain
values (numbers, meshes, points) already have Grasshopper types — don't reinvent
them.
