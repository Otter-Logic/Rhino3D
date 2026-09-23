using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs.Methods;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Components.Graphs.Methods;

/// <summary>
/// Breadth-first search as a method on a wire: no settings. No graph input; see
/// <see cref="DijkstraMethodComponent"/>.
/// </summary>
public sealed class BreadthFirstMethodComponent : GH_Component
{
    public BreadthFirstMethodComponent()
        : base("Breadth-First", "BFS",
               "The Breadth-First method for OtterPath: the fewest steps from the sources to every node, "
               + "ignoring what the connections weigh.\n\n"
               + "Groups is usually what is wanted: every node one step out, then two, then three — the "
               + "generations of a mesh outward from a seed, the joints from a support, how far a change "
               + "spreads. Several sources grow their rings together. Values is the step count per node.\n\n"
               + "When connections have lengths or costs that should count, use Dijkstra; with every "
               + "weight at 1 the two agree exactly."
               + GraphWire.MethodFootnote,
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("9b4e2c7a-5f1d-4a8e-9c3b-7d2e1f8a6b41");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords
        => new[] { "bfs", "breadth-first search", "hops", "steps", "rings", "levels", "flood fill", "topological distance" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("breadthfirst", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphMethodParameter(), "Method", "M", GraphWire.MethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var method = new BreadthFirstMethod();
        da.SetData(0, new GH_GraphMethod(method));
        Message = method.Describe();
    }
}
