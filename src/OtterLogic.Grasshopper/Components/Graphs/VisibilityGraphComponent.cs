using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs.Planar;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// The graph whose shortest routes are the true shortest ways round a set of
/// obstacles.
/// <para>
/// Adapter only. The graph belongs to <see cref="VisibilityGraph.Of"/>.
/// </para>
/// </summary>
public sealed class VisibilityGraphComponent : GH_Component
{
    public VisibilityGraphComponent()
        : base("Visibility Graph", "VisGraph",
               "Join your points and every obstacle corner wherever one can see another in a "
               + "straight line, each connection weighing its length. Dijkstra Shortest Path over the "
               + "result gives the genuinely shortest way round the obstacles — straight where it "
               + "can be, turning only at corners — not the best of whatever a scatter of points "
               + "happened to offer.\n\n"
               + "Give it just the places routes start and end: your points come first in the "
               + "numbering, so with a start and an end wired in, Sources is 0 and Targets is 1. "
               + "Draw, move or add an obstacle and the route re-solves.\n\n"
               + "Routes hug the corners they turn at, so for clearance offset the obstacle curves "
               + "outwards first. For routes that should follow streets or a grid rather than cut "
               + "across open space, use Graph From Points. Read in plan: Z is ignored.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("950e914b-725a-4003-92de-935743f4fb9d");

    // The build tier: making a graph and taking one apart.
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords
        => new[] { "obstacles", "avoid", "shortest path", "route", "navigation", "line of sight", "euclidean", "path planning" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("visibilitygraph", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P",
            "Where routes start, end or must be able to pass. They become nodes 0, 1, 2… in list "
            + "order; obstacle corners are numbered after them.",
            GH_ParamAccess.list);

        pManager.AddCurveParameter("Obstacles", "O", ObstacleData.Description, GH_ParamAccess.list);

        pManager[0].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "The graph, each connection weighing its length in plan. It previews in the viewport.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Enclosed", "E",
            "Nodes inside an obstacle — a point placed in one, or a corner where two obstacles "
            + "overlap. They see nothing and no route reaches them.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var points = new List<Point3d>();
        if (Params.Input[0].VolatileDataCount > 0) da.GetDataList(0, points);

        var curves = new List<Curve?>();
        if (!da.GetDataList(1, curves)) return;

        try
        {
            var obstacles = ObstacleData.Read(curves, DocumentTolerance(), out var corners, out int skipped);
            if (skipped > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{skipped} obstacle curve(s) were left out: an obstacle must be a closed curve with some area.");

            var result = VisibilityGraph.Of(ObstacleData.InPlan(points), obstacles);
            int n = result.Graph.NodeCount;

            // Nodes are drawn where they were drawn, Z and all; only the arithmetic was in plan.
            var placed = points.Concat(corners).ToArray();
            if (placed.Length != n)
                placed = Enumerable.Range(0, n).Select(i => new Point3d(result.Nodes[i, 0], result.Nodes[i, 1], 0.0)).ToArray();

            var enclosed = Enumerable.Range(0, n).Where(i => result.Enclosed[i]).ToArray();
            int enclosedPoints = enclosed.Count(i => i < result.PointCount);
            if (enclosedPoints > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{enclosedPoints} of your points lie inside an obstacle, so no route reaches them. See Enclosed.");

            da.SetData(0, new GH_Graph(new PlacedGraph(result.Graph, placed)));
            da.SetDataList(1, enclosed);

            Message = $"{n} nodes, {result.Graph.EdgeCount} connections";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
