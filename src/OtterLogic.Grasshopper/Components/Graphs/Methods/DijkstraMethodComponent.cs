using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs.Methods;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Components.Graphs.Methods;

/// <summary>
/// Dijkstra as a method on a wire: no settings at all.
/// <para>
/// No graph input, on purpose. The graph, sources and targets go to OtterPath;
/// this only makes the <see cref="DijkstraMethod"/> record and hands it over, the
/// way a Kangaroo goal goes into the solver. A component with nothing but an
/// output is still worth having: it is how a person says "this one" on the canvas,
/// and where the description of what it does lives.
/// </para>
/// </summary>
public sealed class DijkstraMethodComponent : GH_Component
{
    public DijkstraMethodComponent()
        : base("Dijkstra", "Dijkstra",
               "The Dijkstra method for OtterPath: the cheapest route through the graph, reading each "
               + "connection's weight as the cost of travelling it — a length, a time, a penalty.\n\n"
               + "Sources are where routes start and Targets where they end; leave Targets empty for the "
               + "cost to every node. Several sources is still one solve: every node goes to whichever "
               + "source is nearest by route, which is how 'distance to the nearest exit' is asked. Values "
               + "is the Cost per node; Marked lists what no source reaches.\n\n"
               + "When every step should count the same whatever it weighs, use Breadth-First. For one "
               + "source and one target on a large placed graph, A* finds the same route faster. Weights "
               + "must not be negative."
               + GraphWire.MethodFootnote,
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("7e2a9d4b-1c6f-4b3e-a8d5-6f1b2c9e4d70");

    // The methods tier, below the core.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords
        => new[] { "shortest path", "route", "pathfinding", "path finding", "nearest", "travel distance", "egress", "one-way" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("shortestpaths", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphMethodParameter(), "Method", "M", GraphWire.MethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var method = new DijkstraMethod();
        da.SetData(0, new GH_GraphMethod(method));
        Message = method.Describe();
    }
}
