using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Graphs;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// Spectral clustering in its raw form: features, a graph, or both.
/// <para>
/// Adapter only. The algorithm belongs to <see cref="SpectralClustering"/>, and
/// the three ways of wiring this component are its three overloads — which one
/// runs is decided by which inputs carry data, and shown on the component.
/// </para>
/// </summary>
public sealed class SpectralClusteringComponent : GH_Component
{
    public SpectralClusteringComponent()
        : base("Spectral Clustering", "Spectral",
               "Cluster by connection rather than by closeness: samples joined by a path of strong "
               + "links end up together, whatever shape they make.\n\n"
               + "Wire Training Inputs alone and it links each sample to its nearest neighbours. Wire "
               + "Connectivity alone and it cuts that graph as given. Wire both and it keeps your "
               + "connections but weights each by how alike its two ends are — so it cuts where "
               + "connected samples stop behaving alike. Use it for long, curved or chained clusters "
               + "that K-Means and Gaussian Mixture slice across.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("99fbe699-678e-4e1d-b029-adafcfd59aed");

    // The methods tier of the Unsupervised Learning panel.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("spectralclustering", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T",
            "Optional if Connectivity is wired. " + TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Connectivity", "L",
            "Optional if Training Inputs is wired. " + GraphData.ConnectivityDescription,
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Weights", "W", GraphData.WeightsDescription, GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Clusters", "K",
            "How many clusters to cut the graph into.\n\n"
            + "Eigengap on the output says whether that was a natural number to ask for.",
            GH_ParamAccess.item, 4);

        pManager.AddIntegerParameter("Neighbours", "N",
            "Neighbours each sample links to when the graph is built from Training Inputs alone. "
            + "Ignored when Connectivity is wired.\n\n"
            + "Too few and a real cluster breaks into pieces; too many and links start bridging "
            + "clusters that are merely close.",
            GH_ParamAccess.item, 10);

        pManager.AddIntegerParameter("Restarts", "R",
            "Restarts of the k-means that draws the partition in the embedding, keeping the tightest.",
            GH_ParamAccess.item, 10);

        pManager.AddIntegerParameter("Random Seed", "S",
            "Seeds the eigensolver and the k-means. Leave it fixed so a re-solve returns the same "
            + "clusters.",
            GH_ParamAccess.item, 1);

        pManager[0].Optional = true;
        pManager[1].Optional = true;
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Result", "R",
            "Cluster index per sample, largest cluster first, or -1 for a sample with no connections "
            + "at all — it has no place in the embedding, so it is not guessed at.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Clusters", "C",
            "Sample indices bucketed by cluster, one branch each. Unconnected samples are not included.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Isolated", "X",
            "Indices of samples with no connections.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Embedding", "E",
            "One branch per sample, holding its coordinates in the spectral embedding — where the "
            + "partition was actually drawn. Connected regions land close together here, so it is "
            + "worth feeding to another method too.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Eigenvalues", "V",
            "Smallest eigenvalues of the graph's normalised Laplacian, ascending. Each disconnected "
            + "piece gives one of exactly zero, each well-separated cluster one close to zero.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Eigengap", "G",
            "Gap between the (K+1)-th and K-th eigenvalue. Large means the graph really has K "
            + "separate regions; compare it across several K rather than reading one on its own.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Graph Components", "P",
            "How many disconnected pieces the graph has. Spectral clustering never splits one, so "
            + "with at least as many pieces as clusters there is nothing left for it to decide.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        bool hasFeatures = Params.Input[0].VolatileDataCount > 0;
        bool hasGraph = Params.Input[1].VolatileDataCount > 0;
        bool hasWeights = Params.Input[2].VolatileDataCount > 0;

        if (!hasFeatures && !hasGraph)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Wire Training Inputs, Connectivity, or both.");
            return;
        }

        double[,]? data = null;
        if (hasFeatures)
        {
            if (!da.GetDataTree(0, out GH_Structure<GH_Number> tree)) return;
            if (!TrainingData.TryRead(tree, out data, out string? problem))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
                return;
            }
        }

        WeightedGraph? graph = null;
        if (hasGraph)
        {
            if (!da.GetDataTree(1, out GH_Structure<GH_Integer> connectivity)) return;

            GH_Structure<GH_Number>? weights = null;
            if (hasWeights && !da.GetDataTree(2, out weights)) return;

            int nodes = data?.GetLength(0) ?? connectivity.Branches.Count;
            if (!GraphData.TryRead(connectivity, weights, nodes, out graph, out string? problem))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
                return;
            }
        }
        else if (hasWeights)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Weights is ignored without Connectivity — they weight its connections.");
        }

        int clusters = 4;
        int neighbours = 10;
        int restarts = 10;
        int seed = 1;
        if (!da.GetData(3, ref clusters)) return;
        if (!da.GetData(4, ref neighbours)) return;
        if (!da.GetData(5, ref restarts)) return;
        if (!da.GetData(6, ref seed)) return;

        var options = new SpectralClusteringOptions
        {
            Clusters = clusters,
            Neighbours = neighbours,
            Restarts = restarts,
            Seed = seed,
        };

        try
        {
            SpectralClusteringResult result;
            string mode;

            if (data is not null && graph is not null)
            {
                result = SpectralClustering.Fit(data, graph, options);
                mode = "connectivity + features";
            }
            else if (data is not null)
            {
                result = SpectralClustering.Fit(data, options);
                mode = $"{neighbours} neighbours";
            }
            else
            {
                result = SpectralClustering.Fit(graph!, options);
                mode = "connectivity";
            }

            if (!result.Converged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The eigensolver stopped after {result.Iterations} iterations without settling. "
                    + "The clusters are usually still right, but do not trust the eigenvalues far.");

            if (result.GraphComponents >= result.ClusterCount && result.GraphComponents > 1)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"The graph falls into {result.GraphComponents} disconnected pieces, at least as "
                    + $"many as the {result.ClusterCount} clusters asked for. Each cluster is then a "
                    + "whole piece or several, and which pieces share a cluster is arbitrary.");

            if (result.IsolatedCount > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{result.IsolatedCount} sample(s) have no connections and are labelled -1. See Isolated.");

            da.SetDataList(0, result.Labels);
            da.SetDataTree(1, Trees.FromBuckets(result.Clusters()));
            da.SetDataList(2, result.Isolated());
            da.SetDataTree(3, Trees.FromRows(result.Embedding));
            da.SetDataList(4, result.Eigenvalues);
            da.SetData(5, result.EigenGap);
            da.SetData(6, result.GraphComponents);

            Message = $"{result.ClusterCount} clusters\n{mode}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
