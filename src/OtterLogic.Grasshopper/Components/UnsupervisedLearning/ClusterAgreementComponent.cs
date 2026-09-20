using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// How far two labellings of the same samples agree, whatever numbers either
/// of them used.
/// <para>
/// Adapter only. The measure belongs to <see cref="ClusterAgreement"/>.
/// </para>
/// </summary>
public sealed class ClusterAgreementComponent : GH_Component
{
    public ClusterAgreementComponent()
        : base("Cluster Agreement", "Agree",
               "Measure how far two labellings of the same samples agree — the adjusted Rand index: "
               + "1 for the same grouping, about 0 for no better than chance.\n\n"
               + "Renumbering either side changes nothing, so it compares groupings, not label "
               + "values. Use it to see whether two methods found the same structure, whether a "
               + "result survives a change of features or seed, or how close a clustering comes to "
               + "a grouping somebody made by hand. A sample at -1 counts as a group of its own.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("c2ffaca0-d4b4-4c0d-ab11-33554825302e");

    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("clusteragreement", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Labels A", "A",
            "A cluster index per sample — any method's Result.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Labels B", "B",
            "Another labelling of the same samples, in the same order.",
            GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Adjusted Rand", "ARI",
            "1 for the same grouping, about 0 for agreement no better than chance, negative for "
            + "worse. Adjusted, because two groupings into many small clusters agree on most pairs "
            + "simply because most pairs are apart in both.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var a = new List<int>();
        var b = new List<int>();
        if (!da.GetDataList(0, a)) return;
        if (!da.GetDataList(1, b)) return;

        try
        {
            double index = ClusterAgreement.AdjustedRand(a.ToArray(), b.ToArray());
            da.SetData(0, index);
            Message = $"ARI {index:0.00}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
