using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs;
using OtterLogic.Grasshopper.Parameters.Graphs;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// A graph in; one row of numbers per node describing its place in the network
/// out — the table that lets OtterCluster group nodes by what kind of node they
/// are, or OtterTrain learn from them.
/// <para>
/// Adaptor only. The columns and their arithmetic belong to
/// <see cref="NodeFeatures"/> in Graphs; here the wire is read and the rows come
/// back as a tree.
/// </para>
/// </summary>
public sealed class NodeFeaturesComponent : GH_Component
{
    private const int GraphInput = 0;
    private const int SourcesInput = 1;
    private const int MaximumSourcesInput = 2;

    public NodeFeaturesComponent()
        : base("Node Features", "NodeFeat",
               "Describe every node of a graph by the same row of numbers: how many connections it has "
               + "and how strong, how tightly its neighbours are knit, how much it reaches in two steps, "
               + "how much traffic passes through it, how near it is to everything, how much it holds "
               + "on, how big its piece is, and how far it stands from the nearest source.\n\n"
               + "OtterPath answers one question at a time — the busiest node, the cheapest route. This "
               + "answers a different one: what kind of node is this? A hub, a bridge, a leaf on a spur, "
               + "a room off a corridor. Wire Features into OtterCluster to find the kinds, or into "
               + "OtterTrain with a label per node to learn them.\n\n"
               + "Sources are optional: with them, the last two columns say how many steps and how much "
               + "cost stand between each node and the nearest source; without them those columns read -1."
               + GraphWire.UndirectedNote,
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("203b9ce2-406d-4fee-b64b-be265903a518");

    // The build tier, beside Deconstruct Graph: a graph taken apart into a table.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords => new[] { "features", "degree", "centrality", "closeness", "clustering coefficient", "table" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("nodefeatures", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G", GraphWire.InputDescription, GH_ParamAccess.item);

        pManager.AddIntegerParameter("Sources", "S",
            "Optional. Node indices to measure distance from — the exits, the supports, the entrance. "
            + "To find one from a point, use Closest Point against Deconstruct Graph's Points.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Maximum Sources", "Max",
            "Past this many nodes, Betweenness and Closeness are estimated from this many evenly spread "
            + "starting points rather than all of them, which keeps a large graph interactive.",
            GH_ParamAccess.item, 2000);

        pManager[SourcesInput].Optional = true;
        pManager[MaximumSourcesInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Features", "F",
            "One branch per node, in node order, holding its numbers in the order Feature Names gives. "
            + "Wire it into OtterCluster's Data or OtterTrain's Inputs.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Feature Names", "FN",
            "What each value of a branch is — wire it into the cores' Feature Names.",
            GH_ParamAccess.list);

        pManager.AddPointParameter("Points", "P",
            "Where each node is, in node order, for colouring by a column. Empty for a graph with no positions.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Report", "Rp",
            "What each column measures, and anything worth knowing about this run.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!GraphWire.TryGetUndirected(this, da, GraphInput, out var placed, out var graph))
            return;

        var sources = new List<int>();
        da.GetDataList(SourcesInput, sources);

        int maximumSources = 2000;
        if (!da.GetData(MaximumSourcesInput, ref maximumSources)) return;

        NodeFeatureTable table;
        try
        {
            table = NodeFeatures.Of(graph, sources, maximumSources);
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        foreach (string remark in table.Remarks)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, remark);

        var report = new List<string>
        {
            $"{NodeFeatures.Connections}: how many nodes it is connected to.",
            $"{NodeFeatures.Strength}: the weights of its connections summed.",
            $"{NodeFeatures.Triangles}: the share of its neighbours that are connected to each other, 0 to 1.",
            $"{NodeFeatures.WithinTwoSteps}: how many nodes lie within two steps.",
            $"{NodeFeatures.Betweenness}: the share of shortest routes between other pairs that pass through it, 0 to 1.",
            $"{NodeFeatures.Closeness}: one over its mean distance in steps to the nodes it reaches, scaled by how many it reaches.",
            $"{NodeFeatures.Stranded}: how many nodes lose their connection to the rest when it is removed.",
            $"{NodeFeatures.PieceSize}: how many nodes are in its connected piece.",
            $"{NodeFeatures.StepsToSource}: fewest steps to any source; -1 with none or no route.",
            $"{NodeFeatures.CostToSource}: cheapest route cost to any source over the weights; -1 with none or no route.",
        };
        report.AddRange(table.Remarks);

        da.SetDataTree(0, Trees.FromRows(table.Rows));
        da.SetDataList(1, table.Names);
        da.SetDataList(2, placed.Points ?? Array.Empty<Point3d>());
        da.SetDataList(3, report);

        Message = $"{table.NodeCount} nodes\n{table.ColumnCount} numbers each";
    }
}
