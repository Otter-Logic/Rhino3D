using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Parameters.Graphs;

/// <summary>
/// The parameter every Method input and output in the Graphs panel is made of.
/// <para>
/// Hidden from the ribbon: a method is always made by a method component, so
/// there is nothing to park on the canvas and no "set one" menu to offer. Not
/// persistent for the same reason.
/// </para>
/// </summary>
public sealed class GraphMethodParameter : GH_Param<GH_GraphMethod>
{
    public GraphMethodParameter()
        : base("Graph Method", "Method",
               "A graph algorithm with its settings chosen. Made by Dijkstra, A*, Breadth-First, Potential "
               + "Flow, Betweenness, Connected Pieces, Cut Vertices or Dependency Levels; read by OtterPath.",
               Categories.Root, Categories.Graphs, GH_ParamAccess.item)
    {
    }

    public override Guid ComponentGuid => new("5a1c7e2d-93b4-4f6e-8a2b-1c9d7e3f5b60");

    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override Bitmap? Icon => EmbeddedIcons.Load("graphmethod", 24);
}
