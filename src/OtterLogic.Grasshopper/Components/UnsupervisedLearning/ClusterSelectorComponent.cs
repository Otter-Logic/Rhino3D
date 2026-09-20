using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// K-Means, Gaussian Mixture and HDBSCAN fitted to the same samples, with the
/// one the data supports chosen — and the reason given in words.
/// <para>
/// Adapter only. The comparison and the rules it chooses by belong to
/// <see cref="ClusterSelector"/>.
/// </para>
/// </summary>
public sealed class ClusterSelectorComponent : GH_Component
{
    public ClusterSelectorComponent()
        : base("Cluster Selector", "Select",
               "Fit K-Means, Gaussian Mixture and HDBSCAN to the same samples, each across a range "
               + "of cluster counts, and keep the one the data supports — with the reason in words.\n\n"
               + "Each is tested on the question it can answer rather than scored on one number: "
               + "HDBSCAN wins when real outliers or irregular shapes are present, the mixture when "
               + "samples sit between clusters, K-Means when the clusters are clean. The place to "
               + "start when you do not yet know which method or how many clusters. Once you do, "
               + "drive that method directly for every setting it has.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("aba2f564-bbdb-41ed-9654-3d587f4ab384");

    // A method, even though it runs three: it takes samples and returns clusters.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("clusterselector", 24);

    private static readonly ClusterSelectorOptions Defaults = new();

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T", TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Minimum Clusters", "Min",
            "Fewest clusters K-Means and the mixture consider. HDBSCAN is not swept — it reports its "
            + "own count.",
            GH_ParamAccess.item, Defaults.MinimumGroups);

        pManager.AddIntegerParameter("Maximum Clusters", "Max",
            "Most clusters K-Means and the mixture consider.\n\n"
            + "Ten by default, because every cluster is something somebody has to read and act on.",
            GH_ParamAccess.item, Defaults.MaximumGroups);

        pManager.AddIntegerParameter("Model", "M",
            "Optional. Force one model instead of choosing — every candidate is still fitted and "
            + "scored, so the Scores table shows how the others would have done.\n\n"
            + "Right-click for the list, or wire a Clustering Model dropdown in.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Minimum Cluster Size", "N",
            "Optional. Smallest group HDBSCAN calls a cluster. Leave unwired to derive it from the "
            + "number of samples.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Random Seed", "S",
            "Seeds K-Means and the mixture. Leave it fixed: it is the only reason this returns the "
            + "same clusters every time Grasshopper re-solves.",
            GH_ParamAccess.item, Defaults.Seed);

        pManager[3].Optional = true;
        pManager[4].Optional = true;

        var model = (Param_Integer)pManager[3];
        foreach (var (label, value) in EnumChoices.Of<ClusteringModel>())
            model.AddNamedValue(label, value);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Result", "R",
            "Cluster index per sample, in the order the branches arrived. -1 is a sample the chosen "
            + "model declined to place, which only HDBSCAN does.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Clusters", "C",
            "Sample indices bucketed by cluster, one branch each, unplaced samples left out.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Confidence", "F",
            "Per sample, how firmly it belongs, in the chosen model's own terms: a probability for "
            + "the mixture, a density membership for HDBSCAN, and for K-Means how much nearer its "
            + "own centre is than the next. Compare samples with each other, not across models.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Unassigned", "X",
            "Samples in no cluster. Empty unless HDBSCAN was chosen.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Centres", "M",
            "One branch per cluster: the mean of its samples, in the units the data arrived in.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Model", "O",
            "Which model was chosen.",
            GH_ParamAccess.item);

        pManager.AddTextParameter("Rationale", "W",
            "Why, in one sentence, in the terms the choice was made on.",
            GH_ParamAccess.item);

        pManager.AddTextParameter("Scores", "S",
            "All three models side by side — clusters, silhouette, Davies-Bouldin, confidence, "
            + "boundary share, unplaced share. Read it in a panel when the choice surprises you: "
            + "what the other two said is usually the explanation.",
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

        int minimum = Defaults.MinimumGroups;
        int maximum = Defaults.MaximumGroups;
        int seed = Defaults.Seed;
        if (!da.GetData(1, ref minimum)) return;
        if (!da.GetData(2, ref maximum)) return;
        if (!da.GetData(5, ref seed)) return;

        ClusteringModel? forced = null;
        int model = 0;
        if (da.GetData(3, ref model))
        {
            if (!Enum.IsDefined(typeof(ClusteringModel), model))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Model must be one of "
                    + string.Join(", ", EnumChoices.Of<ClusteringModel>().Select(c => $"{c.Value} ({c.Label})"))
                    + ", or unwired to choose.");
                return;
            }

            forced = (ClusteringModel)model;
        }

        int? minimumClusterSize = null;
        int size = 0;
        if (da.GetData(4, ref size))
            minimumClusterSize = size;

        try
        {
            var selection = ClusterSelector.Select(data, new ClusterSelectorOptions
            {
                MinimumGroups = minimum,
                MaximumGroups = maximum,
                Model = forced,
                MinimumClusterSize = minimumClusterSize,
                Seed = seed,
            });

            var unassigned = selection.Unassigned();
            if (unassigned.Length > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{unassigned.Length} sample(s) sit in no cluster and are -1 in Result. Refine "
                    + "Labels can place them from their neighbours if every sample needs a group.");

            string name = ClusterSelection.Name(selection.Chosen);

            da.SetDataList(0, selection.Labels);
            da.SetDataTree(1, Trees.FromBuckets(selection.Members()));
            da.SetDataList(2, selection.Confidence);
            da.SetDataList(3, unassigned);
            da.SetDataTree(4, Trees.FromRows(selection.GroupCentres()));
            da.SetData(5, name);
            da.SetData(6, selection.Rationale);
            da.SetData(7, selection.ScoreTable());

            Message = $"{name}\n{selection.Groups} clusters";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
