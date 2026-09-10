using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// Message-passing clustering in its raw form: smooth features over a graph,
/// then partition them.
/// <para>
/// Adapter only. The algorithm belongs to <see cref="MessagePassing.Fit"/>.
/// </para>
/// </summary>
public sealed class MessagePassingComponent : GH_Component
{
    public MessagePassingComponent()
        : base("Message Passing Clustering", "MsgPass",
               "Blend every sample's values with its neighbours' over a few rounds, then cluster the "
               + "result — the averaging half of a graph neural network, with nothing to train.\n\n"
               + "Neighbours pull each other together, so groups come out spatially coherent and a "
               + "sample whose own values are noisy is steadied by what surrounds it. Use it when "
               + "connection should count as evidence of belonging together. For weights that follow "
               + "similarity as well as connection, run Connectivity through Gaussian Affinity first.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("b735e9d7-f726-47a4-8a82-eba09b73dbf5");

    // The methods tier of the Unsupervised Learning panel.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("messagepassing", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T", TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Connectivity", "L", GraphData.ConnectivityDescription,
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Weights", "W", GraphData.WeightsDescription, GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Clusters", "K",
            "How many clusters to split the smoothed values into.",
            GH_ParamAccess.item, 4);

        pManager.AddParameter(HopsAndRetention.Hops());
        pManager.AddParameter(HopsAndRetention.Retention());

        pManager.AddIntegerParameter("Restarts", "R",
            "Restarts of the k-means on the smoothed values, keeping the tightest.",
            GH_ParamAccess.item, 10);

        pManager.AddIntegerParameter("Random Seed", "S",
            "Seeds the k-means. Leave it fixed so a re-solve returns the same clusters.",
            GH_ParamAccess.item, 1);

        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Result", "R",
            "Cluster index per sample, largest cluster first.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Clusters", "C",
            "Sample indices bucketed by cluster, one branch each.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Smoothed", "E",
            "One branch per sample, holding its values after message passing. Feed these to "
            + "Gaussian Mixture for soft groups that already account for the graph.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Centroids", "M",
            "One branch per cluster, holding its centre in the smoothed values.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Input Means", "I",
            "One branch per cluster, holding the mean of its samples' original values — the one to "
            + "read when naming a cluster, since smoothing draws the centroids towards each other.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Inertia", "D",
            "Total squared distance from every smoothed sample to its centre. Lower is tighter.",
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

        if (!da.GetDataTree(1, out GH_Structure<GH_Integer> connectivity)) return;

        GH_Structure<GH_Number>? weights = null;
        if (Params.Input[2].VolatileDataCount > 0 && !da.GetDataTree(2, out weights)) return;

        if (!GraphData.TryRead(connectivity, weights, data.GetLength(0), out var graph, out problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        int clusters = 4;
        int restarts = 10;
        int seed = 1;
        if (!da.GetData(3, ref clusters)) return;
        if (!HopsAndRetention.TryRead(da, 4, out var propagation)) return;
        if (!da.GetData(6, ref restarts)) return;
        if (!da.GetData(7, ref seed)) return;

        try
        {
            var result = MessagePassing.Fit(data, graph!, new MessagePassingOptions
            {
                Propagation = propagation,
                KMeans = new KMeansOptions { Clusters = clusters, Restarts = restarts, Seed = seed },
            });

            HopsAndRetention.Warn(this, propagation);

            da.SetDataList(0, result.Labels);
            da.SetDataTree(1, Trees.FromBuckets(result.Clusters()));
            da.SetDataTree(2, Trees.FromRows(result.Embedding));
            da.SetDataTree(3, Trees.FromRows(result.Centroids));
            da.SetDataTree(4, Trees.FromRows(result.InputMeans));
            da.SetData(5, result.Inertia);

            Message = $"{result.ClusterCount} clusters\n{propagation.Hops} hops";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
