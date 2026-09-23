using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs.Methods;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Components.Graphs.Methods;

/// <summary>
/// Betweenness as a method on a wire: one setting a user never needs to move.
/// No graph input; see <see cref="DijkstraMethodComponent"/>.
/// </summary>
public sealed class BetweennessMethodComponent : GH_Component
{
    public BetweennessMethodComponent()
        : base("Betweenness", "Between",
               "The Betweenness method for OtterPath: score every node by the share of shortest routes "
               + "between all other pairs that pass through it, 0 to 1.\n\n"
               + "High scores are the bottlenecks and bridges — the nodes the rest of the graph leans on "
               + "to reach itself. Values is the score per node and Marked the ten busiest, busiest first. "
               + "Routes are counted in steps; weights play no part, and neither do Sources or Targets.\n\n"
               + "For the nodes whose loss would actually split the graph, rather than merely slow it, "
               + "use Cut Vertices."
               + GraphWire.MethodFootnote,
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("6a2e8d3c-4b9f-4e1a-b7c2-3d8f1a6e9c25");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords
        => new[] { "centrality", "brandes", "bottleneck", "hub", "congestion", "importance", "busiest" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("betweenness", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Maximum Sources", "M",
            "Past this many nodes the score is estimated from this many evenly spread starting points "
            + "rather than all of them, which keeps a large graph interactive. Evenly spread, not random, "
            + "so the answer is the same every solve. Raise it for the exact value, at the cost of solve time.",
            GH_ParamAccess.item, 2000);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphMethodParameter(), "Method", "M", GraphWire.MethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        int sources = 2000;
        if (!da.GetData(0, ref sources)) return;

        var method = new BetweennessMethod { MaximumSources = sources };
        da.SetData(0, new GH_GraphMethod(method));
        Message = method.Describe();
    }
}
