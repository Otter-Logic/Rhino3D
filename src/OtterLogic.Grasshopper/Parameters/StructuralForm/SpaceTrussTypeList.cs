using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.StructuralForm;

namespace OtterLogic.Grasshopper.Parameters.StructuralForm;

/// <summary>
/// What runs between the layers of a space truss, as a dropdown for the Type
/// input of Space Truss. Everything else is <see cref="EnumValueList{TEnum}"/>'s.
/// </summary>
public sealed class SpaceTrussTypeList : EnumValueList<SpaceTrussType>
{
    public SpaceTrussTypeList()
        : base("Space Truss Type", "SpaceTrussType",
               "Pyramid on every cell, or a flat truss pattern along every grid line. Plug it into "
               + "the Type input of Space Truss.",
               Categories.StructuralForm)
    {
    }

    // Below the generators, behind a divider: a dropdown is wired into a
    // generator's input, so nobody reaches for one first.
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    public override Guid ComponentGuid => new("35243f65-72ec-460f-8b41-dee8471e5936");

    protected override Bitmap? Icon => EmbeddedIcons.Load("spacetrusstype", 24);
}
