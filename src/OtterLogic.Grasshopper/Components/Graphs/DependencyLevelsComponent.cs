using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs;
using OtterLogic.Grasshopper.Parameters.Graphs;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// Ranks the nodes of a directed graph by what they depend on, folding every
/// cycle into one group first.
/// <para>
/// Adapter only. The ordering belongs to <see cref="Condensation.Of"/>.
/// </para>
/// </summary>
public sealed class DependencyLevelsComponent : GH_Component
{
    public DependencyLevelsComponent()
        : base("Dependency Levels", "Levels",
               "Rank every node by the longest chain of things it depends on: level 0 depends on "
               + "nothing, level 1 only on level 0, and so on. Everything on one level can go ahead "
               + "together once the levels below it are done — an erection sequence, an assembly "
               + "order, which element carries which.\n\n"
               + "Dependence that runs in a circle is not an order at all, so every cycle is folded "
               + "into one group whose nodes share a level, and reported under Cycles rather than "
               + "forced into a sequence that would only record which arc was met first.\n\n"
               + "It needs a directed graph, since an order needs a direction: build one with Graph "
               + "From Connectivity and Directed set, each branch listing what that node depends on.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("79cc571e-0796-47c1-8c57-7746eb9afc0d");

    // The structure tier: what the graph is made of, before anything travels over it.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords
        => new[] { "topological sort", "strongly connected components", "scc", "tarjan", "dag", "sequence", "hierarchy", "condensation" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("dependencylevels", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "A directed graph in which an arc from one node to another says the first depends on "
            + "the second — what must be in place before it, what it rests on. Weights play no part.",
            GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Level", "L",
            "Per node, the longest chain of dependence below it. Zero depends on nothing.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Sequence", "S",
            "One branch per level, lowest first, holding the nodes on it.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Order", "O",
            "Every node once, with everything it depends on placed before it — a topological "
            + "order, by level and then by index. Nodes on a cycle come out side by side.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Group", "G",
            "Per node, the group it was folded into — its strongly connected component. Nodes share a group only when each depends on "
            + "the other through some chain; without cycles every node is a group of one.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Cycles", "C",
            "One branch per group of more than one node — the circular dependencies. Empty when the "
            + "graph is a clean hierarchy.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!GraphWire.TryGet(da, 0, out var placed)) return;

        // Read as arcs each way, an undirected graph is nothing but cycles: every
        // connected piece would fold into one group at level zero. True, and useless.
        if (placed.DirectedOrNull is not { } graph)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "This needs a directed graph: an order needs to know which way each dependence runs. "
                + "Build the graph with Graph From Connectivity and set Directed, each branch listing "
                + "what that node depends on.");
            return;
        }

        int n = graph.NodeCount;

        try
        {
            var result = Condensation.Of(graph);

            var level = Enumerable.Range(0, n).Select(result.HeightOf).ToArray();
            int levels = level.Max() + 1;

            var sequence = Enumerable.Range(0, levels)
                .Select(l => Enumerable.Range(0, n).Where(i => level[i] == l).ToArray())
                .ToArray();

            var cycles = Enumerable.Range(0, n)
                .GroupBy(i => result.Component[i])
                .Where(group => group.Count() > 1)
                .Select(group => group.ToArray())
                .ToArray();

            if (cycles.Length > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{cycles.Length} circular dependenc{(cycles.Length == 1 ? "y" : "ies")} folded into "
                    + "one group each. See Cycles.");

            da.SetDataList(0, level);
            da.SetDataTree(1, Trees.FromBuckets(sequence));
            da.SetDataList(2, result.TopologicalOrder());
            da.SetDataList(3, result.Component);
            da.SetDataTree(4, Trees.FromBuckets(cycles));

            Message = $"{levels} level{(levels == 1 ? "" : "s")}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
