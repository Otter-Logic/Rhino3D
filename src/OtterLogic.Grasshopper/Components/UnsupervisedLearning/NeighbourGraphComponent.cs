using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Graphs;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// The nearest-neighbour graph of a set of samples, in the Connectivity and
/// Weights shape every graph component takes.
/// <para>
/// Adapter only. The graph belongs to <see cref="WeightedGraph.NearestNeighbours"/>.
/// </para>
/// </summary>
public sealed class NeighbourGraphComponent : GH_Component
{
    public NeighbourGraphComponent()
        : base("Neighbour Graph", "kNN Graph",
               "Connect every sample to its nearest neighbours by the distance between their values, "
               + "giving a graph for the graph methods when there is none to hand.\n\n"
               + "A connection both samples chose weighs 1; one only one of them chose weighs 0.5. "
               + "When the samples do have real connections — elements that touch, say — build "
               + "Connectivity from those instead: that is information distance cannot recover.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("b11a73b6-4b75-42a8-9f1d-379e0237357f");

    // The graph tier of the Unsupervised Learning panel: built before a method runs.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("neighbourgraph", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T", TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Neighbours", "K",
            "How many nearest samples each one connects to, not counting itself.\n\n"
            + "Too few and a real cluster breaks into disconnected pieces; too many and connections "
            + "start bridging clusters that are merely close.",
            GH_ParamAccess.item, 10);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Connectivity", "L",
            "One branch per sample, listing the samples it connects to — wire into any graph "
            + "method's Connectivity.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Weights", "W",
            "Matching Connectivity item for item: 1 where both ends chose each other, 0.5 where "
            + "only one did.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Graph Components", "P",
            "How many disconnected pieces the graph has. More than the number of clusters you are "
            + "after usually means Neighbours is too low.",
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

        int neighbours = 10;
        if (!da.GetData(1, ref neighbours)) return;

        int n = data.GetLength(0);
        if (neighbours > n - 1)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Only {n - 1} other samples exist, so each connects to all of them.");
            neighbours = n - 1;
        }

        try
        {
            var graph = WeightedGraph.NearestNeighbours(data, neighbours);
            graph.ConnectedComponents(out int components);
            var (connectivity, weights) = GraphData.ToTrees(graph);

            da.SetDataTree(0, connectivity);
            da.SetDataTree(1, weights);
            da.SetData(2, components);

            Message = $"{neighbours} neighbours\n{components} piece{(components == 1 ? string.Empty : "s")}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
