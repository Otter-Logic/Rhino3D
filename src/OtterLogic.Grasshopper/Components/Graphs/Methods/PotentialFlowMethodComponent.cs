using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs.Methods;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Components.Graphs.Methods;

/// <summary>
/// Potential flow as a method on a wire: one setting, how much enters at each
/// source. No graph input; see <see cref="DijkstraMethodComponent"/>.
/// </summary>
public sealed class PotentialFlowMethodComponent : GH_Component
{
    public PotentialFlowMethodComponent()
        : base("Potential Flow", "Flow",
               "The Potential Flow method for OtterPath: how much passes along every connection when "
               + "something enters the graph and drains at the targets — current in a resistor network, "
               + "heat in a conductor, footfall towards the exits, load towards the supports.\n\n"
               + "Targets are the drains and are the one thing it needs. Sources are where things enter; "
               + "leave them empty and the same amount enters at every node, which is 'everything drains "
               + "from everywhere'. Connection Values is the flow along each connection; Values is the "
               + "potential at each node, zero at a drain. Each connection's weight is how readily it "
               + "carries, so for a connection that gets harder with length, build the graph with one "
               + "over its length.\n\n"
               + "Dijkstra says which way is nearest and has to pick one; this says how much goes each way "
               + "and does not pick. Where two routes are as good as each other the flow divides between them."
               + GraphWire.MethodFootnote,
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("4d7c1e9b-8a2f-4d6c-a3e1-5b9c2f7d8e14");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords
        => new[] { "laplacian", "resistance", "current", "kirchhoff", "random walk", "diffusion", "load path", "crowd" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("potentialflow", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Injection", "I",
            "What enters at each source — or at every node, when no source is wired. Negative draws off. "
            + "The size of every flow scales with it, so 1 is the usual choice.",
            GH_ParamAccess.item, 1.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphMethodParameter(), "Method", "M", GraphWire.MethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        double injection = 1.0;
        if (!da.GetData(0, ref injection)) return;

        var method = new PotentialFlowMethod { Injection = injection };
        da.SetData(0, new GH_GraphMethod(method));
        Message = method.Describe();
    }
}
