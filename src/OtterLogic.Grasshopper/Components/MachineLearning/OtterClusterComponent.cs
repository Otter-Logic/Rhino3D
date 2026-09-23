using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Core;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Unsupervised.Clustering;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// Samples in, a clustering a person can act on out — with whatever method is on
/// the wire, or a chosen one when the wire is empty.
/// <para>
/// Adaptor only. Everything between a tree of numbers and a coloured model —
/// standardising, fitting, reading centres back into input units, scoring the
/// separation, describing what sets each cluster apart — belongs to
/// <see cref="ClusterRun"/>. This unpacks the tree, reads the method off its
/// wire, makes one call, and packs the answer back. Adding an algorithm never
/// touches this file: it is one record in Unsupervised and one method component.
/// </para>
/// </summary>
public sealed class OtterClusterComponent : GH_Component
{
    private const int DataInput = 0;
    private const int MethodInput = 1;
    private const int StandardiseInput = 2;
    private const int MapInput = 3;
    private const int NamesInput = 4;

    public OtterClusterComponent()
        : base("OtterCluster", "Cluster",
               "Group samples that are alike, and say which group each one is in.\n\n"
               + "Wire Data and nothing else for a first answer: K-Means, a Gaussian mixture and HDBSCAN "
               + "are all fitted and the one the data supports is kept, with the reason in Report. To "
               + "choose the method yourself, wire K-Means, Gaussian Mixture, HDBSCAN, Spectral Clustering "
               + "or Hierarchical Clustering into Method — each says what it assumes and which to reach "
               + "for when it does not fit.\n\n"
               + "Labels and Groups drive geometry: colour by Labels, or take a cluster's members from "
               + "Groups. Report says how many clusters, how well separated, and what sets each apart, "
               + "in the feature names you give it. Unplaced lists the samples no cluster claimed — the "
               + "outliers, and often the interesting ones.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("d88c0e1e-5d6d-4be9-92f3-88ffc0754216");

    // The cores tier: the first thing a person reaches for, above every method.
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords
        => new[] { "clustering", "cluster", "group", "k-means", "hdbscan", "gaussian mixture", "unsupervised" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("ottercluster", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Data", "D", TrainingData.Description, GH_ParamAccess.tree);

        pManager.AddParameter(new ClusterMethodParameter(), "Method", "M",
            "Wire K-Means, Gaussian Mixture, HDBSCAN, Spectral or Hierarchical. With nothing wired, "
            + "K-Means, a mixture and HDBSCAN are all fitted and the one the data supports is kept, "
            + "with the reason in Report.",
            GH_ParamAccess.item);

        pManager.AddBooleanParameter("Standardise", "S",
            "Bring every column to the same scale before fitting.\n\n"
            + "Leave it on unless the columns already share a scale that means something. Every method "
            + "here measures distances, so without it a length in millimetres beside an angle in radians "
            + "is a clustering that has only looked at the length.",
            GH_ParamAccess.item, true);

        pManager.AddBooleanParameter("Map", "P",
            "Lay the samples out as points to look at, near each other where they are alike: three "
            + "dimensions when the samples hold three or more values, otherwise two on the world XY "
            + "plane. Off by default because it costs a full distance matrix and is only for looking at.",
            GH_ParamAccess.item, false);

        pManager.AddTextParameter("Feature Names", "FN",
            "Optional. One name per value in a sample, so Report can say what sets each cluster apart in "
            + "your words rather than as Feature 0, Feature 1.",
            GH_ParamAccess.list);

        pManager[MethodInput].Optional = true;
        pManager[NamesInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Labels", "L",
            "Cluster per sample, in the order the branches arrived, numbered largest cluster first "
            + "from zero. -1 is a sample in no cluster.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Groups", "G",
            "Sample indices bucketed by cluster, one branch each — ready to drive geometry without "
            + "sorting on the canvas. Unplaced samples are not in any branch.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Confidence", "C",
            "Per sample, how firmly it belongs where it was put, 0 to 1, in the method's own terms: a "
            + "probability for a mixture, a density membership for HDBSCAN, a margin for K-Means. Sort "
            + "by it to find the samples worth a second look. Empty for Hierarchical, which has no "
            + "honest number to give.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Centres", "M",
            "One branch per cluster: the mean of its samples over every value, in the units the data "
            + "arrived in.",
            GH_ParamAccess.tree);

        pManager.AddPointParameter("Map", "P",
            "One point per sample when Map is on, laid out so that samples that are alike sit close. "
            + "Colour them by Labels. Empty when Map is off.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Unplaced", "U",
            "Indices of the samples in no cluster. Only HDBSCAN leaves any; the rest place every sample.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Report", "Rp",
            "The run in words: which method and why, how many clusters and how big, how well separated, "
            + "and what sets each cluster apart.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(DataInput, out GH_Structure<GH_Number> tree)) return;

        if (!TrainingData.TryRead(tree, out double[,] data, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        ClusteringMethod? method = MethodWire.ReadClusterMethod(da, MethodInput);

        bool standardise = true;
        bool map = false;
        if (!da.GetData(StandardiseInput, ref standardise)) return;
        if (!da.GetData(MapInput, ref map)) return;

        var names = new List<string>();
        da.GetDataList(NamesInput, names);

        int columns = data.GetLength(1);
        if (names.Count > 0 && names.Count != columns)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Feature Names has {names.Count} name(s) and each sample holds {columns} value(s), so the "
                + "names were not used. Report calls the columns Feature 0, Feature 1, ...");
            names.Clear();
        }

        // Three dimensions only when the data has that many to show: a map of two
        // columns in three dimensions is a plane, and reads worse than the plane.
        int mapDimensions = map ? (columns >= 3 ? 3 : 2) : 0;

        ClusterRunResult result;
        try
        {
            result = ClusterRun.Fit(data, method, new ClusterRunOptions
            {
                Standardise = standardise,
                MapDimensions = mapDimensions,
            });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // ArgumentOutOfRangeException is an ArgumentException, so a setting the
            // method refuses lands here too, with the message the method wrote.
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        foreach (Note note in result.Notes)
            AddRuntimeMessage(
                note.Level == NoteLevel.Warning ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark,
                note.Text);

        da.SetDataList(0, result.Labels);
        da.SetDataTree(1, Trees.FromBuckets(result.Members()));
        da.SetDataList(2, result.Confidence ?? Array.Empty<double>());
        da.SetDataTree(3, Trees.FromRows(result.Centres));
        da.SetDataList(4, Points(result.Map));
        da.SetDataList(5, result.Unplaced());
        da.SetDataList(6, result.Report(names.Count > 0 ? names : null));

        Message = $"{result.Method}\n{result.ClusterCount} clusters";
    }

    /// <summary>The map as points: a 2D map lies on world XY, a 3D one is as it comes.</summary>
    private static IEnumerable<Point3d> Points(double[,]? map)
    {
        if (map is null)
            yield break;

        bool flat = map.GetLength(1) < 3;
        for (int i = 0; i < map.GetLength(0); i++)
            yield return new Point3d(map[i, 0], map[i, 1], flat ? 0.0 : map[i, 2]);
    }
}
