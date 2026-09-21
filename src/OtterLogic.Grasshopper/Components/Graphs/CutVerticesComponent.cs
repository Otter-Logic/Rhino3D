using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using OtterLogic.Graphs;
using OtterLogic.Grasshopper.Parameters.Graphs;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// The nodes and connections a graph depends on to stay in one piece.
/// <para>
/// Adapter only. The search belongs to <see cref="CutVertices.Of"/>.
/// </para>
/// </summary>
public sealed class CutVerticesComponent : GH_Component
{
    public CutVerticesComponent()
        : base("Cut Vertices", "Cut",
               "Find the nodes whose removal would split the graph, how many others each one would "
               + "strand, and the connections that are the only link between what lies either side "
               + "of them.\n\n"
               + "Every joint of a simple chain is a cut vertex, so the count alone says little; "
               + "Stranded is what says whether one matters. A node holding a large part of the "
               + "graph on by itself is a single point of failure — in a structure, a model, a "
               + "network or a supply chain. Bridges are the same question asked of connections: "
               + "lose one and the graph is in two. For the pieces a graph is already in, see "
               + "Connected Pieces.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("7b95bf21-3580-4294-a30a-8eebe11ea415");

    // The structure tier: what the graph is made of, before anything travels over it.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords
        => new[] { "articulation points", "bridges", "tarjan", "single point of failure", "robustness", "biconnected" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("cutvertices", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G", GraphWire.InputDescription + GraphWire.UndirectedNote, GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Stranded", "S",
            "Per node, how many others lose their connection to the largest remaining piece if it "
            + "is removed. Zero for most.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Cut Vertices", "C",
            "Nodes whose removal strands at least one other, most stranding first.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Bridges", "B",
            "One branch per bridge, holding the two nodes it joins, lower first.",
            GH_ParamAccess.tree);

        pManager.AddLineParameter("Bridge Lines", "L",
            "The bridges as lines, where the graph has positions.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!GraphWire.TryGetUndirected(this, da, 0, out var placed, out var graph)) return;

        var result = CutVertices.Of(graph);
        var stranded = result.Stranded;

        var cuts = result.ArticulationPoints()
            .OrderByDescending(i => stranded[i]).ThenBy(i => i)
            .ToArray();

        da.SetDataList(0, stranded);
        da.SetDataList(1, cuts);
        da.SetDataTree(2, Trees.FromBuckets(result.Bridges.Select(b => new[] { b.A, b.B }).ToArray()));
        da.SetDataList(3, result.Bridges
            .Select(b => placed.Span(b.A, b.B))
            .Where(line => line.HasValue)
            .Select(line => new GH_Line(line!.Value)));

        Message = $"{cuts.Length} cut, {result.Bridges.Length} bridge{(result.Bridges.Length == 1 ? "" : "s")}";
    }
}
