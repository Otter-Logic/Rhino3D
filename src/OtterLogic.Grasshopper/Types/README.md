# Types

`GH_Goo<T>` wrappers for Core objects that need to travel along a wire — a
truss, a fabrication sheet, a goal.

The pattern, when you need it:

```csharp
public sealed class GH_Truss : GH_Goo<Truss2D>
{
    public GH_Truss() { }
    public GH_Truss(Truss2D truss) : base(truss) { }

    public override bool IsValid => Value is not null;
    public override string TypeName => "Truss";
    public override string TypeDescription => "An OtterLogic 2D truss";
    public override IGH_Goo Duplicate() => new GH_Truss(Value);
    public override string ToString() => Value is null ? "<null truss>" : $"Truss ({Value.PanelCount} panels)";
}
```

Rule of thumb: only wrap what a *user* would plug into another component. Plain
values (numbers, meshes, points) already have Grasshopper types — don't reinvent
them.
