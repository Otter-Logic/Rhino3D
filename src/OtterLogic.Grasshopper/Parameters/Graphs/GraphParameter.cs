using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Types;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Parameters.Graphs;

/// <summary>
/// The parameter every Graph input and output is made of, and a floating one for
/// parking a graph on the canvas.
/// <para>
/// Not persistent: a graph is always built by a component from something else, so
/// there is nothing to internalise and no "set one graph" menu to offer.
/// </para>
/// </summary>
public sealed class GraphParameter : GH_Param<GH_Graph>, IGH_PreviewObject
{
    public GraphParameter()
        : base("Graph", "Graph",
               "An OtterLogic graph — nodes, weighted connections, and node positions when it was "
               + "built from geometry. Make one with Graph From Connectivity; take one apart with "
               + "Deconstruct Graph.",
               Categories.Root, Categories.Graphs, GH_ParamAccess.item)
    {
    }

    public override Guid ComponentGuid => new("e4ccd861-a031-4609-aa5e-353ba2eff48a");

    // The build tier, beside the components that make and unmake one.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("graph", 24);

    public bool Hidden { get; set; }
    public bool IsPreviewCapable => true;
    public BoundingBox ClippingBox => Preview_ComputeClippingBox();
    public void DrawViewportWires(IGH_PreviewArgs args) => Preview_DrawWires(args);
    public void DrawViewportMeshes(IGH_PreviewArgs args) => Preview_DrawMeshes(args);
}
