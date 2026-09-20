using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Graphs;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// Least-cost routes from a set of source samples to every sample of a graph.
/// <para>
/// Adapter only. The search belongs to <see cref="ShortestPaths.From"/>.
/// </para>
/// </summary>
public sealed class ShortestPathsComponent : GH_Component
{
    public ShortestPathsComponent()
        : base("Shortest Paths", "Paths",
               "Find the cheapest route from any of a set of sources to every sample, and which "
               + "source it starts from.\n\n"
               + "With several sources this also partitions the graph: every sample goes to the "
               + "source nearest it by route, not by straight line — Source is a grouping in its "
               + "own right. Cost is a distance along the graph, so it is also a feature: how far "
               + "each sample sits from the supports, the entrances, the nearest anything.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("d52cc944-824e-43be-976f-48df673b2ee7");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("shortestpaths", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Connectivity", "L", GraphData.ConnectivityDescription,
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Costs", "W",
            "Optional. The cost of travelling each connection, matching Connectivity item for item "
            + "— a length, a time, a penalty. Unwired, every connection costs 1 and routes are "
            + "counted in hops. Costs must not be negative. These are costs, not the 0 to 1 "
            + "similarities Gaussian Affinity produces: a high similarity is a short distance.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Sources", "S",
            "Samples a route may start from, each at cost zero.",
            GH_ParamAccess.list);

        pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Cost", "C",
            "Per sample, the cost of its cheapest route from any source. Null where none reaches it.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Source", "O",
            "Per sample, the source its cheapest route starts from, or -1 where none reaches it.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Previous", "P",
            "Per sample, the sample before it on its route, or -1 at a source or where none reaches it.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Routes", "R",
            "One branch per sample: its route, source first. Empty where none reaches it.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Unreached", "U",
            "Samples no source reaches — in a part of the graph with no source of its own.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(0, out GH_Structure<GH_Integer> connectivity)) return;

        GH_Structure<GH_Number>? costs = null;
        if (Params.Input[1].VolatileDataCount > 0 && !da.GetDataTree(1, out costs)) return;

        var sources = new List<int>();
        if (!da.GetDataList(2, sources)) return;

        int n = connectivity.Branches.Count;
        if (!GraphData.TryRead(connectivity, costs, n, out var graph, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        try
        {
            var result = ShortestPaths.From(graph!, sources);

            var cost = new List<GH_Number?>(n);
            var routes = new DataTree<int>();
            var unreached = new List<int>();

            for (int i = 0; i < n; i++)
            {
                var path = new GH_Path(i);
                routes.EnsurePath(path);

                if (!result.Reaches(i))
                {
                    cost.Add(null);
                    unreached.Add(i);
                    continue;
                }

                cost.Add(new GH_Number(result.Cost[i]));
                routes.AddRange(result.RouteTo(i), path);
            }

            if (unreached.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{unreached.Count} sample(s) are in a part of the graph no source reaches. See Unreached.");

            da.SetDataList(0, cost);
            da.SetDataList(1, result.Source);
            da.SetDataList(2, result.Previous);
            da.SetDataTree(3, routes);
            da.SetDataList(4, unreached);

            Message = $"{sources.Distinct().Count()} source(s)";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
