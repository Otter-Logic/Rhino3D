using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Core;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.MachineLearning.Embedding;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// Samples in, a map of them out: one point per sample, laid out so that samples
/// that are alike sit close — with whatever method is on the wire, or a chosen one
/// when the wire is empty.
/// <para>
/// Adaptor only. Everything between a tree of numbers and a scatter of points —
/// standardising, laying out, relating the axes back to the columns — belongs to
/// <see cref="EmbedRun"/>. This unpacks the tree and the graph, reads the method
/// off its wire, makes one call, and packs the answer back. Adding a method never
/// touches this file.
/// </para>
/// </summary>
public sealed class OtterEmbedComponent : GH_Component
{
    private const int DataInput = 0;
    private const int GraphInput = 1;
    private const int MethodInput = 2;
    private const int DimensionsInput = 3;
    private const int StandardiseInput = 4;
    private const int NamesInput = 5;

    public OtterEmbedComponent()
        : base("OtterEmbed", "Embed",
               "Lay samples out as points to look at, near each other where they are alike.\n\n"
               + "Wire Data and nothing else for a map that keeps the distances between the samples as "
               + "faithfully as two dimensions allow, with Report saying how faithfully. Wire a Graph "
               + "instead, or as well, and the map follows its connections: nodes joined by a path of "
               + "connections sit together whatever else they are. To choose the method yourself, wire "
               + "Principal Components, Multidimensional Scaling or Spectral Embedding into Method — each "
               + "says what it is for.\n\n"
               + "Points is the map as geometry: colour it by OtterCluster's Labels, or by any value per "
               + "sample, to see whether the values follow the shape. Coordinates is the same map as "
               + "numbers, for a later step to read — OtterCluster on three coordinates of a spectral "
               + "map is spectral clustering. Ask for more than three dimensions and only Coordinates "
               + "is filled.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("f85ed9e3-e0b8-4422-a70e-5baa23ac2fc1");

    // The cores tier, beside OtterCluster, OtterTrain and OtterPredict.
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords
        => new[] { "embedding", "map", "layout", "dimensionality reduction", "pca", "mds", "spectral", "manifold", "scatter" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("otterembed", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Data", "D",
            TrainingData.Description + "\n\nOptional when a Graph is wired: then the nodes are the samples.",
            GH_ParamAccess.tree);

        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "Optional. One node per sample, in the same order as Data. With it, Spectral Embedding lays "
            + "the nodes out by what they connect to; the graph's weights play no part."
            + GraphWire.UndirectedNote,
            GH_ParamAccess.item);

        pManager.AddParameter(new EmbeddingMethodParameter(), "Method", "M",
            "Wire Principal Components, Multidimensional Scaling or Spectral Embedding. With nothing "
            + "wired, Data alone gets Multidimensional Scaling and a Graph gets Spectral Embedding, with "
            + "the reason in Report.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Dimensions", "N",
            "How many coordinates each sample gets. Two to look at on the XY plane, three to look at in "
            + "space; more is for a later step to read from Coordinates.",
            GH_ParamAccess.item, 2);

        pManager.AddBooleanParameter("Standardise", "S",
            "Bring every column of Data to the same scale before mapping.\n\n"
            + "Leave it on unless the columns already share a scale that means something. Every method "
            + "here measures distances, so without it a length in millimetres beside an angle in radians "
            + "is a map that has only looked at the length.",
            GH_ParamAccess.item, true);

        pManager.AddTextParameter("Feature Names", "FN",
            "Optional. One name per value in a sample, so Report can say which columns make each axis "
            + "in your words rather than as Feature 0, Feature 1.",
            GH_ParamAccess.list);

        pManager[DataInput].Optional = true;
        pManager[GraphInput].Optional = true;
        pManager[MethodInput].Optional = true;
        pManager[NamesInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P",
            "One point per sample, in the order the samples arrived: on the world XY plane for two "
            + "dimensions, in space for three, empty for more.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Coordinates", "C",
            "The same map as numbers: one branch per sample, one value per dimension. Wire it into "
            + "OtterCluster's Data, or OtterTrain's Inputs, as the samples' place on the map.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Axes", "A",
            "For Principal Components, one branch per axis: how much of each column of Data makes it, "
            + "matching Feature Names. Empty for the other methods, whose axes mean nothing of their own.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Retained", "R",
            "The share of the samples' structure the map carries, 0 to 1 — how much was lost squeezing "
            + "them into this many dimensions. Empty for Spectral Embedding, which has no honest number.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Distortion", "X",
            "For Multidimensional Scaling, per sample, how badly the map places it: high is a point "
            + "whose neighbours on the map are not really its neighbours. Empty for the other methods.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Report", "Rp",
            "The run in words: which method and why, how much of the structure the map carries, and "
            + "what each axis is made of when it is made of anything.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        double[,]? data = null;
        if (da.GetDataTree(DataInput, out GH_Structure<GH_Number> tree) && tree.DataCount > 0)
        {
            if (!TrainingData.TryRead(tree, out data, out string? problem))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
                return;
            }
        }

        OtterLogic.Graphs.WeightedGraph? graph = null;
        if (Params.Input[GraphInput].SourceCount > 0
            && !GraphWire.TryGetUndirected(this, da, GraphInput, out _, out graph))
            return;

        if (data is null && graph is null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire Data, a Graph, or both.");
            return;
        }

        EmbeddingMethod? method = MethodWire.ReadEmbeddingMethod(da, MethodInput);

        int dimensions = 2;
        bool standardise = true;
        if (!da.GetData(DimensionsInput, ref dimensions)) return;
        if (!da.GetData(StandardiseInput, ref standardise)) return;

        var names = new List<string>();
        da.GetDataList(NamesInput, names);
        int columns = data?.GetLength(1) ?? 0;
        if (names.Count > 0 && names.Count != columns)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Feature Names has {names.Count} name(s) and each sample holds {columns} value(s), so the "
                + "names were not used. Report calls the columns Feature 0, Feature 1, ...");
            names.Clear();
        }

        EmbedRunResult result;
        try
        {
            result = EmbedRun.Fit(new EmbeddingQuery(data, graph), method, new EmbedRunOptions
            {
                Standardise = standardise,
                Dimensions = dimensions,
            });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        foreach (Note note in result.Notes)
            AddRuntimeMessage(
                note.Level == NoteLevel.Warning ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark,
                note.Text);

        if (dimensions > 3)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Points is empty in {dimensions} dimensions; Coordinates holds all of them.");

        da.SetDataList(0, Points(result.Coordinates));
        da.SetDataTree(1, Trees.FromRows(result.Coordinates));
        da.SetDataTree(2, result.Axes is { } axes ? Trees.FromRows(axes) : new DataTree<double>());
        if (!double.IsNaN(result.Retained))
            da.SetData(3, result.Retained);
        da.SetDataList(4, result.Distortion ?? Array.Empty<double>());
        da.SetDataList(5, result.Report(names.Count > 0 ? names : null));

        Message = $"{result.Method}\n{result.SampleCount} samples, {dimensions}D";
    }

    /// <summary>The map as points: one dimension along X, two on world XY, three as they come, more as nothing.</summary>
    private static IEnumerable<Point3d> Points(double[,] map)
    {
        int dimensions = map.GetLength(1);
        if (dimensions > 3)
            yield break;

        for (int i = 0; i < map.GetLength(0); i++)
            yield return new Point3d(
                map[i, 0],
                dimensions > 1 ? map[i, 1] : 0.0,
                dimensions > 2 ? map[i, 2] : 0.0);
    }
}
