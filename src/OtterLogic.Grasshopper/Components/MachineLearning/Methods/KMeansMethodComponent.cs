using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Methods;

/// <summary>
/// K-Means as a method on a wire: told how many clusters, and nothing else.
/// <para>
/// No data input, on purpose. The samples go to OtterCluster; this only makes the
/// <see cref="KMeansMethod"/> record and hands it over, the way a Kangaroo goal
/// goes into the solver. Restarts and the seed stay at the record's defaults
/// because they are about the optimiser, not the data.
/// </para>
/// </summary>
public sealed class KMeansMethodComponent : GH_Component
{
    public KMeansMethodComponent()
        : base("K-Means", "KMeans",
               "The K-Means method for OtterCluster: partition the samples into a fixed number of "
               + "clusters, each sample going to the nearest centre.\n\n"
               + "Assumes clusters are round and of roughly similar size, and places every sample in "
               + "one. Fast and steady when that holds. When clusters overlap use Gaussian Mixture; when "
               + "they are irregular or there are outliers use HDBSCAN; when they are long, curved or "
               + "defined by what connects to what, use Spectral Clustering; when you want to see how "
               + "clusters nest, use Hierarchical Clustering.\n\n"
               + "This component takes no samples: wire its Method output into OtterCluster.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("404c5a6f-b58a-4a01-99e0-61a160fdd7c5");

    // The cluster methods tier, below the cores.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords => new[] { "kmeans", "k means", "clustering", "centroid" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("kmeans", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Clusters", "K",
            "How many clusters to split the samples into.\n\n"
            + "K-Means cannot decide this for itself — the total distance to centres falls every "
            + "time you add one, so there is no value it would settle on. Leave Method unwired on "
            + "OtterCluster to have the count chosen for you.",
            GH_ParamAccess.item, 4);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new ClusterMethodParameter(), "Method", "M", MethodWire.ClusterMethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        int clusters = 4;
        if (!da.GetData(0, ref clusters)) return;

        var method = new KMeansMethod { Clusters = clusters };
        da.SetData(0, new GH_ClusterMethod(method));
        Message = method.Describe();
    }
}
