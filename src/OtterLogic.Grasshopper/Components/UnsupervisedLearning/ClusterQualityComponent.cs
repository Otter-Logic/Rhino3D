using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// How clean a labelling looks from the data and the labels alone.
/// <para>
/// Adapter only. The measures belong to <see cref="ClusterQuality"/>.
/// </para>
/// </summary>
public sealed class ClusterQualityComponent : GH_Component
{
    public ClusterQualityComponent()
        : base("Cluster Quality", "Quality",
               "Score how clean a labelling of samples is, using only the samples and the labels: "
               + "silhouette (higher is better) and Davies-Bouldin (lower is better).\n\n"
               + "Both reward compact, round, well-separated clusters, so use them to compare "
               + "settings of one method — cluster counts, feature choices — not to choose between "
               + "methods: K-Means optimises almost exactly what a silhouette measures, and wins that "
               + "contest whether or not it found the truth. Cluster Selector compares methods on "
               + "fairer terms. Samples labelled -1 are left out, not scored as a cluster.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("066b5210-880c-4c1e-b6b3-383bdfb691b4");

    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("clusterquality", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T",
            "The samples the labelling was made from, in the space it was made in. "
            + TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Labels", "R",
            "A cluster index per sample, in the same order — any method's Result. -1 is left out.",
            GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Silhouette", "S",
            "Mean silhouette, -1 to 1. Near 1 is comfortably inside well-separated clusters, near 0 "
            + "is on boundaries, negative is closer to another cluster than to its own.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Davies-Bouldin", "D",
            "Davies-Bouldin index, lower is better. Driven by the single most confusable pair of "
            + "clusters, so a bad score beside a good silhouette means one pair is doing all the "
            + "damage. Empty with fewer than two clusters.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Cluster Count", "N",
            "Clusters that hold at least one sample.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Noise Fraction", "F",
            "Share of samples labelled -1 and left out of both scores. Read the scores beside this, "
            + "never instead of it: leaving the hard samples out is an easy way to look clean.",
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

        var labels = new List<int>();
        if (!da.GetDataList(1, labels)) return;

        if (labels.Count != data.GetLength(0))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"{labels.Count} label(s) for {data.GetLength(0)} sample(s). Give one per sample, in "
                + "the same order.");
            return;
        }

        try
        {
            var array = labels.ToArray();
            int clusters = array.Where(l => l >= 0).Distinct().Count();
            double noise = (double)array.Count(l => l < 0) / array.Length;

            if (clusters < 2)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Fewer than two clusters hold samples, so there is no separation to score.");

            double silhouette = ClusterQuality.Silhouette(data, array);
            double daviesBouldin = ClusterQuality.DaviesBouldin(data, array);

            da.SetData(0, silhouette);
            if (!double.IsNaN(daviesBouldin))
                da.SetData(1, daviesBouldin);
            da.SetData(2, clusters);
            da.SetData(3, noise);

            Message = $"silhouette {silhouette:0.00}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
