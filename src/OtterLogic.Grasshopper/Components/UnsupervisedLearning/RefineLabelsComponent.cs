using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// Label propagation in its raw form: an existing labelling, passed over a
/// graph.
/// <para>
/// Adapter only. The algorithm belongs to <see cref="MessagePassing.Refine(double[,], OtterLogic.MachineLearning.Graphs.WeightedGraph, PropagationOptions?)"/>.
/// </para>
/// </summary>
public sealed class RefineLabelsComponent : GH_Component
{
    public RefineLabelsComponent()
        : base("Refine Labels", "Refine",
               "Take a labelling from any method and let each sample's neighbours have their say: "
               + "samples their surroundings disagree with get corrected, and unlabelled samples "
               + "(-1, such as HDBSCAN's noise) are filled in from theirs.\n\n"
               + "Wire Labels for a hard labelling, or Probability for a soft one such as Gaussian "
               + "Mixture's. Cluster numbers stay as they came in. Changed lists every sample that "
               + "moved — worth reviewing, since a sample its neighbours overruled is either a "
               + "mistake corrected or a genuine one-off smoothed over.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("0096c49f-5342-4ba4-8069-402bfd9ef691");

    // Refinement acts on a method's output, so it sits in the tier after them.
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("refinelabels", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Labels", "R",
            "A cluster index per sample, or -1 for unlabelled. Wire this or Probability, not both.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Probability", "P",
            "One branch per sample, holding how strongly it belongs to each cluster — Gaussian "
            + "Mixture's Probability output fits directly. A branch of zeros is unlabelled. Wire "
            + "this or Labels, not both.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Connectivity", "L", GraphData.ConnectivityDescription,
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Weights", "W", GraphData.WeightsDescription, GH_ParamAccess.tree);

        pManager.AddParameter(HopsAndRetention.Hops());
        pManager.AddParameter(HopsAndRetention.Retention());

        pManager[0].Optional = true;
        pManager[1].Optional = true;
        pManager[3].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Result", "R",
            "Refined cluster per sample, numbered as it came in, or -1 for a sample no label reached.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Clusters", "C",
            "Sample indices bucketed by refined cluster, one branch each.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Probability", "P",
            "One branch per sample, holding its refined membership of each cluster. Each branch sums "
            + "to one, except for a sample no label reached.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Confidence", "F",
            "The largest refined membership per sample, and 0 where no label reached.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Changed", "X",
            "Samples whose cluster differs from the one they came in with, including unlabelled "
            + "samples that were placed.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Unreached", "U",
            "Samples with no labelled sample within reach, still unlabelled. More Hops reaches further.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        bool hasLabels = Params.Input[0].VolatileDataCount > 0;
        bool hasProbability = Params.Input[1].VolatileDataCount > 0;

        if (hasLabels == hasProbability)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                hasLabels
                    ? "Wire Labels or Probability, not both — it cannot tell which to trust."
                    : "Wire the labelling to refine, into Labels or Probability.");
            return;
        }

        int[]? labels = null;
        double[,]? probability = null;
        int count;

        if (hasLabels)
        {
            var list = new List<int>();
            if (!da.GetDataList(0, list)) return;
            labels = list.ToArray();
            count = labels.Length;
        }
        else
        {
            if (!da.GetDataTree(1, out GH_Structure<GH_Number> tree)) return;
            if (!TrainingData.TryRead(tree, out double[,] rows, out string? unreadable))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, unreadable);
                return;
            }

            probability = rows;
            count = rows.GetLength(0);
        }

        if (!da.GetDataTree(2, out GH_Structure<GH_Integer> connectivity)) return;

        GH_Structure<GH_Number>? weights = null;
        if (Params.Input[3].VolatileDataCount > 0 && !da.GetDataTree(3, out weights)) return;

        if (!GraphData.TryRead(connectivity, weights, count, out var graph, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        if (!HopsAndRetention.TryRead(da, 4, out var propagation)) return;

        try
        {
            var result = labels is not null
                ? MessagePassing.Refine(labels, graph!, propagation)
                : MessagePassing.Refine(probability!, graph!, propagation);

            HopsAndRetention.Warn(this, propagation);

            var changed = result.Changed();
            var unreached = result.Unreached();

            if (unreached.Length > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{unreached.Length} sample(s) have no labelled sample within {propagation.Hops} "
                    + "hop(s) and stay -1. See Unreached; more Hops reaches further.");

            da.SetDataList(0, result.Labels);
            da.SetDataTree(1, Trees.FromBuckets(result.Clusters()));
            da.SetDataTree(2, Trees.FromRows(result.Responsibilities));
            da.SetDataList(3, result.Confidence);
            da.SetDataList(4, changed);
            da.SetDataList(5, unreached);

            Message = $"{changed.Length} changed\n{propagation.Hops} hops";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
