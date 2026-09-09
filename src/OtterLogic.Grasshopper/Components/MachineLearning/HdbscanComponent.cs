using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Clustering;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// HDBSCAN in its raw form: density-based, told how small a cluster may be and
/// nothing else.
/// <para>
/// Adapter only. The algorithm belongs to <see cref="Hdbscan"/>.
/// </para>
/// </summary>
public sealed class HdbscanComponent : GH_Component
{
    public HdbscanComponent()
        : base("HDBSCAN Clustering", "HDBSCAN",
               "Find clusters as dense regions of any shape, and leave the samples that belong to "
               + "none of them unassigned.\n\n"
               + "The only one of the three that is not told how many clusters to look for, and the "
               + "only one that can say a sample is an outlier rather than filing it in the nearest "
               + "group. Use it when clusters are irregular or the data has genuine one-offs.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("6e2b53c9-77a4-4d18-b0f5-9c831ae64d2f");

    // The Learn tier of the Machine Learning panel. Grasshopper draws a
    // divider between exposures, which is what groups this panel by pipeline
    // stage without needing a subcategory each.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("hdbscan", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T", TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Minimum Cluster Size", "M",
            "The smallest group of samples that counts as a cluster rather than as noise.\n\n"
            + "The one setting that matters here, and it reads in the units of the problem: fewer "
            + "than this many is not a group worth naming. Raise it and small groups dissolve into "
            + "noise; lower it and the fit starts naming coincidences. Leave at 0 to size it from "
            + "the sample count.",
            GH_ParamAccess.item, 0);

        pManager.AddIntegerParameter("Minimum Samples", "N",
            "How many neighbours the density estimate looks at. 0 follows Minimum Cluster Size, "
            + "which is the usual choice.\n\n"
            + "The conservativeness dial: raising it declares more of the sparse ground to be noise "
            + "without changing what counts as a cluster once found.",
            GH_ParamAccess.item, 0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Result", "R",
            "Cluster index per sample, or -1 for noise.\n\n"
            + "The -1 is the point of this component. A sample labelled noise is one the data does "
            + "not support putting anywhere, and saying so beats forcing it into the nearest group.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Clusters", "C",
            "Sample indices bucketed by cluster, one branch each. Noise is not included.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Noise", "X",
            "Indices of the samples left in no cluster.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Probability", "P",
            "Membership strength per sample, 0 to 1, and 0 for noise. Low values sit on a cluster's "
            + "fringe and were nearly noise.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Stability", "S",
            "How stable each cluster is — the range of density thresholds it survived. Larger means "
            + "a more believable group.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Noise Fraction", "F",
            "Share of samples left unassigned.\n\n"
            + "The headline diagnostic. A little is the algorithm doing its job; a lot means either "
            + "the data has no density structure at these settings, or Minimum Cluster Size is "
            + "asking for bigger groups than the data contains.",
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

        int minimumClusterSize = 0;
        int minimumSamples = 0;
        if (!da.GetData(1, ref minimumClusterSize)) return;
        if (!da.GetData(2, ref minimumSamples)) return;

        int n = data.GetLength(0);
        if (minimumClusterSize <= 0)
            minimumClusterSize = HdbscanOptions.DefaultMinimumClusterSize(n);

        try
        {
            var result = Hdbscan.Fit(data, new HdbscanOptions
            {
                MinimumClusterSize = minimumClusterSize,
                MinimumSamples = minimumSamples > 0 ? minimumSamples : null,
            });

            if (result.ClusterCount == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No cluster survived. Either there is no density structure here, or Minimum "
                    + $"Cluster Size ({minimumClusterSize}) is larger than any dense group. Data "
                    + "that is one single blob comes back entirely as noise by design — one group "
                    + "containing everything is not a clustering.");
            else if (result.NoiseFraction > 0.5)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{result.NoiseFraction:P0} of samples are unassigned. That is high enough to "
                    + "read as weak density structure rather than as a handful of outliers.");

            da.SetDataList(0, result.Labels);
            da.SetDataTree(1, Trees.FromBuckets(result.Clusters()));
            da.SetDataList(2, result.Noise());
            da.SetDataList(3, result.Probabilities);
            da.SetDataList(4, result.ClusterStabilities);
            da.SetData(5, result.NoiseFraction);

            Message = $"{result.ClusterCount} clusters\n{result.NoiseFraction:P0} noise";
        }
        catch (ArgumentException ex)
        {
            // Covers ArgumentOutOfRangeException too, which is what the option
            // validation throws.
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
