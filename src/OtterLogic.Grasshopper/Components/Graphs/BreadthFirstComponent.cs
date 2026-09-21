using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using OtterLogic.Graphs;
using OtterLogic.Grasshopper.Parameters.Graphs;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// How many steps every node is from a source, and the rings that makes.
/// <para>
/// Adapter only. The search belongs to <see cref="BreadthFirst.From"/>.
/// </para>
/// </summary>
public sealed class BreadthFirstComponent : GH_Component
{
    public BreadthFirstComponent()
        : base("Breadth-First Search", "BFS",
               "Count the fewest steps from a set of sources to every node, ignoring what the "
               + "connections weigh.\n\n"
               + "Rings is usually what is wanted: every node one step out, then two, then three — "
               + "the generations of a mesh outward from a seed, the joints from a support, how far "
               + "a change spreads. Several sources grow their rings together, and Source says whose "
               + "ring reached each node first. When connections have lengths or costs that should "
               + "count, use Dijkstra Shortest Path; with every weight at 1 the two agree exactly.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("7b779277-31b4-4391-bcae-3cc7527f479a");

    // The routes and flow tier.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords
        => new[] { "bfs", "hops", "steps", "rings", "levels", "flood fill", "topological distance" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("breadthfirst", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "The graph to search. Weights play no part. In a directed graph a step follows an arc "
            + "from its tail to its head only.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Sources", "S",
            "Nodes the count starts from, each at zero steps.",
            GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Steps", "N",
            "Per node, the fewest steps from any source. Null where none reaches it.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Rings", "R",
            "One branch per step count — branch 0 the sources, branch 1 their neighbours, and so on.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Source", "O",
            "Per node, the source that reaches it in the fewest steps, or -1.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Previous", "P",
            "Per node, the node before it on its route back, or -1 at a source or where none reaches it.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Unreached", "U",
            "Nodes in a part of the graph with no source.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!GraphWire.TryGet(da, 0, out var placed)) return;

        var sources = new List<int>();
        if (!da.GetDataList(1, sources)) return;

        try
        {
            var result = placed.DirectedOrNull is { } directed
                ? BreadthFirst.From(directed, sources)
                : BreadthFirst.From(placed.UndirectedOrNull!, sources);
            int n = placed.NodeCount;

            var reached = Enumerable.Range(0, n).Where(result.Reaches).ToArray();
            int depth = reached.Length == 0 ? 0 : (int)reached.Max(i => result.Cost[i]) + 1;

            var ringLists = Enumerable.Range(0, depth).Select(_ => new List<int>()).ToArray();
            foreach (int i in reached)
                ringLists[(int)result.Cost[i]].Add(i);

            var rings = ringLists.Select(ring => ring.ToArray()).ToArray();

            var unreached = Enumerable.Range(0, n).Where(i => !result.Reaches(i)).ToArray();
            if (unreached.Length > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{unreached.Length} node(s) are in a part of the graph no source reaches. See Unreached.");

            da.SetDataList(0, Enumerable.Range(0, n)
                .Select(i => result.Reaches(i) ? new GH_Integer((int)result.Cost[i]) : null));
            da.SetDataTree(1, Trees.FromBuckets(rings));
            da.SetDataList(2, result.Source);
            da.SetDataList(3, result.Previous);
            da.SetDataList(4, unreached);

            Message = depth <= 1 ? "0 rings" : $"{depth - 1} ring{(depth == 2 ? "" : "s")}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
