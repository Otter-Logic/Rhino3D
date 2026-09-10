using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Graphs;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// Agglomerative hierarchical clustering in its raw form: the whole tree, cut
/// where the user says.
/// <para>
/// Adapter only. The algorithm belongs to <see cref="HierarchicalClustering"/>
/// and the cutting to <see cref="HierarchicalClusteringResult"/>.
/// </para>
/// </summary>
public sealed class HierarchicalClusteringComponent : GH_Component
{
    public HierarchicalClusteringComponent()
        : base("Hierarchical Clustering", "Hierarchy",
               "Build the full tree of clusters — every sample on its own at the bottom, one cluster "
               + "at the top — then cut it into as many groups as you want.\n\n"
               + "The tree is the point: cut it at 8 and at 3 and every one of the 8 sits wholly "
               + "inside one of the 3, which two separate fits never promise. Wire Connectivity and "
               + "only connected clusters may merge, so every group at every level is one connected "
               + "piece.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("ed8a0c9b-e44e-4793-a907-598899dcb834");

    // The methods tier of the Unsupervised Learning panel.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("hierarchicalclustering", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T", TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Connectivity", "L",
            "Optional. Only clusters joined by a connection may merge. " + GraphData.ConnectivityDescription,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Linkage", "M",
            "How the distance between two clusters is measured. Ward builds compact groups of "
            + "similar size; complete keeps every group's diameter small; average is the compromise; "
            + "single follows chains of close samples however long.\n\n"
            + "Right-click for the list, or wire a Linkage dropdown in.",
            GH_ParamAccess.item, (int)Linkage.Ward);

        pManager.AddIntegerParameter("Clusters", "K",
            "How many groups to cut the tree into. Ignored when Distance is above zero.",
            GH_ParamAccess.item, 4);

        pManager.AddNumberParameter("Distance", "D",
            "Cut by height instead of by count: every merge at or above this distance is undone. "
            + "Leave at 0 to cut by Clusters. Read the distances off Merges to choose one.",
            GH_ParamAccess.item, 0.0);

        pManager[1].Optional = true;

        var linkage = (Param_Integer)pManager[2];
        foreach (var (label, value) in EnumChoices.Of<Linkage>())
            linkage.AddNamedValue(label, value);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Result", "R",
            "Cluster index per sample at the chosen cut, largest cluster first.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Clusters", "C",
            "Sample indices bucketed by cluster, one branch each.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Merges", "T",
            "The whole tree, one branch per merge in the order they were made: the two nodes "
            + "merged, the distance they merged at, and how many samples the new node holds. "
            + "Samples are nodes 0 to n-1, and merge t creates node n+t — SciPy's linkage layout.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Cluster Count", "N",
            "How many clusters the cut produced — useful when cutting by Distance.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Graph Components", "P",
            "Disconnected pieces of Connectivity, or 1 without it. The tree finishes by merging the "
            + "pieces freely, so a cut into fewer clusters than this groups samples the graph never "
            + "connected.",
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

        WeightedGraph? graph = null;
        if (Params.Input[1].VolatileDataCount > 0)
        {
            if (!da.GetDataTree(1, out GH_Structure<GH_Integer> connectivity)) return;
            if (!GraphData.TryRead(connectivity, null, data.GetLength(0), out graph, out problem))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
                return;
            }
        }

        int linkage = (int)Linkage.Ward;
        int clusters = 4;
        double distance = 0.0;
        if (!da.GetData(2, ref linkage)) return;
        if (!da.GetData(3, ref clusters)) return;
        if (!da.GetData(4, ref distance)) return;

        if (!Enum.IsDefined(typeof(Linkage), linkage))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Linkage must be one of "
                + string.Join(", ", EnumChoices.Of<Linkage>().Select(c => $"{c.Value} ({c.Label})"))
                + ".");
            return;
        }

        try
        {
            var options = new HierarchicalClusteringOptions { Linkage = (Linkage)linkage };
            var result = graph is null
                ? HierarchicalClustering.Fit(data, options)
                : HierarchicalClustering.Fit(data, graph, options);

            var labels = distance > 0.0 ? result.CutAtDistance(distance) : result.Cut(clusters);
            int count = labels.Max() + 1;

            if (result.GraphComponents > 1 && count < result.GraphComponents)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Connectivity falls into {result.GraphComponents} pieces, more than the {count} "
                    + "clusters cut, so some clusters join pieces the graph never connected.");

            var merges = new DataTree<double>();
            for (int t = 0; t < result.Merges.Count; t++)
            {
                var merge = result.Merges[t];
                merges.AddRange(new[] { merge.Left, merge.Right, merge.Distance, merge.Size }, new GH_Path(t));
            }

            da.SetDataList(0, labels);
            da.SetDataTree(1, Trees.FromBuckets(
                Enumerable.Range(0, count).Select(c =>
                    Enumerable.Range(0, labels.Length).Where(i => labels[i] == c).ToArray()).ToArray()));
            da.SetDataTree(2, merges);
            da.SetData(3, count);
            da.SetData(4, result.GraphComponents);

            Message = $"{count} clusters\n{(Linkage)linkage}{(graph is null ? string.Empty : ", connected")}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
