using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Graphs;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

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
               "Score every sample by the share of shortest routes between all other pairs that pass "
               + "through it, 0 to 1.\n\n"
               + "High scores are the bottlenecks and bridges — the nodes the rest of the graph "
               + "leans on to reach itself. Useful directly, or as one more column of Training "
               + "Inputs so a clustering can tell hubs from ends. Routes are counted in hops; "
               + "Weights play no part, because a similarity is not a distance.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("a2dad2cf-1b0a-4543-b82c-83e51a013821");

    // The graph tier: a reading of the graph, made before a method runs.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("betweenness", 24);

    private const int DefaultSources = 2000;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Connectivity", "L", GraphData.ConnectivityDescription,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Maximum Sources", "M",
            "Past this many samples the score is estimated from this many evenly spread starting "
            + "points rather than all of them, which keeps a large graph interactive. Evenly "
            + "spread, not random, so the answer is the same every solve.",
            GH_ParamAccess.item, DefaultSources);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Betweenness", "B",
            "Per sample, 0 to 1: the share of shortest routes between other pairs passing through it.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(0, out GH_Structure<GH_Integer> connectivity)) return;

        int sources = DefaultSources;
        if (!da.GetData(1, ref sources)) return;

        int n = connectivity.Branches.Count;
        if (!GraphData.TryRead(connectivity, null, n, out var graph, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        try
        {
            var betweenness = Centrality.Betweenness(graph!, sources);

            if (n > sources)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Estimated from {sources} of {n} samples. Raise Maximum Sources for the exact "
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
