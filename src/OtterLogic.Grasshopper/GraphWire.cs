using Grasshopper.Kernel;
using OtterLogic.Graphs;
using OtterLogic.Graphs.Methods;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper;

/// <summary>
/// The two wires the Graphs panel runs on — the Graph, and the Method that goes
/// into OtterPath — read one way and described in one set of words, so nine
/// small components cannot describe the same wire nine ways.
/// <para>
/// Registering stays in each component: Grasshopper's parameter managers are
/// protected types nested in <c>GH_Component</c>, so nothing outside a component
/// can be handed one. Only the reading and the wording are shared, as
/// <see cref="MethodWire"/> does for the machine learning wires.
/// </para>
/// </summary>
internal static class GraphWire
{
    /// <summary>What a Graph input says unless the component has something more particular to say.</summary>
    public const string InputDescription =
        "The graph to read. Graph From Lines, Graph From Points and Visibility Graph build one from a "
        + "drawing; Graph From Connectivity builds one from a tree of neighbour indices — Proximity 3D's "
        + "Links, Neighbour Graph's Connectivity. Node positions ride along when they are given, "
        + "so results can come back as geometry.";

    /// <summary>Added to the description of an input that is only defined on an undirected graph.</summary>
    public const string UndirectedNote =
        "\n\nThis is only defined where connections run both ways. A directed graph is read with "
        + "its direction forgotten, and the component says so.";

    /// <summary>What every graph method component says about its one output.</summary>
    public const string MethodOutput =
        "This method with these settings. Wire it into OtterPath's Method input; the graph, sources and "
        + "targets are wired there, not here.";

    /// <summary>The sentence every method component ends on.</summary>
    public const string MethodFootnote =
        "\n\nThis component takes no graph: wire its Method output into OtterPath.";

    /// <summary>What OtterPath's Sources input says.</summary>
    public const string SourcesDescription =
        "Optional. The nodes the question starts from — where routes begin, where something enters. "
        + "Node indices: to find one from a point, use Closest Point against Deconstruct Graph's "
        + "Points. What each method makes of them is in its description; with no Method wired, "
        + "sources ask for the cheapest route from them.";

    /// <summary>What OtterPath's Targets input says.</summary>
    public const string TargetsDescription =
        "Optional. The nodes the question ends at — where routes go, where everything drains. Leave "
        + "empty to route to every node.";

    /// <summary>The method at <paramref name="index"/>, or null when nothing is wired — which is a valid choice, not an error.</summary>
    public static GraphMethod? ReadMethod(IGH_DataAccess da, int index)
    {
        GH_GraphMethod? goo = null;
        return da.GetData(index, ref goo) ? goo?.Value : null;
    }

    /// <summary>
    /// The graph, its positions and the question, as the library takes them. Points
    /// become plain rows of x, y, z here and nowhere else.
    /// </summary>
    /// <exception cref="ArgumentException">A source or target is not a node of the graph.</exception>
    public static PathQuery ToQuery(PlacedGraph placed, IEnumerable<int>? sources, IEnumerable<int>? targets)
    {
        double[,]? positions = null;
        if (placed.Points is { } points)
        {
            positions = new double[points.Length, 3];
            for (int i = 0; i < points.Length; i++)
            {
                positions[i, 0] = points[i].X;
                positions[i, 1] = points[i].Y;
                positions[i, 2] = points[i].Z;
            }
        }

        return placed.DirectedOrNull is { } directed
            ? new PathQuery(directed, positions, sources, targets)
            : new PathQuery(placed.UndirectedOrNull!, positions, sources, targets);
    }

    /// <summary>Reads the graph at <paramref name="index"/>; false, with nothing further to say, when it is missing.</summary>
    public static bool TryGet(IGH_DataAccess da, int index, out PlacedGraph graph)
    {
        GH_Graph? goo = null;
        if (da.GetData(index, ref goo) && goo?.Value is not null)
        {
            graph = goo.Value;
            return true;
        }

        graph = null!;
        return false;
    }

    /// <summary>
    /// Reads the graph for a component that needs an undirected one, and tells the
    /// user when direction had to be thrown away to give it one.
    /// <para>
    /// A remark and not an error: the answer is a true one about the network with
    /// its one-way signs taken down, which is often exactly what was wanted — a cut
    /// vertex is a cut vertex whichever way the traffic runs. It is said out loud
    /// because it is the one conversion here that loses something.
    /// </para>
    /// </summary>
    public static bool TryGetUndirected(
        GH_Component owner, IGH_DataAccess da, int index, out PlacedGraph placed, out WeightedGraph graph)
    {
        graph = null!;
        if (!TryGet(da, index, out placed))
            return false;

        try
        {
            graph = placed.Undirected(out bool lostDirection);
            if (lostDirection)
                owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "The graph is directed and this is only defined on an undirected one, so direction "
                    + "was ignored: an arc and its reverse were read as one two-way connection.");

            return true;
        }
        catch (ArgumentException ex)
        {
            owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "The directed graph could not be read as an undirected one: " + ex.Message);
            return false;
        }
    }
}
