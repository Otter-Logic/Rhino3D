using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Graphs;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// The nodes a graph depends on to stay in one piece, and how much each one
/// holds on.
/// <para>
/// Adapter only. The search belongs to <see cref="CutVertices.Stranded"/>.
/// </para>
/// </summary>
public sealed class CutVerticesComponent : GH_Component
{
    public CutVerticesComponent()
        : base("Cut Vertices", "Cut",
               "Find the samples whose removal would split the graph, and how many others each one "
               + "would strand.\n\n"
               + "Every joint of a simple chain is a cut vertex, so the count alone says little; "
               + "Stranded is what says whether one matters. A node holding a large part of the "
               + "graph on by itself is a single point of failure — in a structure, a model, a "
               + "network or a supply chain.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("7b95bf21-3580-4294-a30a-8eebe11ea415");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("cutvertices", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Connectivity", "L", GraphData.ConnectivityDescription,
            GH_ParamAccess.tree);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Stranded", "S",
            "Per sample, how many others lose their connection to the largest remaining piece if it "
            + "is removed. Zero for most.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Cut Vertices", "C",
            "Samples whose removal strands at least one other, most stranding first.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(0, out GH_Structure<GH_Integer> connectivity)) return;

        int n = connectivity.Branches.Count;
        if (!GraphData.TryRead(connectivity, null, n, out var graph, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        var stranded = CutVertices.Stranded(graph!);
        var cuts = Enumerable.Range(0, n)
            .Where(i => stranded[i] > 0)
            .OrderByDescending(i => stranded[i]).ThenBy(i => i)
            .ToArray();

        da.SetDataList(0, stranded);
        da.SetDataList(1, cuts);

        Message = $"{cuts.Length} cut vertices";
    }
}
