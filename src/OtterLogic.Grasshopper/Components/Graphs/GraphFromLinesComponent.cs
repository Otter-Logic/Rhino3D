using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs.Spatial;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// Builds the Graph wire from drawn lines, welding their ends into nodes.
/// <para>
/// Adapter only. The welding belongs to <see cref="LineNetwork.Weld"/> on plain
/// coordinates; what this adds is reading each curve's ends and its length, and
/// handing the welded node positions back as points so the graph draws and
/// answers in geometry.
/// </para>
/// </summary>
public sealed class GraphFromLinesComponent : GH_Component
{
    public GraphFromLinesComponent()
        : base("Graph From Lines", "LnGraph",
               "Build a graph from drawn lines, in three dimensions: every line is a connection, and "
               + "ends that meet within the tolerance are one node. Centrelines, members, a street map, "
               + "a duct run — whatever a network looks like when it is drawn.\n\n"
               + "Each connection weighs its curve's length, so the graph is ready for OtterPath as it "
               + "comes: a route is the shortest way along the lines. Wire Weights to say something else "
               + "— a time, a cost, a capacity — one per line. Nodes are numbered in the order the lines "
               + "arrive, start before end; Deconstruct Graph's Points shows where each one is.\n\n"
               + "Ends that miss each other by more than the tolerance stay separate nodes, and the graph "
               + "falls into pieces; the component says so. Raise Tolerance, or snap the drawing first. "
               + "For a graph that goes round obstacles rather than along lines, use Graph From Points.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("b5e1c8a2-9f4d-4b7e-8c3a-6d2f9e1b4c69");

    // The build tier: making a graph and taking one apart.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords
        => new[] { "lines", "curves", "network", "weld", "join", "topology", "centrelines", "3d" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("graphfromlines", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Lines", "L",
            "The connections, as lines or curves. Each becomes one connection between the nodes at its "
            + "two ends; a curve is followed for its length but drawn as a straight connection.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Weights", "W",
            "Optional. One number per line — a time, a cost, how readily it carries. Unwired, each "
            + "connection weighs its length, which is what a route wants.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Tolerance", "T",
            "Optional. How far apart two ends may be and still be one node. Unwired, the document tolerance.",
            GH_ParamAccess.item);

        pManager[1].Optional = true;
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "The graph, each connection weighing its length unless Weights says otherwise. It previews "
            + "in the viewport.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Collapsed", "X",
            "Lines shorter than the tolerance, whose two ends became one node. They make no connection.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var curves = new List<Curve?>();
        if (!da.GetDataList(0, curves)) return;

        var weights = new List<double>();
        bool weighted = Params.Input[1].VolatileDataCount > 0 && da.GetDataList(1, weights);

        double tolerance = DocumentTolerance();
        if (Params.Input[2].VolatileDataCount > 0 && !da.GetData(2, ref tolerance)) return;

        var kept = curves.Where(c => c is not null).Cast<Curve>().ToList();
        if (kept.Count < curves.Count)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{curves.Count - kept.Count} null curve(s) were left out.");

        if (kept.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A graph needs at least one line.");
            return;
        }

        if (weighted && weights.Count != curves.Count)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Weights holds {weights.Count} value(s) but there are {curves.Count} lines. There must be one per line.");
            return;
        }

        int m = kept.Count;
        var starts = new double[m, 3];
        var ends = new double[m, 3];
        var lengths = new double[m];
        for (int i = 0; i < m; i++)
        {
            Point3d a = kept[i].PointAtStart, b = kept[i].PointAtEnd;
            starts[i, 0] = a.X; starts[i, 1] = a.Y; starts[i, 2] = a.Z;
            ends[i, 0] = b.X; ends[i, 1] = b.Y; ends[i, 2] = b.Z;
            lengths[i] = kept[i].GetLength();
        }

        try
        {
            var network = LineNetwork.Weld(starts, ends, tolerance);

            // Null curves were dropped above, so weights are re-paired with the curves kept.
            IReadOnlyList<double> perLine = weighted
                ? curves.Select((c, i) => (c, w: weights[i])).Where(p => p.c is not null).Select(p => p.w).ToArray()
                : lengths;
            var graph = network.Graph(perLine);

            var points = new Point3d[network.NodeCount];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Point3d(network.Nodes[i, 0], network.Nodes[i, 1], network.Nodes[i, 2]);

            if (network.Collapsed.Length > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{network.Collapsed.Length} line(s) are shorter than the tolerance and make no connection. See Collapsed.");

            graph.ConnectedComponents(out int pieces);
            if (pieces > 1)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"The lines fall into {pieces} separate pieces, and no route crosses between pieces. Ends that "
                    + "should meet may miss by more than the tolerance; OtterPath with nothing else wired shows which is which.");

            da.SetData(0, new GH_Graph(new PlacedGraph(graph, points)));
            da.SetDataList(1, network.Collapsed);

            Message = $"{network.NodeCount} nodes, {graph.EdgeCount} connections";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
