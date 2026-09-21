using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// Builds the Graph wire from a tree of neighbour indices.
/// <para>
/// Adapter only, and the seam between the two ways a graph travels here: the
/// learning components speak Connectivity trees because those line up branch for
/// branch with Training Inputs, and the graph components speak one wire.
/// </para>
/// </summary>
public sealed class GraphFromConnectivityComponent : GH_Component
{
    public GraphFromConnectivityComponent()
        : base("Graph From Connectivity", "Graph",
               "Build a graph from a tree that lists, for every node, the nodes it connects to.\n\n"
               + "That tree is what Proximity 3D puts out on Links, what Neighbour Graph puts out on "
               + "Connectivity, and what most topology tools can be made to give. Wire in Points as "
               + "well and everything downstream can answer in geometry — a route as a polyline, a "
               + "bridge as a line — and the graph previews in the viewport.\n\n"
               + "Set Directed to read the tree one way: branch i lists only where i leads to. That "
               + "is a one-way street, a dependency, a flow with a direction. A two-way street is "
               + "then listed from both ends, and may cost differently each way.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("c0fc274b-e1e8-462d-9bbd-efe0593e8d44");

    // The build tier: making a graph and taking one apart.
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords => new[] { "network", "adjacency", "topology", "edges", "nodes", "arcs", "directed", "digraph", "one-way" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("graphfromconnectivity", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Connectivity", "L", GraphData.ConnectivityDescription,
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Weights", "W",
            "Optional. One number per connection, matching Connectivity item for item. What it "
            + "means is up to whatever reads the graph: Dijkstra reads it as the cost of the "
            + "step, so wire in lengths; Potential Flow reads it as how readily the connection "
            + "carries; the clustering methods read it as a similarity from 0 to 1. Unwired, every "
            + "connection weighs 1. Must not be negative; a connection listed from both ends with "
            + "two values keeps the larger.",
            GH_ParamAccess.tree);

        pManager.AddPointParameter("Points", "P",
            "Optional. Where each node is, one point per node in node order — the same points "
            + "that went into Proximity 3D, say.",
            GH_ParamAccess.list);

        pManager.AddBooleanParameter("Directed", "D",
            "False reads a connection listed from either end as one two-way connection. True reads "
            + "each listing as an arc from the branch's node to the node listed, and nothing the "
            + "other way unless that is listed too. Directed weights may be zero or negative; a "
            + "node listed twice in one branch keeps the smaller weight.",
            GH_ParamAccess.item, false);

        pManager[1].Optional = true;
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "The graph, for any component in this panel.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(0, out GH_Structure<GH_Integer> connectivity)) return;

        GH_Structure<GH_Number>? weights = null;
        if (Params.Input[1].VolatileDataCount > 0 && !da.GetDataTree(1, out weights)) return;

        var points = new List<Point3d>();
        bool placed = Params.Input[2].VolatileDataCount > 0 && da.GetDataList(2, points);

        bool directed = false;
        da.GetData(3, ref directed);

        int n = connectivity.Branches.Count;
        string? problem;
        OtterLogic.Graphs.WeightedGraph? graph = null;
        OtterLogic.Graphs.DirectedGraph? arcs = null;

        if (directed
                ? !GraphData.TryReadDirected(connectivity, weights, n, out arcs, out problem)
                : !GraphData.TryRead(connectivity, weights, n, out graph, out problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        if (placed && points.Count != n)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Points holds {points.Count} point(s) but Connectivity has {n} branch(es). There must "
                + "be one point per node, in the same order.");
            return;
        }

        var positions = placed ? points.ToArray() : null;
        var result = arcs is not null ? new PlacedGraph(arcs, positions) : new PlacedGraph(graph!, positions);

        da.SetData(0, new GH_Graph(result));
        Message = $"{result.NodeCount} nodes, {result.ConnectionCount} {(directed ? "arcs" : "connections")}";
    }
}
