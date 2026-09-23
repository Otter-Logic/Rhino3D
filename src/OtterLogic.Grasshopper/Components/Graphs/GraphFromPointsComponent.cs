using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;
using OtterLogic.MachineLearning.Distances;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// Builds a graph to route over from a set of points in space, leaving out
/// whatever the obstacles block.
/// <para>
/// Adapter only. The neighbours and the lengths belong to
/// <see cref="NeighbourGraph.ByDistance"/>, and what is blocked to
/// <c>PlanarObstacles</c> and <c>SolidObstacles</c> in Graphs. Two kinds of
/// obstacle because a Rhino user draws both: a closed curve is a footprint read
/// in plan and standing at every height, which is how a plan is drawn; a mesh or
/// Brep is a solid where it is. Distances are measured in three dimensions, so a
/// scatter of points through a building routes between floors as readily as
/// across one.
/// </para>
/// </summary>
public sealed class GraphFromPointsComponent : GH_Component
{
    public GraphFromPointsComponent()
        : base("Graph From Points", "PtGraph",
               "Join every point to its nearest few, weigh each connection by its length in three "
               + "dimensions, and leave out any connection that would pass through an obstacle. The "
               + "graph is ready for OtterPath as it comes.\n\n"
               + "Node i is point i, obstacles or not: a point inside an obstacle keeps its number and is "
               + "simply connected to nothing, so indices picked from your own point list stay good. Move "
               + "an obstacle and the routes re-solve. Obstacles come two ways: closed curves are "
               + "footprints, read in plan and blocking at every height; Solids are meshes and Breps, "
               + "blocking where they are.\n\n"
               + "A route can only ever follow the connections given, so it is as good as the points are "
               + "dense — scatter or grid plenty of them through the open space. For the true shortest way "
               + "round obstacles in plan from a handful of points, use Visibility Graph; for a network "
               + "that is already drawn, Graph From Lines.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("884c2ea0-6a96-4c93-83da-c803c148836f");

    // The build tier: making a graph and taking one apart.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords
        => new[] { "proximity", "nearest neighbours", "network", "obstacles", "avoid", "route", "navigation", "3d" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("graphfrompoints", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P",
            "The nodes, one per point. List order is the node numbering.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Neighbours", "N",
            "How many of its nearest points each point reaches for. A connection is kept if either "
            + "end chose it. Around 6 to 8 suits a scatter or a grid with diagonals in plan, more for "
            + "a grid in three dimensions; a blocked connection still uses up one of the count, so go "
            + "higher among dense obstacles.",
            GH_ParamAccess.item, 6);

        pManager.AddNumberParameter("Max Distance", "D",
            "Optional. Connections longer than this are left out — what stops a point at the edge "
            + "of a gap from reaching across it.",
            GH_ParamAccess.item);

        pManager.AddCurveParameter("Obstacles", "O", "Optional. " + ObstacleData.Description, GH_ParamAccess.list);

        pManager.AddGeometryParameter("Solids", "S", ObstacleData.SolidsDescription, GH_ParamAccess.list);

        pManager[2].Optional = true;
        pManager[3].Optional = true;
        pManager[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "The graph, each connection weighing its length. It previews in the viewport.",
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

        var geometry = new List<GeometryBase?>();
        if (Params.Input[4].VolatileDataCount > 0) da.GetDataList(4, geometry);

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
            double tolerance = DocumentTolerance();
            var footprints = ObstacleData.Read(curves, tolerance, out _, out int skippedCurves);
            if (skippedCurves > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{skippedCurves} obstacle curve(s) were left out: an obstacle must be a closed curve with some area.");

            var solids = ObstacleData.ReadSolids(geometry, tolerance, out int skippedSolids);
            if (skippedSolids > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{skippedSolids} solid(s) were left out: a solid must be a mesh or a Brep with at least one face.");

            var x = ObstacleData.InSpace(points);
            bool any = footprints.Count > 0 || solids.Count > 0;

            var enclosed = new bool[n];
            for (int i = 0; i < n; i++)
                enclosed[i] = footprints.Contains(x[i, 0], x[i, 1]) || solids.Contains(x[i, 0], x[i, 1], x[i, 2]);

            bool Blocked(int a, int b)
                => enclosed[a] || enclosed[b]
                   || footprints.Blocks(x[a, 0], x[a, 1], x[b, 0], x[b, 1])
                   || solids.Blocks(x[a, 0], x[a, 1], x[a, 2], x[b, 0], x[b, 1], x[b, 2]);

            var graph = NeighbourGraph.ByDistance(x, neighbours, maximum, any ? Blocked : null);

            // Enclosed points are pieces of one by design, so they are not what this warns about.
            var piece = graph.ConnectedComponents(out _);
            int pieces = Enumerable.Range(0, n).Where(i => !enclosed[i]).Select(i => piece[i]).Distinct().Count();
            if (pieces > 1)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"The open points fall into {pieces} separate pieces, and no route crosses between "
                    + "pieces. Raise Neighbours or Max Distance, or add points across the gap. OtterPath "
                    + "with nothing else wired shows which is which.");

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
