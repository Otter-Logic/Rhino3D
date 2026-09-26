using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.StructuralForm;
using Rhino;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.StructuralForm;

/// <summary>
/// Runs primary beams along gridlines between the columns that stand on them.
/// <para>
/// Adapter only. Reading the columns in plan, finding them on the gridlines
/// and cutting and lifting the beams all belong to
/// <see cref="GridBeamsGenerator"/>, which the OtterBeam Rhino command calls
/// in exactly the same way.
/// </para>
/// </summary>
public sealed class GridBeamsComponent : GH_Component
{
    public GridBeamsComponent()
        : base("Grid Beams", "Beams",
               "Run a primary beam along every gridline between each pair of neighbouring columns "
               + "standing on it, at a level or on a surface.\n\n"
               + "Columns and gridlines are read in plan: a column is where it crosses the level, "
               + "columns stacked storey on storey are one, and a gridline at any height is the "
               + "same gridline. A crossing with no column gets no beam through it, and nothing "
               + "runs past the last column on a line. Give a Surface and the beams follow it "
               + "instead of the level. For the columns themselves, use Grid Columns; to fill the "
               + "panels between these beams, Beam Infill.",
               Categories.Root, Categories.StructuralForm)
    {
    }

    public override Guid ComponentGuid => new("87e9f57e-2006-4d69-84b4-a92f0be48b94");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap? Icon => EmbeddedIcons.Load("gridbeams", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Columns", "C",
            "The columns the beams connect into, as lines or curves, in any order. A curve that "
            + "runs further in plan than it rises is not a column and is left out.",
            GH_ParamAccess.list);

        pManager.AddCurveParameter("Gridlines", "G",
            "The gridlines the beams run along, in any order, at any height. Vertical curves "
            + "caught up in the list are ignored.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Level", "L",
            "Height of the beams, as world Z. Ignored when a Surface is given.",
            GH_ParamAccess.item, 0.0);

        pManager.AddBrepParameter("Surface", "S",
            "A surface for the beams to follow instead of the level: a sloping roof, a ramp. Each "
            + "beam is projected onto it straight up or down from where it lies in plan.",
            GH_ParamAccess.item);
        pManager[3].Optional = true;

        pManager.AddNumberParameter("Reach", "R",
            "How far a column may sit off a gridline, in plan, and still count as on it. Zero "
            + "uses the document tolerance, which is right for columns the grid tools placed; "
            + "raise it for a model drawn by hand.",
            GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Beam", "B",
            "The beams, one branch per gridline in Plan order; item k of a branch runs from the "
            + "k-th column on that line to the next. Straight where the gridline is straight, an "
            + "arc where it is an arc.",
            GH_ParamAccess.tree);
        pManager.AddPointParameter("Node", "N",
            "Where beams meet columns, at the level or on the surface, with coincident ones merged "
            + "and ordered by X then Y.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Plan", "P",
            "The gridlines flattened to Z = 0: the grid the beams were cut from.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var columns = new List<Curve>();
        var gridlines = new List<Curve>();
        double level = 0.0;
        Brep? surface = null;
        double reach = 0.0;

        if (!da.GetDataList(0, columns)) return;
        if (!da.GetDataList(1, gridlines)) return;
        if (!da.GetData(2, ref level)) return;
        da.GetData(3, ref surface);
        if (!da.GetData(4, ref reach)) return;

        // Nulls are a wiring accident rather than a modelling one, so they are
        // dropped here; everything else is the generator's to validate.
        columns.RemoveAll(c => c is null);
        gridlines.RemoveAll(g => g is null);

        var options = new GridBeamsOptions
        {
            Level = level,
            Surface = surface,
            Reach = reach,
            Tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01,
        };

        try
        {
            GridBeams beams = GridBeamsGenerator.Generate(columns, gridlines, options);

            foreach (FormNote note in beams.Notes)
                AddRuntimeMessage(
                    note.Level == FormNoteLevel.Warning
                        ? GH_RuntimeMessageLevel.Warning
                        : GH_RuntimeMessageLevel.Remark,
                    note.Message);

            da.SetDataTree(0, Trees.FromRows(beams.ByGridline));
            da.SetDataList(1, beams.Nodes);
            da.SetDataList(2, beams.Plan);

            Message = $"{beams.Beams.Count} beams\n{beams.Nodes.Count} nodes";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
