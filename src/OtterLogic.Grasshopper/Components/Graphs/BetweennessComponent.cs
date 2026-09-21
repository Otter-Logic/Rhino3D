using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs;
using OtterLogic.Grasshopper.Parameters.Graphs;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// How much of a graph's traffic passes through each node.
/// <para>
/// Adapter only. The measure belongs to <see cref="Centrality.Betweenness"/>.
/// </para>
/// </summary>
public sealed class BetweennessComponent : GH_Component
{
    public BetweennessComponent()
        : base("Betweenness", "Between",
               "Score every node by the share of shortest routes between all other pairs that pass "
               + "through it, 0 to 1.\n\n"
               + "High scores are the bottlenecks and bridges — the nodes the rest of the graph "
               + "leans on to reach itself. Useful directly, or as one more column of Training "
               + "Inputs so a clustering can tell hubs from ends. Routes are counted in hops; "
               + "Weights play no part, because a similarity is not a distance.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("a2dad2cf-1b0a-4543-b82c-83e51a013821");

    // The importance tier: a score per node, read off every route at once.
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    public override IEnumerable<string> Keywords
        => new[] { "centrality", "brandes", "bottleneck", "hub", "congestion", "importance" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("betweenness", 24);

    private const int DefaultSources = 2000;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "The graph to score. Routes are counted in steps; weights play no part." + GraphWire.UndirectedNote,
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Maximum Sources", "M",
            "Past this many nodes the score is estimated from this many evenly spread starting "
            + "points rather than all of them, which keeps a large graph interactive. Evenly "
            + "spread, not random, so the answer is the same every solve.",
            GH_ParamAccess.item, DefaultSources);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Betweenness", "B",
            "Per node, 0 to 1: the share of shortest routes between other pairs passing through it.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!GraphWire.TryGetUndirected(this, da, 0, out _, out var graph)) return;

        int sources = DefaultSources;
        if (!da.GetData(1, ref sources)) return;

        int n = graph.NodeCount;

        try
        {
            var betweenness = Centrality.Betweenness(graph, sources);

            if (n > sources)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Estimated from {sources} of {n} nodes. Raise Maximum Sources for the exact "
                    + "value, at the cost of solve time.");

            da.SetDataList(0, betweenness);
            Message = n > sources ? "estimated" : "exact";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
