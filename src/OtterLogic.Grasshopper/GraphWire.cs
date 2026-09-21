using Grasshopper.Kernel;
using OtterLogic.Graphs;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper;

/// <summary>
/// The Graph input every graph component shares, so they read it one way and
/// describe it in one set of words.
/// <para>
/// Registering it stays in each component: Grasshopper's parameter managers are
/// protected types nested in <c>GH_Component</c>, so nothing outside a component
/// can be handed one.
/// </para>
/// </summary>
internal static class GraphWire
{
    /// <summary>What a Graph input says unless the component has something more particular to say.</summary>
    public const string InputDescription =
        "The graph to read. Graph From Points and Visibility Graph build one from a drawing; "
        + "Graph From Connectivity builds one from a tree of neighbour indices — Proximity 3D's "
        + "Links, Neighbour Graph's Connectivity. Node positions ride along when they are given, "
        + "so results can come back as geometry.";

    /// <summary>Added to the description of an input that is only defined on an undirected graph.</summary>
    public const string UndirectedNote =
        "\n\nThis is only defined where connections run both ways. A directed graph is read with "
        + "its direction forgotten, and the component says so.";

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
