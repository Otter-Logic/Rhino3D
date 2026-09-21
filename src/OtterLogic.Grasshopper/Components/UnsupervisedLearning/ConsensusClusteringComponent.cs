using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Graphs;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// Several labellings of the same samples fused into the one grouping they
/// collectively support.
/// <para>
/// Adapter only. The fusion belongs to <see cref="ConsensusClustering"/>.
/// </para>
/// </summary>
public sealed class ConsensusClusteringComponent : GH_Component
{
    public ConsensusClusteringComponent()
        : base("Consensus Clustering", "Consensus",
               "Fuse several clusterings of the same samples into the one grouping they collectively "
               + "support: two samples belong together in proportion to how many labellings, by "
               + "weight, put them together.\n\n"
               + "Each method assumes something different about what a cluster is, and choosing one "
               + "throws the others' evidence away. Wire the Result of any methods here — different "
               + "algorithms, different features, the same algorithm with different settings, a "
               + "labelling corrected by hand — and Agreement says per sample how firmly they agreed. "
               + "Samples a method left at -1 abstain from its vote rather than counting against it.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("dbf3a701-ddbc-4bb3-b329-ea37acba1342");

    // It acts on the methods' outputs rather than on samples, so it sits in the
    // tier after them beside Refine Labels.
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("consensusclustering", 24);

    private static readonly ConsensusOptions Defaults = new();

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Views", "V",
            "One branch per labelling, each holding a cluster index per sample in the same sample "
            + "order — Entwine the Result outputs of the methods you want to fuse. -1 is unplaced.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("View Names", "N",
            "Optional. A name per view, for the messages. Defaults to View 0, View 1, ...",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("View Weights", "W",
            "Optional. How much each view's vote counts, one per view. Zero leaves a view out; "
            + "unwired treats every view alike.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Connectivity", "L",
            "Optional. When given, a group too small to keep is only ever merged into a group it is "
            + "connected to. " + GraphData.ConnectivityDescription,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Groups", "K",
            "Optional. A fixed number of groups. Leave unwired to take the count the views support "
            + "over the widest range of thresholds.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Minimum Groups", "Min",
            "Fewest groups considered when the count is chosen.",
            GH_ParamAccess.item, Defaults.MinimumGroups);

        pManager.AddIntegerParameter("Maximum Groups", "Max",
            "Most groups considered when the count is chosen.",
            GH_ParamAccess.item, Defaults.MaximumGroups);

        pManager.AddIntegerParameter("Minimum Group Size", "S",
            "Groups smaller than this are merged into the group their samples agree with most. One "
            + "merges nothing.",
            GH_ParamAccess.item, Defaults.MinimumGroupSize);

        pManager.AddBooleanParameter("Weight By Agreement", "A",
            "Scale each view's vote by how far the other views agree with it, never below a tenth.\n\n"
            + "On by default: a view every other view disagrees with is more often the one that "
            + "failed on this data than the only one that saw it clearly. Turn off when a view is "
            + "deliberately different — a hand-corrected labelling, say — and should keep its full say.",
            GH_ParamAccess.item, Defaults.WeightByAgreement);

        pManager[1].Optional = true;
        pManager[2].Optional = true;
        pManager[3].Optional = true;
        pManager[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Result", "R",
            "Group per sample, largest first. Every sample is placed.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Clusters", "C",
            "Sample indices bucketed by group, one branch each.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Agreement", "F",
            "Per sample, 0 to 1: across the rest of its group, the weighted share of views that put "
            + "the two together. One is unanimous; around a half means the views split on this "
            + "sample, and it is the one to look at.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("View Weights", "W",
            "The share of the vote each view actually carried, summing to one — its own weight, "
            + "scaled by agreement when that is on.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("View Agreement", "A",
            "Adjusted Rand index of each view against the consensus. A view far below the rest saw "
            + "something the others did not — or failed.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Counts", "K",
            "Every group count tried.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Lifetime", "T",
            "Matching Counts: the range of disagreement over which the fused tree gives that many "
            + "groups. The longest was chosen — a grouping that survives a wide range of thresholds "
            + "is one the views support, not one a threshold produced.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Silhouette", "Q",
            "Silhouette of the final grouping, measured on disagreement between views.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(0, out GH_Structure<GH_Integer> tree))
            return;

        var branches = tree.Branches;
        if (branches.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire at least one labelling into Views.");
            return;
        }

        var names = new List<string>();
        var weights = new List<double>();
        if (Params.Input[1].VolatileDataCount > 0 && !da.GetDataList(1, names)) return;
        if (Params.Input[2].VolatileDataCount > 0 && !da.GetDataList(2, weights)) return;

        if (names.Count > 0 && names.Count != branches.Count)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"{names.Count} name(s) for {branches.Count} view(s). Give one per view, or none.");
            return;
        }

        if (weights.Count > 0 && weights.Count != branches.Count)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"{weights.Count} weight(s) for {branches.Count} view(s). Give one per view, or none.");
            return;
        }

        var views = new List<ClusterView>(branches.Count);
        for (int v = 0; v < branches.Count; v++)
        {
            var labels = new int[branches[v].Count];
            for (int i = 0; i < labels.Length; i++)
            {
                if (branches[v][i] is not { } label)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"View {v} holds a null at position {i}. Use -1 for a sample the view left unplaced.");
                    return;
                }

                labels[i] = label.Value;
            }

            views.Add(new ClusterView(
                names.Count > 0 ? names[v] : $"View {v}",
                labels,
                weights.Count > 0 ? weights[v] : 1.0));
        }

        WeightedGraph? graph = null;
        if (Params.Input[3].VolatileDataCount > 0)
        {
            if (!da.GetDataTree(3, out GH_Structure<GH_Integer> connectivity)) return;
            if (!GraphData.TryRead(connectivity, null, views[0].Labels.Length, out graph, out string? problem))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
                return;
            }
        }

        int? groups = null;
        int fixedCount = 0;
        if (da.GetData(4, ref fixedCount))
            groups = fixedCount;

        int minimum = Defaults.MinimumGroups;
        int maximum = Defaults.MaximumGroups;
        int minimumSize = Defaults.MinimumGroupSize;
        bool byAgreement = Defaults.WeightByAgreement;
        if (!da.GetData(5, ref minimum)) return;
        if (!da.GetData(6, ref maximum)) return;
        if (!da.GetData(7, ref minimumSize)) return;
        if (!da.GetData(8, ref byAgreement)) return;

        try
        {
            var result = ConsensusClustering.Fuse(views, graph, new ConsensusOptions
            {
                Groups = groups,
                MinimumGroups = minimum,
                MaximumGroups = maximum,
                MinimumGroupSize = minimumSize,
                WeightByAgreement = byAgreement,
            });

            if (result.MergedGroups > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{result.MergedGroups} group(s) smaller than {minimumSize} were merged into the "
                    + "group their samples agree with most.");

            // The view the consensus disagrees with most, named, is the thing a
            // user would otherwise have to read a list of numbers to spot.
            if (views.Count > 1)
            {
                int worst = Array.IndexOf(result.ViewAgreement, result.ViewAgreement.Min());
                if (result.ViewAgreement[worst] < 0.2)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"\"{views[worst].Name}\" barely agrees with the consensus (adjusted Rand "
                        + $"{result.ViewAgreement[worst]:0.00}). It either saw structure the others "
                        + "missed or failed on this data — worth looking at on its own.");
            }

            da.SetDataList(0, result.Labels);
            da.SetDataTree(1, Trees.FromBuckets(result.Members()));
            da.SetDataList(2, result.Agreement);
            da.SetDataList(3, result.Weights);
            da.SetDataList(4, result.ViewAgreement);
            da.SetDataList(5, result.Sweep.Select(c => c.Groups));
            da.SetDataList(6, result.Sweep.Select(c => c.Lifetime));
            da.SetData(7, result.Silhouette);

            Message = $"{result.Groups} groups\n{views.Count} views";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
