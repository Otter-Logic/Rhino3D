using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino;
using Rhino.Geometry;
using OtterLogic.Construction;

namespace OtterLogic.Grasshopper.Components.Construction;

/// <summary>
/// Lines and supports in; the order the lines go up in out, with the stage each
/// belongs to and what each lands on.
/// <para>
/// Adapter only. The order is decided by <see cref="ErectionSequence.Plan"/>; here
/// curves become end points and the answer is packed back with the original curves,
/// in the order they go up and in the order they came in.
/// </para>
/// </summary>
public sealed class ErectionSequenceComponent : GH_Component
{
    private const int LinesInput = 0;
    private const int SupportsInput = 1;
    private const int ToleranceInput = 2;

    public ErectionSequenceComponent()
        : base("Erection Sequence", "Erect",
               "The order a structure's pieces can go up in, read from its lines and supports and nothing else.\n\n"
               + "Two rules, true of any erection: a piece is lifted only onto something already standing — a "
               + "support or an earlier piece — and what carries goes up before what it carries. What carries what "
               + "is read the way the Structural Insight Engine reads it, from the supports up, so a column goes up "
               + "before the beam on it and a truss before the purlins on it, with no names involved. Among the "
               + "pieces that could go next, the lowest goes first.\n\n"
               + "Play the result with Drop Animation. For the hand-over hierarchy itself, and the groups the "
               + "structure falls into, use the Structural Insight Engine; for a plain dependency order over any "
               + "graph, Dependency Levels.",
               Categories.Root, Categories.Construction)
    {
    }

    public override Guid ComponentGuid => new("8f2c6e1a-4b7d-4a3e-9c15-2d6f0e7b9a41");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("erectionsequence", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Lines", "L",
            "The structure's line elements — columns, beams, braces, chords, webs. Only the end points are read, "
            + "so explode polylines. A run drawn in pieces is read as one member for what it carries, but each "
            + "piece goes up on its own.",
            GH_ParamAccess.list);

        pManager.AddPointParameter("Supports", "S",
            "Where the structure meets the ground: a point at each supported line end. Without supports the "
            + "sequence simply works upwards from the lowest piece, with no notion of what carries what.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Tolerance", "T",
            "Points closer than this are one point. Line ends within ten times this weld into one joint — the "
            + "same rule the Structural Insight Engine uses, so the two never disagree about what rests on what. "
            + "The document's absolute tolerance by default.",
            GH_ParamAccess.item);

        pManager[SupportsInput].Optional = true;
        pManager[ToleranceInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Sequence", "Seq", "The lines in the order they go up.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Order", "O",
            "Per line, in the order they came in: its place in the sequence, 0 first. Wire this into Drop Animation.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Stage", "St",
            "Per line: the stage it goes up in, 0 for what rests on the ground. Stages never go backwards along "
            + "the sequence, so each is one contiguous run of it. Wire this into Drop Animation instead of Order "
            + "to drop each stage as one.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Stages", "SL",
            "The lines of each stage in one branch per stage, in sequence order within the branch.",
            GH_ParamAccess.tree);
        pManager.AddIntegerParameter("Rests On", "R",
            "Per line: the index of the earlier line it lands on, or -1 for a line landing on a support.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Level", "Lv",
            "Per line: how many hand-overs its assembly stands from the ground as the load paths read it, "
            + "0 on the supports, -1 with no route to one. The stages are cut from this.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Unsupported", "U",
            "Lines that had nothing to land on when their turn came — the lowest of a piece no support reaches. "
            + "Each starts its piece in mid-air; a support is probably missing there.",
            GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "Rp", "The sequence stage by stage, and anything worth knowing.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? new ErectionSequenceOptions().Tolerance;
        da.GetData(ToleranceInput, ref tolerance);

        var curves = new List<Curve>();
        da.GetDataList(LinesInput, curves);
        for (int i = 0; i < curves.Count; i++)
        {
            if (curves[i] is null || !curves[i].IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Line {i} is missing or invalid. Remove it, or every index after it shifts.");
                return;
            }
        }

        if (curves.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire in the structure's lines.");
            return;
        }

        int curved = curves.Count(c => !c.IsLinear(Math.Max(tolerance, 1e-9)));
        if (curved > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{curved} line(s) are not straight and are read as the straight element between their ends.");

        var supportPoints = new List<Point3d>();
        da.GetDataList(SupportsInput, supportPoints);

        ErectionSequenceResult result;
        try
        {
            result = ErectionSequence.Plan(
                Rows(curves.Select(c => c.PointAtStart).ToList()),
                Rows(curves.Select(c => c.PointAtEnd).ToList()),
                supportPoints.Count > 0 ? Rows(supportPoints) : null,
                new ErectionSequenceOptions { Tolerance = tolerance });
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var stages = new DataTree<Curve>();
        var byStage = result.Stages();
        for (int s = 0; s < byStage.Length; s++)
        {
            var path = new GH_Path(s);
            stages.EnsurePath(path);
            foreach (int e in byStage[s])
                stages.Add(curves[e], path);
        }

        if (result.Unsupported.Length > 0 && supportPoints.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{result.Unsupported.Length} line(s) started a piece with nothing to land on — see Unsupported.");
        foreach (string note in result.Notes)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);

        da.SetDataList(0, result.Sequence.Select(e => curves[e]));
        da.SetDataList(1, result.Order);
        da.SetDataList(2, result.Stage);
        da.SetDataTree(3, stages);
        da.SetDataList(4, result.RestsOn);
        da.SetDataList(5, result.Level);
        da.SetDataList(6, result.Unsupported);
        da.SetData(7, result.Report());

        Message = $"{result.ElementCount} pieces\n{result.StageCount} stages";
    }

    private static double[,] Rows(IReadOnlyList<Point3d> points)
    {
        var rows = new double[points.Count, 3];
        for (int i = 0; i < points.Count; i++)
            (rows[i, 0], rows[i, 1], rows[i, 2]) = (points[i].X, points[i].Y, points[i].Z);

        return rows;
    }
}
