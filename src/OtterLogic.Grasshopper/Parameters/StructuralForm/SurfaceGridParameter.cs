using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Parameters.StructuralForm;

/// <summary>
/// The parameter the Grid output of Surface Grid and the Grid input of Space
/// Truss are made of.
/// <para>
/// Hidden from the ribbon and not persistent: a grid is always made by Surface
/// Grid from a surface, so there is nothing to park on the canvas by hand and
/// no "set one grid" menu to offer.
/// </para>
/// </summary>
public sealed class SurfaceGridParameter : GH_Param<GH_SurfaceGrid>
{
    public SurfaceGridParameter()
        : base("Grid", "Grid",
               "An OtterLogic surface grid — nodes on a surface and the members between them. "
               + "Make one with Surface Grid; build a Space Truss on it.",
               Categories.Root, Categories.StructuralForm, GH_ParamAccess.item)
    {
    }

    public override Guid ComponentGuid => new("14b8764a-57a1-4d34-82ad-68af67869c88");

    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override Bitmap? Icon => EmbeddedIcons.Load("surfacegrid", 24);
}
