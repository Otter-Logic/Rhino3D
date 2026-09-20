using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Decomposition;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// Samples laid out as 2D or 3D points, near where they are alike and far
/// where they differ — something to look at.
/// <para>
/// Adapter only. The map belongs to <see cref="MultidimensionalScaling"/>, which
/// will make one of any number of dimensions. Limiting it to two or three is this
/// component's own decision, because its output is points in a Rhino viewport and
/// a viewport shows no more than three.
/// </para>
/// </summary>
public sealed class DataMapComponent : GH_Component
{
    public DataMapComponent()
        : base("Data Map", "Map",
               "Lay samples out as points on a 2D or 3D map you can see: samples that are alike land "
               + "close together, samples that differ land far apart. Colour the points by any "
               + "method's Result to see whether its groups are real islands, where they overlap, and "
               + "which samples sit between them.\n\n"
               + "A map for looking at, so always 2 or 3 dimensions — however many columns the data "
               + "has. Only distances on the map mean anything: the axes have no units or names, and "
               + "a rotated or mirrored map says the same thing. Stress says how far to trust it. Cluster "
               + "on the full data, not on the map — the map is for seeing a result, not a step "
               + "towards one.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("4b66c88d-a0a7-4aa3-bb2c-d646ac7c7978");

    // Beside Principal Components: it decomposes samples into a few coordinates.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("datamap", 24);

    private static readonly MultidimensionalScalingOptions Defaults = new();

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T",
            "The samples to map, compared by straight-line distance. Scale columns in different "
            + "units first with Prepare Features, or the largest units decide the map. Wire this or "
            + "Distances, not both. " + TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Distances", "D",
            "How far apart every pair of samples is, when you have your own measure of difference "
            + "rather than columns — route lengths on a graph, 1 − Consensus agreement, anything. "
            + "One branch per sample holding its distance to every sample, so n branches of n "
            + "values; zero to itself, the same both ways round. Wire this or Training Inputs, not "
            + "both.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Dimensions", "N",
            "2 for a flat map — the easiest to read. 3 for a map you can orbit in the viewport, "
            + "when two leave too much overlapping.\n\n"
            + "Nothing else: a map is for looking at, and a viewport shows at most three. This is "
            + "the size of the map, not the number of columns in the data — map any number of "
            + "columns in 2 or 3.",
            GH_ParamAccess.item, 2);

        pManager.AddBooleanParameter("Refine", "R",
            "Improve the map to fit the distances more closely, starting from the exact classical "
            + "map. Never makes it worse; turn off for speed on a very large set.",
            GH_ParamAccess.item, Defaults.Refine);

        pManager.AddPlaneParameter("Plane", "P",
            "Where to draw the map. Its X and Y axes carry the map's first two axes, its normal the "
            + "third.",
            GH_ParamAccess.item, Plane.WorldXY);

        pManager.AddNumberParameter("Size", "S",
            "Optional. Scale the drawn map so its widest extent is this long, in model units. "
            + "Unwired, points sit at the map's own distances — tiny for prepared features, since "
            + "those are in standard deviations. Coordinates are never scaled.",
            GH_ParamAccess.item);

        pManager[0].Optional = true;
        pManager[1].Optional = true;
        pManager[5].Optional = true;

        var dimensions = (Param_Integer)pManager[2];
        dimensions.AddNamedValue("2D map", 2);
        dimensions.AddNamedValue("3D map", 3);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Points", "P",
            "One point per sample, in the order the samples arrived — colour, tag or pick them like "
            + "any other geometry. Index i is sample i.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Coordinates", "C",
            "One branch per sample: its 2 or 3 map coordinates as numbers, unscaled.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Stress", "S",
            "How far the map's distances disagree with the real ones, as a share: 0 is a perfect "
            + "map. Kruskal's rule of thumb reads about 0.05 as good, 0.1 as fair and 0.2 as poor — "
            + "above that, points that look close may not be. Try 3 dimensions if it is high in 2.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Distortion", "X",
            "The same measure per sample. High values are the points not to trust: their "
            + "neighbours on the map are not really their neighbours.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Retained", "K",
            "Share of the structure the map's axes carry, 0 to 1 (Mardia's criterion, which also "
            + "works for distances no flat space can hold). Not the same number as Principal "
            + "Components' Retained Ratio, which weighs axes differently.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Axis Strength", "E",
            "How much each map axis carries, strongest first — an eigenvalue per axis. A third "
            + "axis far weaker than the first two says a 2D map would show nearly everything.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        bool hasFeatures = Params.Input[0].VolatileDataCount > 0;
        bool hasDistances = Params.Input[1].VolatileDataCount > 0;

        if (hasFeatures == hasDistances)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                hasFeatures
                    ? "Wire Training Inputs or Distances, not both — it cannot tell which difference to map."
                    : "Wire the samples to map: their columns into Training Inputs, or your own distances "
                        + "between them into Distances.");
            return;
        }

        int dimensions = 2;
        bool refine = Defaults.Refine;
        var plane = Plane.WorldXY;
        if (!da.GetData(2, ref dimensions)) return;
        if (!da.GetData(3, ref refine)) return;
        if (!da.GetData(4, ref plane)) return;

        double? size = null;
        double requested = 0.0;
        if (da.GetData(5, ref requested))
        {
            if (!(requested > 0.0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Size is {requested}; it must be above zero.");
                return;
            }

            size = requested;
        }

        int index = hasFeatures ? 0 : 1;
        if (!da.GetDataTree(index, out GH_Structure<GH_Number> tree)) return;
        if (!TrainingData.TryRead(tree, out double[,] data, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, (hasFeatures ? "Training Inputs: " : "Distances: ") + problem);
            return;
        }

        if (dimensions is not (2 or 3))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, DimensionsGuidance(dimensions, hasFeatures ? data.GetLength(1) : null));
            return;
        }

        try
        {
            var options = Defaults with { Dimensions = dimensions, Refine = refine };
            var map = hasFeatures
                ? MultidimensionalScaling.FromFeatures(data, options)
                : MultidimensionalScaling.FromDistances(data, options);

            foreach (string note in map.Notes)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);

            da.SetDataList(0, Draw(map.Coordinates, plane, size));
            da.SetDataTree(1, Trees.FromRows(map.Coordinates));
            da.SetData(2, map.Stress);
            da.SetDataList(3, map.Distortion);
            da.SetData(4, map.Retained);
            da.SetDataList(5, map.Eigenvalues);

            Message = $"{dimensions}D map\nstress {map.Stress:0.000}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    /// <summary>
    /// Why the map refuses a dimension count, written for somebody who does not
    /// yet know that a map's dimensions and the data's columns are different
    /// things — the likeliest reason for asking for more than three.
    /// </summary>
    private static string DimensionsGuidance(int dimensions, int? columns)
    {
        if (dimensions > 3)
        {
            string columnsLine = columns is { } d
                ? $"Your data has {d} columns — that is fine, and it is exactly what a map is for: every one "
                    + "of them is taken into account in a 2 or 3 dimensional map. "
                : string.Empty;

            return $"Dimensions is {dimensions}, but a Data Map makes points to look at, and Rhino shows points "
                + "in 2 or 3 dimensions only — there would be nothing to see. Set Dimensions to 2 for a flat map "
                + "(easiest to read) or 3 for one you can orbit.\n\n"
                + columnsLine
                + $"If you wanted the data squeezed into {dimensions} columns to feed a clustering method rather "
                + $"than to look at, use Principal Components with Components set to {dimensions} — though "
                + "clustering the full prepared data is usually better than clustering a squeezed copy.";
        }

        if (dimensions == 1)
            return "Dimensions is 1, which lays every sample along one line — samples that are nothing alike "
                + "end up on top of each other, so the picture misleads. Set Dimensions to 2 for a flat map or 3 "
                + "for one you can orbit.";

        return $"Dimensions is {dimensions}. A Data Map is 2D or 3D: set 2 for a flat map (easiest to read) "
            + "or 3 for one you can orbit.";
    }

    /// <summary>
    /// The coordinates as points on the plane, scaled to the requested size.
    /// Drawing only — the Coordinates output keeps the map's own distances.
    /// </summary>
    private static List<Point3d> Draw(double[,] coordinates, Plane plane, double? size)
    {
        int n = coordinates.GetLength(0);
        int k = coordinates.GetLength(1);

        double factor = 1.0;
        if (size is { } target)
        {
            double extent = 0.0;
            for (int a = 0; a < k; a++)
            {
                double low = double.PositiveInfinity, high = double.NegativeInfinity;
                for (int i = 0; i < n; i++)
                {
                    low = Math.Min(low, coordinates[i, a]);
                    high = Math.Max(high, coordinates[i, a]);
                }

                extent = Math.Max(extent, high - low);
            }

            if (extent > 0.0)
                factor = target / extent;
        }

        var points = new List<Point3d>(n);
        for (int i = 0; i < n; i++)
            points.Add(plane.PointAt(
                coordinates[i, 0] * factor,
                k > 1 ? coordinates[i, 1] * factor : 0.0,
                k > 2 ? coordinates[i, 2] * factor : 0.0));

        return points;
    }
}
