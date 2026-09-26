using System.Drawing;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using OtterLogic.StructuralDesign;

namespace OtterLogic.Grasshopper.Components.StructuralDesign;

/// <summary>
/// A model's lines in; the levels and structural grid they imply, named, and the
/// elements not quite on them, out.
/// <para>
/// Adapter only. Everything it reports is decided by
/// <see cref="GridLevelInference.Infer"/>; here curves become end points, and levels
/// and gridlines become planes and lines.
/// </para>
/// <para>
/// Elevations and Orientation came off on 2026-09-26: a level plane's origin already
/// holds its elevation, and how each line stands is Describe Member's to report.
/// </para>
/// </summary>
public sealed class GridLevelInferenceComponent : GH_Component
{
    public GridLevelInferenceComponent()
        : base("Grid and Level Inference", "Grids",
               "Read the levels and structural grid a model's lines imply, for a model that arrived without "
               + "them — an IFC import, a consultant's line model, an old drawing.\n\n"
               + "Nothing is assumed: not a floor height, a bay size, a grid direction or a tolerance for "
               + "\"on the grid\". Levels are the heights the columns' tops and feet gather at — a flat "
               + "purlin on a pitched roof lies at a height of its own, and is not a storey; grid directions "
               + "are the directions level lines gather in; gridlines are the positions where columns stand "
               + "and primary framing runs. Each is found from the gaps in the model's own values. Only the "
               + "names are conventions: gridlines numbered one way and lettered the other (no I or O), "
               + "levels Level 00 upward.\n\n"
               + "Issues lists the columns that belong to a level or gridline but are not quite on it — "
               + "the column a few millimetres out — measured against how tightly the rest of that level "
               + "or gridline holds, not against a fixed number.",
               Categories.Root, Categories.StructuralDesign)
    {
    }

    public override Guid ComponentGuid => new("add95b52-d629-41f9-953e-6223264ec369");

    // The second step: naming what the model implies, once the engine has read it.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("gridlevelinference", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Lines", "L",
            "Line elements — columns, beams, braces. Only the end points are read, so explode polylines and "
            + "split columns and beams where they meet for the best result: a gridline is found where beam "
            + "ends land on column ends.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Tolerance", "T",
            "Points closer than this are the same point — it decides which beams frame into which columns, "
            + "and no level or gridline is ever split by a gap this small. The document's absolute tolerance "
            + "by default. Not a tolerance for \"on the grid\": that is read from the model.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Extension", "E",
            "How far each gridline is drawn past the last element on it, at both ends, in model units.",
            GH_ParamAccess.item, 0.0);

        pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPlaneParameter("Level Planes", "LP",
            "One plane per level, lowest first. The origin's Z is the level's elevation: the median of the column "
            + "ends on it.",
            GH_ParamAccess.list);
        pManager.AddTextParameter("Level Names", "LN", "Level 00, Level 01, ... matching Level Planes.", GH_ParamAccess.list);

        pManager.AddLineParameter("Gridlines", "G",
            "One line per gridline at the lowest level's height, the numbered family first, each in order of position.",
            GH_ParamAccess.list);
        pManager.AddTextParameter("Gridline Names", "GN", "The name of each gridline, matching Gridlines.", GH_ParamAccess.list);

        pManager.AddTextParameter("Grid Label", "GL",
            "Per line, in the order they came in: a column's grid position, such as B/3, or empty for anything that "
            + "is not a column on the grid.",
            GH_ParamAccess.list);
        pManager.AddTextParameter("Level Label", "LL",
            "Per line: the level it sits on, or the levels it spans for a column or brace, such as Level 00–Level 02. "
            + "Empty for a line on no level — a purlin partway up a roof.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Issues", "I",
            "Columns not quite on their level or gridline, furthest off first — and columns on no gridline.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Issue Lines", "IL", "The line index behind each issue, matching Issues.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var curves = new List<Curve>();
        if (!da.GetDataList(0, curves)) return;

        double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? new GridLevelOptions().Tolerance;
        da.GetData(1, ref tolerance);

        double extension = 0.0;
        if (!da.GetData(2, ref extension)) return;

        curves = curves.Where(c => c is not null).ToList();
        if (curves.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire the model's lines into Lines.");
            return;
        }

        try
        {
            var result = GridLevelInference.Infer(
                SurfaceInput.Rows(curves.Select(c => c.PointAtStart).ToList()),
                SurfaceInput.Rows(curves.Select(c => c.PointAtEnd).ToList()),
                new GridLevelOptions { Tolerance = tolerance, Extension = extension });

            foreach (string note in result.Notes)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);

            if (result.Issues.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{result.Issues.Count} element(s) not quite on their level or gridline — see Issues.");

            da.SetDataList(0, result.Levels.Select(l => new Plane(new Point3d(0, 0, l.Elevation), Vector3d.ZAxis)));
            da.SetDataList(1, result.Levels.Select(l => l.Name));

            da.SetDataList(2, result.Gridlines.Select(g =>
                new Line(new Point3d(g.Start[0], g.Start[1], g.Start[2]), new Point3d(g.End[0], g.End[1], g.End[2]))));
            da.SetDataList(3, result.Gridlines.Select(g => g.Name));

            da.SetDataList(4, result.GridLabel);
            da.SetDataList(5, Enumerable.Range(0, result.LineCount).Select(i => LevelLabel(result, i)));

            da.SetDataList(6, result.Issues.Select(i => i.Message));
            da.SetDataList(7, result.Issues.Select(i => i.Element));

            Message = $"{result.Levels.Count} levels\n{result.Gridlines.Count} gridlines";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    private static string LevelLabel(GridLevelResult result, int line)
    {
        var levels = new[] { result.StartLevel[line], result.EndLevel[line] }
            .Where(level => level >= 0)
            .Distinct()
            .OrderBy(level => level)
            .Select(level => result.Levels[level].Name)
            .ToArray();

        return string.Join("–", levels);
    }
}
