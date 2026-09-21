using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Graphs;
using OtterLogic.Grasshopper.Parameters.Graphs;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// Cheapest routes through a graph, from one source or several.
/// <para>
/// Adapter only. The search belongs to <see cref="Dijkstra.From"/>.
/// </para>
/// </summary>
public sealed class DijkstraComponent : GH_Component
{
    public DijkstraComponent()
        : base("Dijkstra Shortest Path", "Dijkstra",
               "Find the cheapest route through a graph, reading each connection's weight as the "
               + "cost of travelling it — a length, a time, a penalty.\n\n"
               + "One source and one target gives the route between two nodes. Several sources is "
               + "still one solve: every node goes to whichever source is nearest by route, not by "
               + "straight line, which is how 'distance to the nearest exit' is asked, and Source "
               + "is then a partition of the graph in its own right. Leave Targets empty for the "
               + "cost to every node. To find a node's index from a point, use Closest Point "
               + "against Deconstruct Graph's Points.\n\n"
               + "When every step should count the same whatever it weighs, use Breadth-First "
               + "Search. Weights that are similarities — Gaussian Affinity's — are not costs: a "
               + "high similarity is a short distance.",
               Categories.Root, Categories.Graphs)
    {
    }

    // Unchanged from when this was Shortest Paths.
    public override Guid ComponentGuid => new("d52cc944-824e-43be-976f-48df673b2ee7");

    // The routes and flow tier.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords
        => new[] { "shortest path", "route", "pathfinding", "path finding", "nearest", "travel distance", "egress", "one-way" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("shortestpaths", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "The graph to route over. Each connection's weight is the cost of travelling it and must "
            + "not be negative. In a directed graph an arc is travelled from its tail to its head "
            + "only, which is how a one-way street is said.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Sources", "S",
            "Nodes a route may start from, each at cost zero.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Targets", "T",
            "Optional. The only nodes a route is wanted to. The search stops as soon as the last "
            + "of them is reached, which on a large graph is most of the saving, and only what was "
            + "settled by then is reported — everything further reads as unreached rather than as "
            + "a guess. Empty routes to every node.",
            GH_ParamAccess.list);

        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Routes", "R",
            "One branch per target — or per node, without targets — holding its route, source first. "
            + "Empty where none reaches it.",
            GH_ParamAccess.tree);

        pManager.AddCurveParameter("Curves", "Cv",
            "The same routes as polylines, where the graph has positions. Null for a route of one "
            + "node or none.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Cost", "C",
            "Per node, the cost of its cheapest route from any source. Null where none was found.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Source", "O",
            "Per node, the source its cheapest route starts from, or -1.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Previous", "P",
            "Per node, the node before it on its route, or -1 at a source or where none was found. "
            + "Together these are the whole tree of routes.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Unreached", "U",
            "Targets — or nodes, without targets — that no source reaches.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!GraphWire.TryGet(da, 0, out var placed)) return;

        var sources = new List<int>();
        if (!da.GetDataList(1, sources)) return;

        var targets = new List<int>();
        bool targeted = Params.Input[2].VolatileDataCount > 0 && da.GetDataList(2, targets) && targets.Count > 0;

        try
        {
            var wanted = targeted ? targets : null;
            var result = placed.DirectedOrNull is { } directed
                ? Dijkstra.From(directed, sources, targets: wanted)
                : Dijkstra.From(placed.UndirectedOrNull!, sources, targets: wanted);

            int n = placed.NodeCount;
            var ends = targeted ? targets : Enumerable.Range(0, n).ToList();

            var routes = new DataTree<int>();
            var curves = new List<GH_Curve?>(ends.Count);
            var unreached = new List<int>();

            for (int k = 0; k < ends.Count; k++)
            {
                var path = new GH_Path(k);
                routes.EnsurePath(path);

                var route = result.RouteTo(ends[k]);
                routes.AddRange(route, path);

                if (route.Length == 0)
                    unreached.Add(ends[k]);

                curves.Add(placed.Trace(route) is { } line ? new GH_Curve(line.ToPolylineCurve()) : null);
            }

            if (unreached.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{unreached.Count} {(targeted ? "target" : "node")}(s) are in a part of the graph no "
                    + "source reaches. See Unreached.");

            da.SetDataTree(0, routes);
            da.SetDataList(1, placed.Points is null ? new List<GH_Curve?>() : curves);
            da.SetDataList(2, Enumerable.Range(0, n)
                .Select(i => result.Reaches(i) ? new GH_Number(result.Cost[i]) : null));
            da.SetDataList(3, result.Source);
            da.SetDataList(4, result.Previous);
            da.SetDataList(5, unreached);

            Message = targeted
                ? $"{sources.Distinct().Count()} → {targets.Distinct().Count()}"
                : $"{sources.Distinct().Count()} source(s)";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
