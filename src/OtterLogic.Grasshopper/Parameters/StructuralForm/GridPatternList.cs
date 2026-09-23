using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.StructuralForm;

namespace OtterLogic.Grasshopper.Parameters.StructuralForm;

/// <summary>
/// The grid patterns as a dropdown, for the Pattern input of Surface Grid.
/// Everything else is <see cref="EnumValueList{TEnum}"/>'s.
/// </summary>
public sealed class GridPatternList : EnumValueList<GridPattern>
{
    public GridPatternList()
        : base("Grid Pattern", "GridPattern",
               "Quad, triangulated or diagrid. Plug it into the Pattern input of Surface Grid.",
               Categories.StructuralForm)
    {
    }

    // Below the generators, behind a divider: a dropdown is wired into a
    // generator's input, so nobody reaches for one first.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override Guid ComponentGuid => new("996a98c2-ef68-45f4-b4b6-bc8e96d5588a");

    protected override Bitmap? Icon => EmbeddedIcons.Load("gridpattern", 24);
}
