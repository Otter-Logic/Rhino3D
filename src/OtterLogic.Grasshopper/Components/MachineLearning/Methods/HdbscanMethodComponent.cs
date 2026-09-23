using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Methods;

/// <summary>
/// HDBSCAN as a method on a wire: the smallest group worth calling a cluster,
/// and nothing else. No data input; see <see cref="KMeansMethodComponent"/>.
/// </summary>
public sealed class HdbscanMethodComponent : GH_Component
{
    public HdbscanMethodComponent()
        : base("HDBSCAN", "HDBSCAN",
               "The HDBSCAN method for OtterCluster: find clusters as dense regions of any shape, and "
               + "leave the samples that belong to none of them unplaced.\n\n"
               + "Not told how many clusters to look for — it reports however many the data supports "
               + "— and the only method here that can say a sample is an outlier rather than filing it "
               + "in the nearest cluster. Use it when clusters are irregular or the data has genuine "
               + "one-offs. When every sample must go somewhere and the count is known, use K-Means or "
               + "Gaussian Mixture; when the clusters are round and you want them to nest, Hierarchical "
               + "Clustering.\n\n"
               + "This component takes no samples: wire its Method output into OtterCluster.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("fc479490-7062-4155-a422-ea5a7f8316db");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords => new[] { "hdbscan", "dbscan", "density", "outliers", "noise", "clustering" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("hdbscan", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Minimum Cluster Size", "N",
            "Optional. The smallest group of samples that counts as a cluster rather than as outliers.\n\n"
            + "The one setting that matters here, and it reads in the units of the problem: fewer than "
            + "this many is not a group worth naming. Raise it and small groups dissolve into outliers; "
            + "lower it and the fit starts naming coincidences. Leave unwired to derive it from the "
            + "sample count — about two per cent of them, between five and twenty-five.",
            GH_ParamAccess.item);

        pManager[0].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new ClusterMethodParameter(), "Method", "M", MethodWire.ClusterMethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        int size = 0;
        int? minimumClusterSize = da.GetData(0, ref size) ? size : null;

        var method = new HdbscanMethod { MinimumClusterSize = minimumClusterSize };
        da.SetData(0, new GH_ClusterMethod(method));
        Message = method.Describe();
    }
}
