# Types

`GH_Goo<T>` wrappers for Core objects that need to travel along a wire — an
`IGoal`, a solver, a fabrication sheet.

The pattern, when you need it:

```csharp
public sealed class GH_Goal : GH_Goo<IGoal>
{
    public GH_Goal() { }
    public GH_Goal(IGoal goal) : base(goal) { }

    public override bool IsValid => Value is not null;
    public override string TypeName => "Goal";
    public override string TypeDescription => "An OtterLogic relaxation goal";
    public override IGH_Goo Duplicate() => new GH_Goal(Value);
    public override string ToString() => Value?.GetType().Name ?? "<null goal>";
}
```

Rule of thumb: only wrap what a *user* would plug into another component. Plain
values (numbers, meshes, points) already have Grasshopper types — don't reinvent
them.
