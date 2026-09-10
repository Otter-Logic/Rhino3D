using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// Weights a graph's connections by how alike their two ends are.
/// <para>
/// Adapter only. The kernel belongs to <see cref="Affinity.Gaussian"/>.
/// </para>
/// </summary>
public sealed class GaussianAffinityComponent : GH_Component
{
    public GaussianAffinityComponent()
        : base("Gaussian Affinity", "Affinity",
               "Keep a graph's connections, but weight each by how alike the two samples it joins "
               + "are: near 1 for similar values, falling towards 0 as they differ.\n\n"
               + "This is how connection and behaviour are combined. The graph says which samples are "
               + "related, the values say how alike, and a strong weight then means both — so a graph "
               + "method run on the result cuts where related samples stop behaving alike. The width "
               + "of the kernel adapts to each sample's own neighbourhood, so dense and sparse regions "
               + "are both handled.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("36e547e2-2b26-4cbd-9b64-f6ddb365c7a4");

    // The graph tier of the Unsupervised Learning panel: built before a method runs.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("gaussianaffinity", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T", TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Connectivity", "L", GraphData.ConnectivityDescription,
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Weights", "W",
            "Optional. Existing weights, kept as a multiplier on the similarity. " + GraphData.WeightsDescription,
            GH_ParamAccess.tree);

        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Connectivity", "L",
            "The same connections, one branch per sample, in the order Weights follows. Wire both "
            + "on together — the input order is not kept.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Weights", "W",
            "Similarity-weighted strength of each connection, matching Connectivity item for item.",
            GH_ParamAccess.tree);
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

        try
        {
            var weighted = Affinity.Gaussian(graph!, data);
            var (outConnectivity, outWeights) = GraphData.ToTrees(weighted);

            da.SetDataTree(0, outConnectivity);
            da.SetDataTree(1, outWeights);

            Message = $"{weighted.EdgeCount} connections";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
