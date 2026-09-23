using Grasshopper.Kernel.Types;
using OtterLogic.Core;
using OtterLogic.StructuralForm;

namespace OtterLogic.Grasshopper.Types;

/// <summary>
/// The Grid wire: a <see cref="SurfaceGrid"/> as one connection between
/// components — its lattice of nodes, the members drawn over it, the surface
/// they sit on and the openings it was clipped to — instead of a tree of
/// lines and a tree of points that would have to be kept in step and would
/// still not say where the surface was.
/// <para>
/// Surface Grid makes one; Space Truss builds on it. Nothing draws it in the
/// viewport, because the component that made it already put every member and
/// node on an output of its own, and a wire that drew them again would draw
/// the grid twice.
/// </para>
/// </summary>
public sealed class GH_SurfaceGrid : GH_Goo<SurfaceGrid>
{
    public GH_SurfaceGrid() { }
    public GH_SurfaceGrid(SurfaceGrid grid) : base(grid) { }

    public override bool IsValid => Value is not null;
    public override string TypeName => "Surface Grid";
    public override string TypeDescription
        => "An OtterLogic surface grid: a lattice of nodes on a surface and the members a pattern drew over it";

    // A generated grid never changes once built, so a duplicate may share it.
    public override IGH_Goo Duplicate() => new GH_SurfaceGrid(Value);

    public override string ToString()
        => Value is null
            ? "<null grid>"
            : $"Surface grid ({Naming.Humanise(Value.Options.Pattern).ToLowerInvariant()}, "
              + $"{Value.PanelsU} by {Value.PanelsV}, {Value.Members.Count} members"
              + (Value.IsClipped ? ", clipped)" : ")");
}
