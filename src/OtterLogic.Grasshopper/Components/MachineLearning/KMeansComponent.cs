using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Clustering;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// k-means in its raw form: told how many clusters to find, and nothing else.
/// <para>
/// Adapter only. The algorithm belongs to <see cref="KMeans"/>; this unpacks a
/// tree, calls it, and packs the answer back out.
/// </para>
/// </summary>
public sealed class KMeansComponent : GH_Component
{
    public KMeansComponent()
        : base("K-Means Clustering", "K-Means",
               "Partition samples into a fixed number of clusters, each sample going to the nearest "
               + "centre.\n\n"
               + "Assumes clusters are round and of roughly similar size, and places every sample in "
               + "one. Fast and steady when that holds. When clusters overlap use Gaussian Mixture; "
               + "when they are irregular or there are outliers use HDBSCAN.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("3f7f8f2a-6c1e-4f0b-9a3d-5b2e7c14a081");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("kmeans", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T", TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Clusters", "K",
            "How many clusters to split the samples into.\n\n"
            + "k-means cannot decide this for itself — the total distance to centres falls every "
            + "time you add one, so there is no value it would settle on.",
            GH_ParamAccess.item, 4);

        pManager.AddIntegerParameter("Restarts", "R",
            "How many times to refit from a different start, keeping the tightest.\n\n"
            + "The fit descends to a local optimum and stops, so this is the cheapest accuracy "
            + "available — ten costs milliseconds and reliably beats one.",
            GH_ParamAccess.item, 10);

        pManager.AddIntegerParameter("Random Seed", "S",
            "Seeds the initialisation. Change it to sample a different set of starting points.\n\n"
            + "Leave it fixed the rest of the time: it is the only reason this returns the same "
            + "clusters every time Grasshopper re-solves.",
            GH_ParamAccess.item, 1);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Result", "R",
            "Cluster index per sample, in the order the branches arrived.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Clusters", "C",
            "Sample indices bucketed by cluster, one branch each — ready to drive geometry without "
            + "sorting on the canvas.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Centroids", "M",
            "One branch per cluster, holding that cluster's centre in the units the data arrived in.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Inertia", "I",
            "Total squared distance from every sample to its own centre. Lower is tighter.\n\n"
            + "Compare it between restarts at one cluster count, or look for an elbow across "
            + "several — but it always falls as clusters are added, so it cannot choose the count.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(0, out GH_Structure<GH_Number> tree))
            return;

        if (!TrainingData.TryRead(tree, out double[,] data, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        int clusters = 4;
        int restarts = 10;
        int seed = 1;
        if (!da.GetData(1, ref clusters)) return;
        if (!da.GetData(2, ref restarts)) return;
        if (!da.GetData(3, ref seed)) return;

        try
        {
            var result = KMeans.Fit(data, new KMeansOptions
            {
                Clusters = clusters,
                Restarts = restarts,
                Seed = seed,
            });

            if (!result.Converged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Stopped at the iteration cap after {result.Iterations} rounds rather than "
                    + "settling. The clusters are usable but not final.");

            da.SetDataList(0, result.Labels);
            da.SetDataTree(1, Trees.FromBuckets(result.Clusters()));
            da.SetDataTree(2, Trees.FromRows(result.Centroids));
            da.SetData(3, result.Inertia);

            Message = $"{clusters} clusters";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
