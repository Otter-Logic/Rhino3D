using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;
using OtterLogic.MachineLearning.Distances;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// Builds a graph to route over from a set of points, leaving out whatever the
/// obstacles block.
/// <para>
/// Adapter only. The neighbours and the lengths belong to
/// <see cref="NeighbourGraph.ByDistance"/>, and what is blocked to
/// <c>PlanarObstacles</c> in Graphs.
/// </para>
/// </summary>
public sealed class GraphFromPointsComponent : GH_Component
{
    public GraphFromPointsComponent()
        : base("Graph From Points", "PtGraph",
               "Join every point to its nearest few, weigh each connection by its length, and leave "
               + "out any connection that would pass through an obstacle. The graph is ready for "
               + "Dijkstra Shortest Path as it comes.\n\n"
               + "Node i is point i, obstacles or not: a point inside an obstacle keeps its number "
               + "and is simply connected to nothing, so indices picked from your own point list "
               + "stay good. Move an obstacle and the routes re-solve.\n\n"
               + "A route can only ever follow the connections given, so it is as good as the "
               + "points are dense — scatter or grid plenty of them across the open space. For the "
               + "true shortest way round obstacles from a handful of points, use Visibility Graph. "
               + "Read in plan: lengths and obstacles ignore Z.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("884c2ea0-6a96-4c93-83da-c803c148836f");

    // The build tier: making a graph and taking one apart.
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords
        => new[] { "proximity", "nearest neighbours", "network", "obstacles", "avoid", "route", "navigation" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("graphfrompoints", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P",
            "The nodes, one per point. List order is the node numbering.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Neighbours", "N",
            "How many of its nearest points each point reaches for. A connection is kept if either "
            + "end chose it. Around 6 to 8 suits a scatter or a grid with diagonals; a blocked "
            + "connection still uses up one of the count, so go higher among dense obstacles.",
            GH_ParamAccess.item, 6);

        pManager.AddNumberParameter("Max Distance", "D",
            "Optional. Connections longer than this are left out — what stops a point at the edge "
            + "of a gap from reaching across it.",
            GH_ParamAccess.item);

        pManager.AddCurveParameter("Obstacles", "O", "Optional. " + ObstacleData.Description, GH_ParamAccess.list);

        pManager[2].Optional = true;
        pManager[3].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "The graph, each connection weighing its length in plan. It previews in the viewport.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Enclosed", "E",
            "Points inside an obstacle. They keep their node numbers and connect to nothing.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var points = new List<Point3d>();
        if (!da.GetDataList(0, points)) return;

        int neighbours = 6;
        if (!da.GetData(1, ref neighbours)) return;

        double maximum = double.PositiveInfinity;
        if (Params.Input[2].VolatileDataCount > 0 && !da.GetData(2, ref maximum)) return;

        var curves = new List<Curve?>();
        if (Params.Input[3].VolatileDataCount > 0) da.GetDataList(3, curves);

        int n = points.Count;
        if (n < 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A graph needs at least two points.");
            return;
        }

        if (neighbours > n - 1)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Neighbours lowered from {neighbours} to {n - 1}: there are only {n} points.");
            neighbours = n - 1;
        }

        try
        {
            var obstacles = ObstacleData.Read(curves, DocumentTolerance(), out _, out int skipped);
            if (skipped > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{skipped} obstacle curve(s) were left out: an obstacle must be a closed curve with some area.");

            var x = ObstacleData.InPlan(points);
            var enclosed = Enumerable.Range(0, n).Select(i => obstacles.Contains(x[i, 0], x[i, 1])).ToArray();

            var graph = NeighbourGraph.ByDistance(x, neighbours, maximum,
                obstacles.Count == 0
                    ? null
                    : (a, b) => enclosed[a] || enclosed[b] || obstacles.Blocks(x[a, 0], x[a, 1], x[b, 0], x[b, 1]));

            // Enclosed points are pieces of one by design, so they are not what this warns about.
            var piece = graph.ConnectedComponents(out _);
            int pieces = Enumerable.Range(0, n).Where(i => !enclosed[i]).Select(i => piece[i]).Distinct().Count();
            if (pieces > 1)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"The open points fall into {pieces} separate pieces, and no route crosses between "
                    + "pieces. Raise Neighbours or Max Distance, or add points across the gap. "
                    + "Connected Pieces shows which is which.");

            da.SetData(0, new GH_Graph(new PlacedGraph(graph, points.ToArray())));
            da.SetDataList(1, Enumerable.Range(0, n).Where(i => enclosed[i]));

            Message = $"{n} nodes, {graph.EdgeCount} connections";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
