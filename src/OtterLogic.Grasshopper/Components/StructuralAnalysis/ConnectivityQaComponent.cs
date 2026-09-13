using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino;
using Rhino.Geometry;
using OtterLogic.StructuralAnalysis;

namespace OtterLogic.Grasshopper.Components.StructuralAnalysis;

/// <summary>
/// A structural model's lines in, before any analysis; whole disconnected
/// sub-structures, how weakly it holds together as a whole, and a
/// connectivity-and-spatial map of its joints, out — alongside the exact
/// duplicates, gaps and crossings <see cref="LoadPathHierarchyComponent"/>
/// also reports.
/// <para>
/// Adapter only. Everything it reports is decided by
/// <see cref="ConnectivityQa.Analyse"/>; here the curves become end points and
/// the answer is packed back onto the canvas.
/// </para>
/// </summary>
public sealed class ConnectivityQaComponent : GH_Component
{
    private const int LinesInput = 0;
    private const int ToleranceInput = 1;
    private const int RegionsInput = 2;
    private const int SupportsInput = 3;

    public ConnectivityQaComponent()
        : base("Connectivity QA", "ConnectQA",
               "Check a structural model's connectivity before analysis: the exact geometry problems — "
               + "duplicated lines, gaps, crossings, near misses, orphan and free-end joints — and how the "
               + "model's own connectivity graph holds together.\n\n"
               + "An end resting along another member with no node drawn there — a secondary beam on a "
               + "primary, a spoke on a ring — is connected, so it comes out in Bearings, never in Outliers.\n\n"
               + "A model can pass every exact check and still be two dense regions joined by a single "
               + "member. Algebraic Connectivity is a single number for how weakly tied together the whole "
               + "model is, close to zero exactly in that case; Weak Cut Side shows which joints sit on each "
               + "side of the weakest tie, so the members crossing it are the ones actually worth a look. "
               + "Regions groups joints by spatial proximity and connectivity together, so every group at "
               + "every cut is a connected part of the real model.",
               Categories.Root, Categories.StructuralAnalysis)
    {
    }

    public override Guid ComponentGuid => new("c1a6e2b4-7d9f-4a1e-9c3d-2f6b8e0a5c17");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("connectivityqa", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Lines", "L",
            "Every member of the model as a line. Only the end points are read, so a curve is read as the "
            + "straight member between its ends.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Tolerance", "T",
            "Member ends closer than this are joined in the model. The document's absolute tolerance by "
            + "default. Ends and crossings within ten times this are read as meant to meet, and reported.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Regions", "R",
            "How many regions to cut the connectivity-and-spatial map into.",
            GH_ParamAccess.item, 8);

        // Appended after Regions rather than beside Lines, so a definition
        // wired against Tolerance and Regions by position is not disturbed.
        pManager.AddPointParameter("Supports", "S",
            "The supported points. Leave it empty and the lowest joints of the model are taken as supported — "
            + "without this, a column foot with nothing else framing into it reads as a free-end outlier, "
            + "indistinguishable from a genuine missed connection.",
            GH_ParamAccess.list);

        pManager[ToleranceInput].Optional = true;
        pManager[RegionsInput].Optional = true;
        pManager[SupportsInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Outliers", "O",
            "Points where the model is not joined the way it looks meant to be, or is not joined at all. Ends "
            + "resting along another member are connected, and come out in Bearings instead.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Outlier Reasons", "OR",
            "What is wrong at each point in Outliers, item for item.",
            GH_ParamAccess.list);

        pManager.AddPointParameter("Isolated", "X",
            "Joints with no connections at all.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Components", "C",
            "Connected component of every joint, in the order the model's joints were welded. One value "
            + "everywhere means the model is a single connected structure.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Algebraic Connectivity", "A",
            "How weakly tied together the whole model is, between 0 and 2. Zero whenever Components reports "
            + "more than one; close to zero for a model that is technically one piece but held together by "
            + "very little.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Eigengap", "G",
            "Gap between the third- and second-smallest Laplacian eigenvalue. Large means the weak cut below "
            + "is a natural one to draw.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Weak Cut Side", "W",
            "Which side of the model's weakest tie each joint sits on, 0 or 1, or -1 for an isolated joint. "
            + "The members crossing from one side to the other are the ones actually worth a look.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Regions", "R",
            "Joints grouped by spatial proximity and connectivity together, cut to the Regions input. Every "
            + "group is a connected part of the real model.",
            GH_ParamAccess.list);

        pManager.AddPointParameter("Joints", "J",
            "Every joint's position, after welding at the snap distance — what Components, Weak Cut Side and "
            + "Regions are indexed by.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Report", "!",
            "The connectivity read, what it found, and what is worth a look.",
            GH_ParamAccess.item);

        // Appended after Report rather than beside Outliers, so a definition
        // wired against the outputs above by position is not disturbed.
        pManager.AddPointParameter("Bearings", "B",
            "Ends resting along another member with no node drawn there — connected, so kept out of Outliers.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Bearing Reasons", "BR",
            "Which member each point in Bearings rests on, and how many ends share it, item for item.",
            GH_ParamAccess.list);
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

        double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? new ConnectivityQaOptions().Tolerance;
        da.GetData(ToleranceInput, ref tolerance);

        int regions = 8;
        if (!da.GetData(RegionsInput, ref regions))
            return;

        var supportPoints = new List<Point3d>();
        da.GetDataList(SupportsInput, supportPoints);

        ConnectivityQaResult result;
        try
        {
            result = ConnectivityQa.Analyse(
                Rows(curves.Select(c => c.PointAtStart).ToList()), Rows(curves.Select(c => c.PointAtEnd).ToList()),
                supportPoints.Count > 0 ? Rows(supportPoints) : null,
                new ConnectivityQaOptions { Tolerance = tolerance, Regions = regions });
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var joints = Enumerable.Range(0, result.JointCount)
            .Select(j => new Point3d(result.Joints[j, 0], result.Joints[j, 1], result.Joints[j, 2])).ToArray();

        if (result.SupportsInferred)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "No supports given, so the lowest joints were taken as supported. Wire in Supports if the "
                + "model stands on anything else, or free ends there will be misread.");
        if (result.ComponentCount > 1)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"The model is not one connected structure: {result.ComponentCount} separate pieces.");
        if (result.Isolated.Length > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{result.Isolated.Length} joint(s) have no connections at all. See Isolated.");

        da.SetDataList(0, result.Outliers.Select(o => new Point3d(o.X, o.Y, o.Z)));
        da.SetDataList(1, result.Outliers.Select(o => o.Reason));
        da.SetDataList(2, result.Isolated.Select(j => joints[j]));
        da.SetDataList(3, result.Component);
        da.SetData(4, result.AlgebraicConnectivity);
        da.SetData(5, result.EigenGap);
        da.SetDataList(6, result.WeakCutSide);
        da.SetDataList(7, result.Regions);
        da.SetDataList(8, joints);
        da.SetData(9, result.Report());
        da.SetDataList(10, result.Bearings.Select(b => new Point3d(b.X, b.Y, b.Z)));
        da.SetDataList(11, result.Bearings.Select(b => b.Reason));

        Message = result.ComponentCount == 1
            ? $"connectivity {result.AlgebraicConnectivity:0.00}"
            : $"{result.ComponentCount} pieces";
    }

    private static double[,] Rows(IReadOnlyList<Point3d> points)
    {
        var rows = new double[points.Count, 3];
        for (int i = 0; i < points.Count; i++)
            (rows[i, 0], rows[i, 1], rows[i, 2]) = (points[i].X, points[i].Y, points[i].Z);

        return rows;
    }
}
