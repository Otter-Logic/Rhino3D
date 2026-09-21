using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using OtterLogic.Graphs;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// The cheapest route between two nodes, found by searching towards the target.
/// <para>
/// Adapter only. The search belongs to <see cref="AStar.Route"/>. What this adds is
/// the one thing Graphs cannot know — where the nodes are — turned into the
/// straight-line estimate the search steers by, and a check that the estimate is
/// one the search can trust.
/// </para>
/// </summary>
public sealed class AStarComponent : GH_Component
{
    public AStarComponent()
        : base("A* Shortest Path", "A*",
               "Find the cheapest route between two nodes by searching towards the target rather "
               + "than evenly in all directions. The route is the one Dijkstra Shortest Path finds; "
               + "what changes is how much of the graph is looked at on the way, which on a large "
               + "grid or a street network is most of the solve time. Explored shows exactly what "
               + "was looked at — preview it against Dijkstra's Cost to see the difference.\n\n"
               + "It steers by the straight-line distance from each node to the target, so the graph "
               + "needs positions, and each connection must weigh at least its own length times "
               + "Estimate Scale — true of any graph weighted by length, and of Graph From Points "
               + "and Visibility Graph as they come. If weights are travel times, set Estimate Scale "
               + "to one over the fastest speed.\n\n"
               + "One source and one target only. For the cost to every node, or from the nearest "
               + "of several sources, use Dijkstra Shortest Path; on a graph of a few hundred nodes "
               + "use it anyway, since there is nothing to save.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("b9dcff21-0588-4d42-9fcb-756acd26bf7b");

    // The routes and flow tier.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords
        => new[] { "astar", "a star", "shortest path", "route", "pathfinding", "path finding", "heuristic", "navigation" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("astar", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "The graph to route over, with node positions. Each connection's weight is the cost of "
            + "travelling it and must not be negative. In a directed graph an arc is travelled from "
            + "its tail to its head only.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Source", "S", "The node the route starts from.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Target", "T", "The node the route ends at.", GH_ParamAccess.item);

        pManager.AddNumberParameter("Estimate Scale", "E",
            "What one unit of straight-line distance is worth in the graph's weights, at the very "
            + "least. 1 when weights are lengths. One over the fastest speed when they are times. "
            + "0 switches the estimate off, which makes this Dijkstra. Higher than the truth is "
            + "faster still and may return a route that is not the cheapest; the component warns "
            + "when the weights show that is possible.",
            GH_ParamAccess.item, 1.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Route", "R", "The route, source first. Empty when the target cannot be reached.", GH_ParamAccess.list);
        pManager.AddCurveParameter("Curve", "Cv", "The route as a polyline.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Cost", "C", "What the route costs. Null when the target cannot be reached.", GH_ParamAccess.item);

        pManager.AddIntegerParameter("Explored", "X",
            "Every node the search expanded, in the order it did — what it actually looked at. "
            + "List Item these against the graph's points to see the search reach for the target.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!GraphWire.TryGet(da, 0, out var placed)) return;

        int source = 0, target = 0;
        double scale = 1.0;
        if (!da.GetData(1, ref source) || !da.GetData(2, ref target) || !da.GetData(3, ref scale)) return;

        if (placed.Points is not { } points)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "This graph has no node positions, so there is no straight line to estimate with. Build "
                + "it with Points wired in, or use Dijkstra Shortest Path, which needs none.");
            return;
        }

        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale < 0.0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Estimate Scale must be zero or more.");
            return;
        }

        try
        {
            bool inPlan = ChooseMetric(placed, points, scale);
            int n = placed.NodeCount;
            if (target < 0 || target >= n)
                throw new ArgumentOutOfRangeException(nameof(target), target, $"Target {target} is outside 0..{n - 1}.");

            Point3d end = points[target];
            double Estimate(int node)
            {
                Point3d at = points[node];
                double dx = at.X - end.X, dy = at.Y - end.Y, dz = inPlan ? 0.0 : at.Z - end.Z;
                return scale * Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            int[] expanded;
            var result = placed.DirectedOrNull is { } directed
                ? AStar.Route(directed, source, target, Estimate, null, out expanded)
                : AStar.Route(placed.UndirectedOrNull!, source, target, Estimate, null, out expanded);

            var route = result.RouteTo(target);
            if (route.Length == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No route reaches the target. Connected Pieces shows whether the two are in the same piece"
                    + (placed.IsDirected ? "; in a directed graph the arcs may simply not lead there." : "."));

            da.SetDataList(0, route);
            da.SetData(1, placed.Trace(route) is { } line ? new GH_Curve(line.ToPolylineCurve()) : null);
            da.SetData(2, route.Length == 0 ? null : new GH_Number(result.Cost[target]));
            da.SetDataList(3, expanded);

            Message = $"explored {expanded.Distinct().Count()} of {n}";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    /// <summary>
    /// Picks the longest straight line the weights can vouch for, and warns when they
    /// can vouch for none.
    /// <para>
    /// A straight-line estimate is safe exactly when no connection weighs less than
    /// scale times its own length: then no route can beat the straight line, and
    /// the estimate never drops by more than the step it drops across. Distance in
    /// three dimensions is the stronger estimate and is used when every connection
    /// clears that bar. Graph From Points and Visibility Graph weigh connections by
    /// their length in plan, which a sloping connection falls short of in 3D — so
    /// the fallback is distance in plan, which those always clear.
    /// </para>
    /// </summary>
    /// <returns>True when the estimate should be measured in plan.</returns>
    private bool ChooseMetric(PlacedGraph placed, Point3d[] points, double scale)
    {
        if (scale == 0.0)
            return false;

        double tightest3D = double.PositiveInfinity, tightestPlan = double.PositiveInfinity;
        foreach (var (a, b, weight) in placed.Connections())
        {
            double length = points[a].DistanceTo(points[b]);
            double dx = points[a].X - points[b].X, dy = points[a].Y - points[b].Y;
            double plan = Math.Sqrt(dx * dx + dy * dy);

            if (length > 0.0) tightest3D = Math.Min(tightest3D, weight / length);
            if (plan > 0.0) tightestPlan = Math.Min(tightestPlan, weight / plan);
        }

        const double slack = 1.0 - 1e-9;
        if (tightest3D >= scale * slack)
            return false;
        if (tightestPlan >= scale * slack)
            return true;

        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
            $"Some connections weigh less than {scale:0.###} times their straight-line length, so the "
            + "estimate can overshoot and the route may not be the cheapest. Lower Estimate Scale to "
            + $"{Math.Max(tightestPlan, 0.0):0.###} or less, or use Dijkstra Shortest Path.");
        return true;
    }
}
