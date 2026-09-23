using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Graphs.Methods;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Components.Graphs.Methods;

/// <summary>
/// Dependency levels as a method on a wire: no settings. No graph input; see
/// <see cref="DijkstraMethodComponent"/>.
/// </summary>
public sealed class DependencyLevelsMethodComponent : GH_Component
{
    public DependencyLevelsMethodComponent()
        : base("Dependency Levels", "Levels",
               "The Dependency Levels method for OtterPath: rank every node by the longest chain of things "
               + "it depends on — level 0 depends on nothing, level 1 only on level 0, and so on. Everything "
               + "on one level can go ahead together once the levels below it are done: an erection "
               + "sequence, an assembly order, which element carries which.\n\n"
               + "Groups holds one branch per level, lowest first; Values is the level per node. Dependence "
               + "that runs in a circle is not an order at all, so every cycle is folded into one group "
               + "whose nodes share a level, listed in Marked and named in the Report.\n\n"
               + "It needs a directed graph, since an order needs a direction: build one with Graph From "
               + "Connectivity and Directed set, each branch listing what that node depends on."
               + GraphWire.MethodFootnote,
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("2f7d9a4c-6e1b-4d8f-b9a3-1c5e8f2a7d58");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords
        => new[] { "topological sort", "strongly connected components", "scc", "tarjan", "dag", "sequence", "hierarchy", "condensation" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("dependencylevels", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new GraphMethodParameter(), "Method", "M", GraphWire.MethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var method = new DependencyLevelsMethod();
        da.SetData(0, new GH_GraphMethod(method));
        Message = method.Describe();
    }
}
