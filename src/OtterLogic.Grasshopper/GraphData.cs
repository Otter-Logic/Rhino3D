using Grasshopper;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Graphs;

namespace OtterLogic.Grasshopper;

/// <summary>
/// Turns the Connectivity and Weights trees the graph components take into a
/// <see cref="WeightedGraph"/>, and back.
/// <para>
/// One branch per sample, listing the indices of the samples it connects to.
/// That is exactly what Grasshopper's own Proximity 3D puts out on its Links
/// output, which matters more than any argument about the best encoding: a
/// definition that already finds neighbours natively wires straight in, and one
/// that builds its own adjacency from geometry only has to produce the same shape.
/// It also matches Training Inputs — branch i is sample i in both.
/// </para>
/// </summary>
internal static class GraphData
{
    /// <summary>The wording every component uses for the Connectivity input, so they cannot drift.</summary>
    public const string ConnectivityDescription =
        "One branch per sample, listing the indices of the samples it is connected to — the shape "
        + "Proximity 3D puts out on its Links output, or Neighbour Graph on its Connectivity.\n\n"
        + "Branch position is the sample index, so there must be exactly one branch per sample, in "
        + "the same order as Training Inputs. An empty branch is a sample connected to nothing. "
        + "Listing an edge from both ends is fine; it counts once.";

    /// <summary>The wording every component uses for the optional Weights input.</summary>
    public const string WeightsDescription =
        "Optional. How strongly each connection holds, matching Connectivity item for item. Leave "
        + "unwired and every connection counts the same.\n\n"
        + "Treat these as similarities between 0 and 1, as Gaussian Affinity produces. An edge "
        + "listed from both ends with two weights keeps the larger.";

    /// <summary>
    /// Builds the graph, or explains why it cannot.
    /// <para>
    /// Refuses rather than repairs a tree whose branch count is wrong, for the
    /// same reason Training Inputs does: branch position is what ties a
    /// connection to a sample, and a quiet fix would connect the wrong samples.
    /// </para>
    /// </summary>
    /// <param name="connectivity">One branch per sample of neighbour indices.</param>
    /// <param name="weights">Matching weights, or null for all ones.</param>
    /// <param name="nodeCount">How many samples there are.</param>
    public static bool TryRead(
        GH_Structure<GH_Integer> connectivity,
        GH_Structure<GH_Number>? weights,
        int nodeCount,
        out WeightedGraph? graph,
        out string? problem)
    {
        graph = null;
        problem = null;

        var branches = connectivity.Branches;
        if (branches.Count != nodeCount)
        {
            problem = $"Connectivity has {branches.Count} branch(es) but there are {nodeCount} samples. "
                + "It needs one branch per sample, in the same order — branch i lists the samples "
                + "connected to sample i.";
            return false;
        }

        if (weights is not null)
        {
            var weightBranches = weights.Branches;
            if (weightBranches.Count != branches.Count)
            {
                problem = $"Weights has {weightBranches.Count} branch(es) but Connectivity has "
                    + $"{branches.Count}. They must match branch for branch.";
                return false;
            }

            for (int i = 0; i < branches.Count; i++)
            {
                if (weightBranches[i].Count != branches[i].Count)
                {
                    problem = $"Branch {i} of Weights holds {weightBranches[i].Count} value(s) but "
                        + $"the same branch of Connectivity holds {branches[i].Count}.";
                    return false;
                }
            }
        }

        var edges = new List<(int, int, double)>();
        for (int i = 0; i < branches.Count; i++)
        {
            for (int j = 0; j < branches[i].Count; j++)
            {
                GH_Integer? target = branches[i][j];
                if (target is null)
                {
                    problem = $"Branch {i} of Connectivity holds a null at position {j}.";
                    return false;
                }

                if (target.Value < 0 || target.Value >= nodeCount)
                {
                    problem = $"Branch {i} of Connectivity points at sample {target.Value}, but samples "
                        + $"run from 0 to {nodeCount - 1}.";
                    return false;
                }

                double weight = 1.0;
                if (weights is not null)
                {
                    GH_Number? value = weights.Branches[i][j];
                    if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)
                        || value.Value < 0.0)
                    {
                        problem = $"Branch {i} of Weights holds {value?.Value.ToString() ?? "a null"} "
                            + $"at position {j}. Weights must be finite and not negative.";
                        return false;
                    }

                    weight = value.Value;
                }

                edges.Add((i, target.Value, weight));
            }
        }

        graph = WeightedGraph.FromEdges(nodeCount, edges);
        return true;
    }

    /// <summary>
    /// The graph as Connectivity and Weights trees — one branch per sample, every
    /// edge listed from both ends, neighbours ascending.
    /// </summary>
    public static (DataTree<int> Connectivity, DataTree<double> Weights) ToTrees(WeightedGraph graph)
    {
        var connectivity = new DataTree<int>();
        var weights = new DataTree<double>();

        for (int i = 0; i < graph.NodeCount; i++)
        {
            var path = new GH_Path(i);

            // An isolated sample still gets its branch, so branch i stays sample i.
            connectivity.EnsurePath(path);
            weights.EnsurePath(path);

            connectivity.AddRange(graph.Neighbours(i).ToArray(), path);
            weights.AddRange(graph.EdgeWeights(i).ToArray(), path);
        }

        return (connectivity, weights);
    }
}
