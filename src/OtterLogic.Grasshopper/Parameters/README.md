# Parameters

Things that sit on the canvas and feed a component, rather than doing work
themselves.

## Enum dropdowns

`EnumValueList<TEnum>` turns any enum into a `GH_ValueList` in dropdown mode: an
icon in the ribbon, a dropdown on the canvas, and an integer down the wire into
whatever input expects it.

A concrete one is a constructor call, a GUID and an icon:

```csharp
public sealed class TrussTypeList : EnumValueList<TrussType>
{
    public TrussTypeList()
        : base("Truss Type", "TrussType",
               "Web bracing patterns for a 2D truss. Plug it into the Type input of Truss 2D.",
               Categories.StructuralForm)
    {
    }

    public override Guid ComponentGuid => new("...a fresh one...");
    protected override Bitmap? Icon => EmbeddedIcons.Load("trusstype", 24);
}
```

The labels come from `EnumChoices.Of<TEnum>()`, which is also what the receiving
component's input uses for its right-click menu. That is the point of it being
one call: a dropdown and the input it feeds cannot end up spelling an option two
different ways.

Two things to remember when adding one:

- **A fresh `ComponentGuid`.** Reusing another object's GUID makes Grasshopper
  load the wrong thing into old definitions.
- **Embed the icon.** Add an `EmbeddedResource` to the csproj with a
  `LogicalName` of `OtterLogic.Icons.<name>.png`, which is what
  `EmbeddedIcons.Load` looks up. A missing icon degrades to no icon rather than
  breaking, so it fails quietly — check it appears.

Rule of thumb: a dropdown is worth it when the value is a closed set someone has
to *choose* from. Numbers, toggles and points already have inputs; don't wrap
them.
