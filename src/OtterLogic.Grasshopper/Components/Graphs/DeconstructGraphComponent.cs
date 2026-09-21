using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using OtterLogic.Grasshopper.Parameters.Graphs;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// Takes the Graph wire apart: as a list of connections, and as the Connectivity
/// tree the learning components read.
/// <para>
/// Adapter only. The connection order is <c>WeightedGraph.Edges()</c>, and it is
/// the order every per-connection output in this panel uses.
/// </para>
/// </summary>
public sealed class DeconstructGraphComponent : GH_Component
{
    public DeconstructGraphComponent()
        : base("Deconstruct Graph", "DeGraph",
               "Take a graph apart into its nodes and connections.\n\n"
               + "The connections come out once each, lower node first, in ascending order. That "
               + "order is the one every per-connection output in this panel follows — Potential "
               + "Flow's Flow lines up with Lines here item for item. Connectivity and Weights are "
               + "the same graph as trees, for the clustering components that read those.\n\n"
               + "A directed graph comes out as arcs: From is the tail and To the head, in order of "
               + "tail then head, an arc and its reverse listed separately, and Lines run the way "
               + "the arc does.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("3c0d1720-1368-4b18-b158-d7f7ccf3709f");

    // The build tier: making a graph and taking one apart.
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords => new[] { "edges", "nodes", "adjacency", "explode" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("deconstructgraph", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "The graph to take apart.",
            GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P", "Where each node is, in node order. Empty for a graph with no positions.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("From", "A", "Per connection, its lower-numbered node — or, for a directed graph, the tail of the arc.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("To", "B", "Per connection, its higher-numbered node — or, for a directed graph, the head of the arc.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Weight", "W", "Per connection, its weight.", GH_ParamAccess.list);
        pManager.AddLineParameter("Lines", "L", "Per connection, the line between its nodes. Empty for a graph with no positions.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Connectivity", "C", "One branch per node, listing the nodes it connects to, ascending.", GH_ParamAccess.tree);
        pManager.AddNumberParameter("Weights", "Wt", "Matching Connectivity item for item.", GH_ParamAccess.tree);
        pManager.AddBooleanParameter("Directed", "D", "Whether the graph is directed.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!GraphWire.TryGet(da, 0, out var placed)) return;

        var edges = placed.Connections().ToArray();
        var (connectivity, weights) = placed.DirectedOrNull is { } directed
            ? GraphData.ToTrees(directed)
            : GraphData.ToTrees(placed.UndirectedOrNull!);

        da.SetDataList(0, placed.Points ?? Array.Empty<Point3d>());
        da.SetDataList(1, edges.Select(e => e.A));
        da.SetDataList(2, edges.Select(e => e.B));
        da.SetDataList(3, edges.Select(e => e.Weight));
        da.SetDataList(4, placed.Points is null
            ? Array.Empty<Line>()
            : edges.Select(e => new Line(placed.Points[e.A], placed.Points[e.B])));
        da.SetDataTree(5, connectivity);
        da.SetDataTree(6, weights);
        da.SetData(7, placed.IsDirected);

        Message = $"{placed.NodeCount} nodes, {edges.Length} {(placed.IsDirected ? "arcs" : "connections")}";
    }
}
