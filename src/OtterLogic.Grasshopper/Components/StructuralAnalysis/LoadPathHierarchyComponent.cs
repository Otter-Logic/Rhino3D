using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Rhino;
using Rhino.Geometry;
using OtterLogic.StructuralAnalysis;

namespace OtterLogic.Grasshopper.Components.StructuralAnalysis;

/// <summary>
/// A structural model's lines and supports in, before any analysis; the lines
/// sorted into the load path hierarchy, what falls outside it, where the model is
/// not joined as meant, and suggested releases, out.
/// <para>
/// Adapter only. Everything it reports is decided by
/// <see cref="LoadPathHierarchy.Analyse"/>; here the curves become end points and
/// the answer is packed back onto the canvas.
/// </para>
/// </summary>
public sealed class LoadPathHierarchyComponent : GH_Component
{
    private const int LinesInput = 0;
    private const int SupportsInput = 1;
    private const int ConnectionsInput = 2;
    private const int ToleranceInput = 3;

    public LoadPathHierarchyComponent()
        : base("Load Path Hierarchy", "Hierarchy",
               "Read a structural model before analysis — its lines and supports, nothing else — and trace how "
               + "gravity finds its way to the ground.\n\n"
               + "Every line is grouped by where it sits in that path: columns by storey, beams as primary, "
               + "secondary and on, trusses with their chords, verticals and diagonals. Lines outside the "
               + "gravity path — bracing, anything that reaches no support, duplicates — come out separately, "
               + "with the reason.\n\n"
               + "Outliers are the places the model is not joined the way it was meant: ends that almost meet, "
               + "an end resting along a member with no node, members crossing with none. The hierarchy is "
               + "read as if they were joined, so one missed snap does not unhook everything above it.\n\n"
               + "Releases are suggested for both ends of every line, from the connection style and where each "
               + "member bears — six true or false values, Fx to Mz, true where released.",
               Categories.Root, Categories.StructuralAnalysis)
    {
    }

    public override Guid ComponentGuid => new("e8c1326f-9f27-4266-a4e7-05e63bc2e644");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("loadpathhierarchy", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Lines", "L",
            "Every member of the model as a line. Only the end points are read, so a curve is read as the "
            + "straight member between its ends.",
            GH_ParamAccess.list);

        pManager.AddPointParameter("Supports", "S",
            "The supported points. Leave it empty and the lowest joints of the model are taken as supported — "
            + "right for a building standing on its ground floor, wrong for anything else.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Connections", "C",
            "How the members are connected, which decides the releases. Simple construction by default: beams "
            + "pinned where they bear, braces and truss webs pinned, columns and chords continuous. A moment "
            + "frame fixes primary beams to their columns; monolithic releases nothing. Plug in Connection "
            + "Style for a dropdown.",
            GH_ParamAccess.item, (int)ConnectionStyle.Simple);

        pManager.AddNumberParameter("Tolerance", "T",
            "Member ends closer than this are joined in the model. The document's absolute tolerance by default. "
            + "Ends and crossings within ten times this are read as meant to meet, and reported.",
            GH_ParamAccess.item);

        pManager[SupportsInput].Optional = true;
        pManager[ToleranceInput].Optional = true;

        var connections = (Param_Integer)pManager[ConnectionsInput];
        foreach (var (label, value) in EnumChoices.Of<ConnectionStyle>())
            connections.AddNamedValue(label, value);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Groups", "G",
            "The lines sorted into the hierarchy. Branch {category; rank; part}: category 0 column, 1 beam, "
            + "2 truss, 3 rafter, 4 brace, 5 strut, 6 hanger, 7 unsupported, 8 excluded. Rank is a column's "
            + "storey, or a beam's, rafter's or truss's tier: 1 bearing only on columns and supports, then by "
            + "order from the ground for members that carry others, and the last tier for members resting on "
            + "another spanning member and carrying nothing. Part is what the line is within "
            + "that: 4 top chord, 5 bottom chord, 6 vertical, 7 diagonal, 1 transfer, 2 cantilever. The same "
            + "branch means the same thing in every model.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Group Names", "N",
            "The name of each line's group — \"Secondary beam\", \"Primary truss top chord\" — grouped like "
            + "Groups.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Indices", "I",
            "The original index of each line, grouped like Groups.",
            GH_ParamAccess.tree);

        pManager.AddCurveParameter("Redundant", "R",
            "Lines outside the gravity load path: bracing, which carries stability rather than gravity; lines "
            + "that reach no support; duplicates and lines too short to be members.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Redundant Reasons", "RR",
            "Why each line in Redundant is there, item for item.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Redundant Indices", "RI",
            "The original index of each line in Redundant, item for item.",
            GH_ParamAccess.list);

        pManager.AddPointParameter("Outliers", "O",
            "Points where the model is not joined the way it looks meant to be.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Outlier Reasons", "OR",
            "What is wrong at each point in Outliers, item for item.",
            GH_ParamAccess.list);

        pManager.AddBooleanParameter("Release Start", "RS",
            "Suggested releases at the start of each line: branch i holds six values for line i — Fx, Fy, Fz, "
            + "Mx, My, Mz in its local axes — true where released. In the order the lines came in.",
            GH_ParamAccess.tree);

        pManager.AddBooleanParameter("Release End", "RE",
            "Suggested releases at the end of each line, as Release Start.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Report", "!",
            "The groups and their sizes, what falls outside the load path, and the outliers by kind.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var curves = new List<Curve>();
        if (!da.GetDataList(LinesInput, curves))
            return;

        for (int i = 0; i < curves.Count; i++)
        {
            if (curves[i] is null || !curves[i].IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Line {i} is missing or invalid. Remove it, or every index after it shifts.");
                return;
            }
        }

        int curved = curves.Count(c => !c.IsLinear());
        if (curved > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{curved} curve(s) are not straight; each is read as the straight member between its ends.");

        var supportPoints = new List<Point3d>();
        da.GetDataList(SupportsInput, supportPoints);

        int connections = (int)ConnectionStyle.Simple;
        if (!da.GetData(ConnectionsInput, ref connections))
            return;
        if (!Enum.IsDefined(typeof(ConnectionStyle), connections))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Connections must be one of "
                + string.Join(", ", EnumChoices.Of<ConnectionStyle>().Select(c => $"{c.Value} ({c.Label})")) + ".");
            return;
        }

        double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? new LoadPathHierarchyOptions().Tolerance;
        da.GetData(ToleranceInput, ref tolerance);

        LoadPathHierarchyResult result;
        try
        {
            result = LoadPathHierarchy.Analyse(
                Rows(curves.Select(c => c.PointAtStart).ToList()), Rows(curves.Select(c => c.PointAtEnd).ToList()),
                supportPoints.Count > 0 ? Rows(supportPoints) : null,
                new LoadPathHierarchyOptions { Tolerance = tolerance, Connections = (ConnectionStyle)connections });
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var groups = new DataTree<Curve>();
        var names = new DataTree<string>();
        var indices = new DataTree<int>();
        foreach (var group in result.Groups)
        {
            var path = new GH_Path((int)group.Category, group.Rank, (int)group.Part);
            foreach (int line in group.Lines)
            {
                groups.Add(curves[line], path);
                names.Add(group.Name, path);
                indices.Add(line, path);
            }
        }

        var redundant = Enumerable.Range(0, result.LineCount).Where(result.IsRedundant).ToArray();

        var releaseStart = new DataTree<bool>();
        var releaseEnd = new DataTree<bool>();
        for (int line = 0; line < result.LineCount; line++)
        {
            releaseStart.AddRange(result.ReleaseStart[line], new GH_Path(line));
            releaseEnd.AddRange(result.ReleaseEnd[line], new GH_Path(line));
        }

        if (result.SupportsInferred)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "No supports given, so the lowest joints were taken as supported. Wire in Supports if the model "
                + "stands on anything else.");
        if (result.Outliers.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{result.Outliers.Count} outlier(s): places the model is not joined as it looks meant to be.");

        da.SetDataTree(0, groups);
        da.SetDataTree(1, names);
        da.SetDataTree(2, indices);
        da.SetDataList(3, redundant.Select(line => curves[line]));
        da.SetDataList(4, redundant.Select(line => result.RedundantReason[line]));
        da.SetDataList(5, redundant);
        da.SetDataList(6, result.Outliers.Select(o => new Point3d(o.X, o.Y, o.Z)));
        da.SetDataList(7, result.Outliers.Select(o => o.Reason));
        da.SetDataTree(8, releaseStart);
        da.SetDataTree(9, releaseEnd);
        da.SetData(10, result.Report());

        Message = $"{result.Groups.Count} groups\n{result.Outliers.Count} outliers";
    }

    private static double[,] Rows(IReadOnlyList<Point3d> points)
    {
        var rows = new double[points.Count, 3];
        for (int i = 0; i < points.Count; i++)
            (rows[i, 0], rows[i, 1], rows[i, 2]) = (points[i].X, points[i].Y, points[i].Z);

        return rows;
    }
}
