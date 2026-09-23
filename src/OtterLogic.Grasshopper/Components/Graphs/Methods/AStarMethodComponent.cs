using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs.Methods;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Components.Graphs.Methods;

/// <summary>
/// A* as a method on a wire: one setting, what a unit of straight-line distance
/// is worth. No graph input; see <see cref="DijkstraMethodComponent"/>.
/// </summary>
public sealed class AStarMethodComponent : GH_Component
{
    public AStarMethodComponent()
        : base("A*", "A*",
               "The A* method for OtterPath: the route Dijkstra would find from one source to one target, "
               + "found by searching towards the target rather than evenly in all directions. On a large "
               + "grid or a street network that is most of the solve time; Marked shows exactly what was "
               + "looked at.\n\n"
               + "It steers by the straight-line distance to the target, so the graph needs positions, and "
               + "each connection must weigh at least its own length times Estimate Scale — true of any "
               + "graph weighted by length, and of Graph From Lines, Graph From Points and Visibility Graph "
               + "as they come. Whether to measure that line in three dimensions or in plan is decided "
               + "from the graph itself.\n\n"
               + "One source and one target only. For the cost to every node, or from the nearest of "
               + "several sources, use Dijkstra; on a graph of a few hundred nodes use it anyway, since "
               + "there is nothing to save."
               + GraphWire.MethodFootnote,
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("3c8f1b6e-2d7a-4c9b-b1e4-8a2f6d3c1e52");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords
        => new[] { "astar", "a star", "shortest path", "route", "pathfinding", "path finding", "heuristic", "navigation" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("astar", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Estimate Scale", "E",
            "What one unit of straight-line distance is worth in the graph's weights, at the very least. "
            + "1 when weights are lengths. One over the fastest speed when they are times. 0 switches the "
            + "estimate off, which makes this Dijkstra. Higher than the truth is faster still and may "
            + "return a route that is not the cheapest; OtterPath warns when the weights show that is possible.",
            GH_ParamAccess.item, 1.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphMethodParameter(), "Method", "M", GraphWire.MethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        double scale = 1.0;
        if (!da.GetData(0, ref scale)) return;

        var method = new AStarMethod { EstimateScale = scale };
        da.SetData(0, new GH_GraphMethod(method));
        Message = method.Describe();
    }
}
