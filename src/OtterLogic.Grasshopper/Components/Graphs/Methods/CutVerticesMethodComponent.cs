using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs.Methods;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Components.Graphs.Methods;

/// <summary>
/// Cut vertices as a method on a wire: no settings. No graph input; see
/// <see cref="DijkstraMethodComponent"/>.
/// </summary>
public sealed class CutVerticesMethodComponent : GH_Component
{
    public CutVerticesMethodComponent()
        : base("Cut Vertices", "Cut",
               "The Cut Vertices method for OtterPath: the nodes whose removal would split the graph, how "
               + "many others each one would strand, and the bridges — the connections that are the only "
               + "link between what lies either side of them.\n\n"
               + "Every joint of a simple chain is a cut vertex, so the count alone says little; Values, "
               + "the number stranded, is what says whether one matters. A node holding a large part of "
               + "the graph on by itself is a single point of failure — in a structure, a model, a network "
               + "or a supply chain. Marked lists the cut vertices, most stranding first; Connection Values "
               + "flags the bridges and Groups holds their two ends.\n\n"
               + "For the pieces a graph is already in, use Connected Pieces; for the nodes routes lean on "
               + "without depending on, Betweenness."
               + GraphWire.MethodFootnote,
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("8c3a6e1f-7d2b-4f9a-a4c8-2e7b1d5f9a47");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords
        => new[] { "articulation points", "bridges", "tarjan", "single point of failure", "robustness", "biconnected", "weak points" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("cutvertices", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphMethodParameter(), "Method", "M", GraphWire.MethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var method = new CutVerticesMethod();
        da.SetData(0, new GH_GraphMethod(method));
        Message = method.Describe();
    }
}
