using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs;
using OtterLogic.Grasshopper.Parameters.Graphs;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// The separate pieces of a graph.
/// <para>
/// Adapter only. The search belongs to <see cref="WeightedGraph.ConnectedComponents"/>.
/// Called pieces on the canvas rather than components, because in Grasshopper a
/// component is the thing the user just dropped.
/// </para>
/// </summary>
public sealed class ConnectedPiecesComponent : GH_Component
{
    public ConnectedPiecesComponent()
        : base("Connected Pieces", "Pieces",
               "Split a graph into its separate pieces — sets of nodes that can reach each other, "
               + "with no connection between one set and the next.\n\n"
               + "Worth running before anything else here. No route, flow or message crosses a gap, "
               + "so a graph that was meant to be one piece and is not explains most surprising "
               + "answers downstream: an unreached node, a null cost, a cluster that will not merge. "
               + "A piece of one node is a node connected to nothing. For nodes that hold a single "
               + "piece together, see Cut Vertices.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("24409a00-3397-42a6-9be6-c8dccdefc0a5");

    // The structure tier: what the graph is made of, before anything travels over it.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords
        => new[] { "connected components", "islands", "disjoint", "flood fill", "separate" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("connectedpieces", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G", GraphWire.InputDescription + GraphWire.UndirectedNote, GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Piece", "P",
            "Per node, the piece it belongs to. Pieces are numbered by their lowest node, so the "
            + "numbering does not change with the order connections were listed in.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Nodes", "N",
            "One branch per piece, holding its nodes.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Count", "C", "How many pieces there are.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!GraphWire.TryGetUndirected(this, da, 0, out _, out var graph)) return;

        int n = graph.NodeCount;
        var piece = graph.ConnectedComponents(out int count);

        // Pieces are numbered by their lowest node, so grouping the nodes in order
        // meets the pieces in order too.
        var nodes = Enumerable.Range(0, n)
            .GroupBy(i => piece[i])
            .Select(group => group.ToArray())
            .ToArray();

        da.SetDataList(0, piece);
        da.SetDataTree(1, Trees.FromBuckets(nodes));
        da.SetData(2, count);

        Message = count == 1 ? "1 piece" : $"{count} pieces";
    }
}
