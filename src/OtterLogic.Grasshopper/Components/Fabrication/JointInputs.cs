using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using OtterLogic.StructuralDesign;

namespace OtterLogic.Grasshopper.Components.Fabrication;

/// <summary>
/// The five inputs Joint Signature and Connection Typology share — one wording,
/// one reading, so the two cannot describe or read the same model differently.
/// </summary>
internal static class JointInputs
{
    public const int Count = 5;

    /// <summary>Everything read from the inputs, ready for the library.</summary>
    public sealed record Model(
        double[,] Starts, double[,] Ends, double[,]? Supports, double[,]? Attributes, string[]? AttributeNames,
        JointSignatureOptions Options);

    /// <summary>
    /// The five inputs, built here and added by the component — Grasshopper's
    /// parameter manager is only reachable from inside one.
    /// </summary>
    public static IEnumerable<IGH_Param> Parameters()
    {
        yield return new Param_Curve
        {
            Name = "Lines", NickName = "L", Access = GH_ParamAccess.list,
            Description = "Line elements — columns, beams, braces. Only the end points are read. A joint is where line "
                + "ends meet, or where a line end lands on the middle of another line, so members may be drawn whole "
                + "or split at joints: the result is the same.",
        };

        yield return new Param_Point
        {
            Name = "Supports", NickName = "S", Access = GH_ParamAccess.list, Optional = true,
            Description = "Optional. Supported points. A supported joint is a different connection from the same "
                + "geometry in the air — a base plate rather than a splice.",
        };

        yield return new Param_Number
        {
            Name = "Line Attributes", NickName = "A", Access = GH_ParamAccess.tree, Optional = true,
            Description = "Optional. One branch per line, in the same order as Lines, holding numbers about it — "
                + "section depth, plate thickness, a profile code. Each column adds its largest and smallest over a "
                + "joint's lines to the signature, so the same geometry in different members becomes a different "
                + "connection.",
        };

        yield return new Param_String
        {
            Name = "Attribute Names", NickName = "AN", Access = GH_ParamAccess.list, Optional = true,
            Description = "Optional. A name per column of Line Attributes, used in the feature names and descriptions.",
        };

        yield return new Param_Number
        {
            Name = "Tolerance", NickName = "T", Access = GH_ParamAccess.item, Optional = true,
            Description = "Points closer than this are the same point — the document's absolute tolerance by "
                + "default. Line ends within ten times this are read as one joint, as the Structural Insight Engine "
                + "reads them.",
        };
    }

    /// <summary>Reads the inputs, or reports why it cannot and returns null.</summary>
    public static Model? Read(GH_Component component, IGH_DataAccess da)
    {
        var curves = new List<Curve>();
        if (!da.GetDataList(0, curves)) return null;
        curves = curves.Where(c => c is not null).ToList();
        if (curves.Count == 0)
        {
            component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire the model's lines into Lines.");
            return null;
        }

        var supports = new List<Point3d>();
        if (component.Params.Input[1].VolatileDataCount > 0 && !da.GetDataList(1, supports)) return null;

        double[,]? attributes = null;
        if (component.Params.Input[2].VolatileDataCount > 0)
        {
            if (!da.GetDataTree(2, out GH_Structure<GH_Number> tree)) return null;
            if (!TrainingData.TryRead(tree, out double[,] rows, out string? problem, minimumSamples: 1))
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Line Attributes: " + problem);
                return null;
            }

            if (rows.GetLength(0) != curves.Count)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Line Attributes has {rows.GetLength(0)} branch(es) for {curves.Count} line(s). Give one branch per "
                    + "line, in the same order.");
                return null;
            }

            attributes = rows;
        }

        var names = new List<string>();
        if (component.Params.Input[3].VolatileDataCount > 0 && !da.GetDataList(3, names)) return null;

        double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? new JointSignatureOptions().Tolerance;
        da.GetData(4, ref tolerance);

        return new Model(
            Rows(curves.Select(c => c.PointAtStart).ToList()),
            Rows(curves.Select(c => c.PointAtEnd).ToList()),
            supports.Count > 0 ? Rows(supports) : null,
            attributes,
            names.Count > 0 ? names.ToArray() : null,
            new JointSignatureOptions { Tolerance = tolerance });
    }

    public static List<Point3d> Points(double[,] rows)
        => Enumerable.Range(0, rows.GetLength(0)).Select(i => new Point3d(rows[i, 0], rows[i, 1], rows[i, 2])).ToList();

    private static double[,] Rows(List<Point3d> points)
    {
        var rows = new double[points.Count, 3];
        for (int i = 0; i < points.Count; i++)
            (rows[i, 0], rows[i, 1], rows[i, 2]) = (points[i].X, points[i].Y, points[i].Z);
        return rows;
    }
}
