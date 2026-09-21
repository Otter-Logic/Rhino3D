using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using OtterLogic.Graphs;
using Rhino.Display;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Types;

/// <summary>
/// A graph of either kind and, when it came from geometry, where its nodes are.
/// <para>
/// The positions live here and not in the graph because Graphs references nothing
/// and knows nothing of geometry — a graph there is nodes and connections. Carrying
/// the points beside it is an adaptor's job: it is what lets a component hand a
/// route back as a polyline rather than as a list of indices the user has to look
/// up, and what gives A* a straight line to estimate with.
/// </para>
/// <para>
/// One wire for both kinds rather than a wire each, because most of what a user
/// does with a graph does not care: they build one, route over it, take it apart.
/// The two conversions are not alike, and the difference is kept visible. Reading
/// an undirected graph as directed loses nothing — every edge is an arc each way —
/// so <see cref="Directed"/> just does it. Reading a directed one as undirected
/// throws direction away, so <see cref="Undirected"/> says whether it had to, and
/// the component passes that on as a remark.
/// </para>
/// </summary>
public sealed class PlacedGraph
{
    private readonly WeightedGraph? _undirected;
    private readonly DirectedGraph? _directed;

    public PlacedGraph(WeightedGraph graph, Point3d[]? points)
    {
        _undirected = graph ?? throw new ArgumentNullException(nameof(graph));
        Points = points;
    }

    public PlacedGraph(DirectedGraph graph, Point3d[]? points)
    {
        _directed = graph ?? throw new ArgumentNullException(nameof(graph));
        Points = points;
    }

    /// <summary>One position per node, in node order, or null for a graph that has none.</summary>
    public Point3d[]? Points { get; }

    public bool IsDirected => _directed is not null;

    public int NodeCount => _directed?.NodeCount ?? _undirected!.NodeCount;

    /// <summary>Edges of an undirected graph; arcs of a directed one, an arc and its reverse counted separately.</summary>
    public int ConnectionCount => _directed?.ArcCount ?? _undirected!.EdgeCount;

    /// <summary>The directed graph, or null when this holds an undirected one.</summary>
    public DirectedGraph? DirectedOrNull => _directed;

    /// <summary>The undirected graph, or null when this holds a directed one.</summary>
    public WeightedGraph? UndirectedOrNull => _undirected;

    /// <summary>The graph as arcs. Lossless: an undirected edge becomes an arc each way.</summary>
    public DirectedGraph Directed() => _directed ?? _undirected!.ToDirected();

    /// <summary>
    /// The graph with direction forgotten, for what is only defined on an undirected
    /// one. Where an arc and its reverse disagreed the larger weight is kept, which
    /// is what <see cref="WeightedGraph"/> does with any edge it is told twice.
    /// </summary>
    /// <param name="lostDirection">Whether direction was thrown away to answer.</param>
    public WeightedGraph Undirected(out bool lostDirection)
    {
        lostDirection = _directed is not null;
        return _undirected ?? _directed!.ToUndirected(DuplicateArcs.KeepLargest);
    }

    /// <summary>
    /// Every connection in the order every per-connection output follows: edges once
    /// each, lower node first; arcs by tail then head, which is arc-id order.
    /// </summary>
    public IEnumerable<(int A, int B, double Weight)> Connections()
        => _directed is not null ? _directed.Arcs() : _undirected!.Edges();

    /// <summary>The polyline through <paramref name="route"/>, or null without positions or with fewer than two nodes.</summary>
    public Polyline? Trace(IReadOnlyList<int> route)
        => Points is null || route.Count < 2 ? null : new Polyline(route.Select(i => Points[i]));

    /// <summary>The line along a connection, or null without positions.</summary>
    public Line? Span(int a, int b) => Points is null ? null : new Line(Points[a], Points[b]);
}

/// <summary>
/// The Graph wire: one connection between components instead of a Connectivity
/// tree, a Weights tree and a list of points that must all be kept in step.
/// </summary>
public sealed class GH_Graph : GH_Goo<PlacedGraph>, IGH_PreviewData
{
    public GH_Graph() { }
    public GH_Graph(PlacedGraph graph) : base(graph) { }

    public override bool IsValid => Value is not null;
    public override string TypeName => "Graph";
    public override string TypeDescription => "An OtterLogic graph: nodes, weighted connections one-way or two-way, and node positions when it has them";

    // PlacedGraph never changes once built, so a duplicate may share it.
    public override IGH_Goo Duplicate() => new GH_Graph(Value);

    public override string ToString()
        => Value is null
            ? "<null graph>"
            : $"{(Value.IsDirected ? "Directed graph" : "Graph")} ({Value.NodeCount} nodes, "
              + $"{Value.ConnectionCount} {(Value.IsDirected ? "arcs" : "connections")}"
              + (Value.Points is null ? ")" : ", placed)");

    public BoundingBox ClippingBox
        => Value?.Points is null ? BoundingBox.Empty : new BoundingBox(Value.Points);

    public void DrawViewportWires(GH_PreviewWireArgs args)
    {
        if (Value?.Points is not { } points)
            return;

        foreach (var (a, b, _) in Value.Connections())
        {
            var line = new Line(points[a], points[b]);
            if (Value.IsDirected)
                args.Pipeline.DrawArrow(line, args.Color);
            else
                args.Pipeline.DrawLine(line, args.Color, args.Thickness);
        }

        args.Pipeline.DrawPoints(points, PointStyle.RoundSimple, 3, args.Color);
    }

    public void DrawViewportMeshes(GH_PreviewMeshArgs args) { }
}
