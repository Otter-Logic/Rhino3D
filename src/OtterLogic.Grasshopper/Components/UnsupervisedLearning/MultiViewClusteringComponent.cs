using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using OtterLogic.Graphs;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// The same samples clustered by connection, by likeness and by density, and
/// the three fused into one grouping.
/// <para>
/// Adapter only. The three views and their fusion belong to
/// <see cref="MultiViewClustering"/>.
/// </para>
/// </summary>
public sealed class MultiViewClusteringComponent : GH_Component
{
    public MultiViewClusteringComponent()
        : base("Multi-View Clustering", "MultiView",
               "Cluster samples three ways and fuse the answers: Spectral over the graph (connected "
               + "samples that are alike), Hierarchical over features alone (alike wherever they "
               + "are), and HDBSCAN for dense regions and the outliers outside them. Each view "
               + "chooses its own count.\n\n"
               + "For samples that have both real connections and features describing them — "
               + "elements that touch, say — when neither alone tells the whole story. Every view's "
               + "labels come out beside the consensus, so disagreements can be read. For control "
               + "over the fusion itself, run the methods separately and wire their Results into "
               + "Consensus Clustering.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("4627fd9c-f5ed-4836-aa26-9c711522f975");

    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("multiviewclustering", 24);

    private static readonly MultiViewClusteringOptions Defaults = new();

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Affinity Inputs", "A",
            "What decides how strongly two connected samples belong together in the spectral view. "
            + "Also used by the other two views when their own inputs are unwired. "
            + TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Hierarchy Inputs", "H",
            "Optional. What the hierarchical view groups on, when it should differ from Affinity "
            + "Inputs. Same shape: one branch per sample.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Density Inputs", "D",
            "Optional. What the density view groups on and finds outliers in, when it should differ "
            + "from Affinity Inputs. Same shape: one branch per sample.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Connectivity", "L", GraphData.ConnectivityDescription,
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Weights", "W", GraphData.WeightsDescription, GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Minimum Groups", "Min",
            "Fewest clusters the spectral and hierarchical views consider.",
            GH_ParamAccess.item, Defaults.MinimumGroups);

        pManager.AddIntegerParameter("Maximum Groups", "Max",
            "Most clusters the spectral and hierarchical views consider.",
            GH_ParamAccess.item, Defaults.MaximumGroups);

        pManager.AddNumberParameter("View Weights", "V",
            "Optional. Three numbers: the votes of the spectral, hierarchical and density views, in "
            + "that order. Zero skips a view. Unwired counts all three alike.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Linkage", "M",
            "How the hierarchical view measures the distance between clusters. Right-click for the "
            + "list, or wire a Linkage dropdown in.",
            GH_ParamAccess.item, (int)Defaults.Linkage);

        pManager.AddIntegerParameter("Minimum Cluster Size", "N",
            "Optional. Smallest group the density view calls a cluster. Leave unwired to derive it "
            + "from the number of samples.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Random Seed", "S",
            "Seeds the spectral view. Leave it fixed so a re-solve returns the same groups.",
            GH_ParamAccess.item, Defaults.Seed);

        pManager.AddIntegerParameter("Minimum Group Size", "G",
            "Consensus groups smaller than this are merged into the connected group their samples "
            + "agree with most. One merges nothing.\n\n"
            + "Raise it when outliers come out as a scatter of tiny groups: the density view abstains "
            + "on them, so the other two views alone decide their company, and they rarely agree.",
            GH_ParamAccess.item, Defaults.Consensus.MinimumGroupSize);

        pManager[1].Optional = true;
        pManager[2].Optional = true;
        pManager[4].Optional = true;
        pManager[7].Optional = true;
        pManager[9].Optional = true;

        var linkage = (Param_Integer)pManager[8];
        foreach (var (label, value) in EnumChoices.Of<Linkage>())
            linkage.AddNamedValue(label, value);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Result", "R",
            "Consensus group per sample. Every sample is placed.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Clusters", "C",
            "Sample indices bucketed by consensus group, one branch each.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Agreement", "F",
            "Per sample, 0 to 1: how firmly the views agreed about the company it keeps. Low values "
            + "are the samples the views split on.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Spectral", "Sp",
            "The spectral view's own labels. Empty when it was skipped.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Hierarchical", "Hi",
            "The hierarchical view's own labels. Empty when it was skipped.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Density", "De",
            "The density view's own labels, -1 for outliers. Empty when it was skipped.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Outliers", "X",
            "Samples in no dense region, by the density view. They are still placed in Result — "
            + "this says which placements rest on the other two views alone.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("View Agreement", "VA",
            "Adjusted Rand index of each view that ran against the consensus, in view order.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Algebraic Connectivity", "G",
            "Second-smallest eigenvalue of the graph's normalised Laplacian, 0 to 2. Near zero means "
            + "the graph is only weakly one piece.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(0, out GH_Structure<GH_Number> affinityTree))
            return;

        if (!TrainingData.TryRead(affinityTree, out double[,] affinity, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Affinity Inputs: " + problem);
            return;
        }

        if (!TryReadOptional(da, 1, "Hierarchy Inputs", affinity, out double[,]? hierarchy)) return;
        if (!TryReadOptional(da, 2, "Density Inputs", affinity, out double[,]? density)) return;

        int n = affinity.GetLength(0);

        if (!da.GetDataTree(3, out GH_Structure<GH_Integer> connectivity)) return;

        GH_Structure<GH_Number>? edgeWeights = null;
        if (Params.Input[4].VolatileDataCount > 0 && !da.GetDataTree(4, out edgeWeights)) return;

        if (!GraphData.TryRead(connectivity, edgeWeights, n, out var graph, out problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        int minimum = Defaults.MinimumGroups;
        int maximum = Defaults.MaximumGroups;
        int linkage = (int)Defaults.Linkage;
        int seed = Defaults.Seed;
        if (!da.GetData(5, ref minimum)) return;
        if (!da.GetData(6, ref maximum)) return;
        if (!da.GetData(8, ref linkage)) return;
        if (!da.GetData(10, ref seed)) return;

        int minimumGroupSize = Defaults.Consensus.MinimumGroupSize;
        if (!da.GetData(11, ref minimumGroupSize)) return;

        var viewWeights = new List<double>();
        if (Params.Input[7].VolatileDataCount > 0 && !da.GetDataList(7, viewWeights)) return;
        if (viewWeights.Count is not (0 or 3))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"View Weights holds {viewWeights.Count} value(s). Give three — spectral, "
                + "hierarchical, density — or leave it unwired.");
            return;
        }

        if (!Enum.IsDefined(typeof(Linkage), linkage))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Linkage must be one of "
                + string.Join(", ", EnumChoices.Of<Linkage>().Select(c => $"{c.Value} ({c.Label})"))
                + ".");
            return;
        }

        int? minimumClusterSize = null;
        int size = 0;
        if (da.GetData(9, ref size))
            minimumClusterSize = size;

        try
        {
            var result = MultiViewClustering.Fit(graph!, affinity, hierarchy!, density!, new MultiViewClusteringOptions
            {
                MinimumGroups = minimum,
                MaximumGroups = maximum,
                Linkage = (Linkage)linkage,
                MinimumClusterSize = minimumClusterSize,
                SpectralWeight = viewWeights.Count == 3 ? viewWeights[0] : Defaults.SpectralWeight,
                HierarchicalWeight = viewWeights.Count == 3 ? viewWeights[1] : Defaults.HierarchicalWeight,
                DensityWeight = viewWeights.Count == 3 ? viewWeights[2] : Defaults.DensityWeight,
                Seed = seed,
                Consensus = Defaults.Consensus with { MinimumGroupSize = minimumGroupSize },
            });

            foreach (string note in result.Notes)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);

            if (result.Consensus.MergedGroups > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{result.Consensus.MergedGroups} group(s) smaller than {minimumGroupSize} were "
                    + "merged into the group their samples agree with most.");

            da.SetDataList(0, result.Labels);
            da.SetDataTree(1, Trees.FromBuckets(result.Consensus.Members()));
            da.SetDataList(2, result.Agreement);
            da.SetDataList(3, result.Spectral?.Labels ?? Array.Empty<int>());
            da.SetDataList(4, result.HierarchicalLabels ?? Array.Empty<int>());
            da.SetDataList(5, result.Density?.Labels ?? Array.Empty<int>());
            da.SetDataList(6, result.Outliers());
            da.SetDataList(7, result.Consensus.ViewAgreement);
            da.SetData(8, result.AlgebraicConnectivity);

            Message = $"{result.Groups} groups\n{result.Views.Count} views";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    /// <summary>
    /// Reads an optional feature input, falling back to the affinity features
    /// when it is unwired, and refusing one whose sample count disagrees.
    /// </summary>
    private bool TryReadOptional(
        IGH_DataAccess da, int index, string name, double[,] fallback, out double[,]? features)
    {
        features = fallback;
        if (Params.Input[index].VolatileDataCount == 0)
            return true;

        if (!da.GetDataTree(index, out GH_Structure<GH_Number> tree))
            return false;

        if (!TrainingData.TryRead(tree, out double[,] read, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{name}: {problem}");
            return false;
        }

        if (read.GetLength(0) != fallback.GetLength(0))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"{name} has {read.GetLength(0)} branch(es) but Affinity Inputs has "
                + $"{fallback.GetLength(0)}. Every input needs one branch per sample, in the same order.");
            return false;
        }

        features = read;
        return true;
    }
}
