using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs.Methods;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Components.Graphs.Methods;

/// <summary>
/// Connected pieces as a method on a wire: no settings. No graph input; see
/// <see cref="DijkstraMethodComponent"/>.
/// </summary>
public sealed class ConnectedPiecesMethodComponent : GH_Component
{
    public ConnectedPiecesMethodComponent()
        : base("Connected Pieces", "Pieces",
               "The Connected Pieces method for OtterPath: split the graph into its separate pieces — sets "
               + "of nodes that can reach each other, with no connection between one set and the next.\n\n"
               + "Worth running before anything else here, and what OtterPath runs when nothing else is "
               + "wired. No route, flow or message crosses a gap, so a graph that was meant to be one piece "
               + "and is not explains most surprising answers downstream. Groups holds the pieces, Values "
               + "the piece per node, Marked the nodes connected to nothing; with Sources and Targets wired "
               + "the Report says whether they share a piece.\n\n"
               + "For the nodes that hold a single piece together, see Cut Vertices."
               + GraphWire.MethodFootnote,
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("1e9c4b7d-3a6f-4c2e-8d1b-9f4a7c2e5b36");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords
        => new[] { "connected components", "islands", "disjoint", "flood fill", "separate" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("connectedpieces", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphMethodParameter(), "Method", "M", GraphWire.MethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var method = new ConnectedPiecesMethod();
        da.SetData(0, new GH_GraphMethod(method));
        Message = method.Describe();
    }
}
