using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.StructuralForm;
using Rhino;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.StructuralForm;

/// <summary>
/// Stands a column at every crossing of a set of gridlines.
/// <para>
/// Adapter only. Flattening the grid, finding and merging the crossings and
/// standing the columns all belong to <see cref="GridColumnsGenerator"/>,
/// which the OtterColumn Rhino command calls in exactly the same way.
/// </para>
/// </summary>
public sealed class GridColumnsComponent : GH_Component
{
    public GridColumnsComponent()
        : base("Grid Columns", "Columns",
               "Stand a column at every crossing of the gridlines, from a base height to a top "
               + "height.\n\n"
               + "The gridlines are read in plan — flattened to Z = 0 before their crossings are "
               + "found — so curves at any height make one grid. Every crossing gets a column; "
               + "leave a gridline out, or delete a column after, where one is not wanted. Set Top "
               + "equal to Base to see the crossings alone.",
               Categories.Root, Categories.StructuralForm)
    {
    }

    public override Guid ComponentGuid => new("275d627e-5b42-441d-a31c-068294070b5b");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override Bitmap? Icon => EmbeddedIcons.Load("gridcolumns", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Gridlines", "G",
            "The gridlines, in any order — lines, arcs or polylines, at any height. Vertical "
            + "curves caught up in the list are ignored.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Base", "B",
            "Height of the foot of every column, as world Z.",
            GH_ParamAccess.item, 0.0);

        pManager.AddNumberParameter("Top", "T",
            "Height of the head of every column, as world Z. Below Base draws the columns "
            + "downward; equal to Base places none and shows the crossings.",
            GH_ParamAccess.item, 3.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddLineParameter("Column", "C",
            "One line per crossing, from Base to Top, in the same order as Crossing.",
            GH_ParamAccess.list);
        pManager.AddPointParameter("Crossing", "X",
            "Where the gridlines cross in plan, at Z = 0, with coincident crossings merged.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Plan", "P",
            "The gridlines flattened to Z = 0: the grid the columns were read from.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var gridlines = new List<Curve>();
        double baseHeight = 0.0;
        double topHeight = 0.0;

        if (!da.GetDataList(0, gridlines)) return;
        if (!da.GetData(1, ref baseHeight)) return;
        if (!da.GetData(2, ref topHeight)) return;

        // Nulls are a wiring accident rather than a modelling one, so they are
        // dropped here; everything else is the generator's to validate.
        gridlines.RemoveAll(gridline => gridline is null);

        var options = new GridColumnsOptions
        {
            Base = baseHeight,
            Top = topHeight,
            Tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01,
        };

        try
        {
            GridColumns columns = GridColumnsGenerator.Generate(gridlines, options);

            foreach (FormNote note in columns.Notes)
                AddRuntimeMessage(
                    note.Level == FormNoteLevel.Warning
                        ? GH_RuntimeMessageLevel.Warning
                        : GH_RuntimeMessageLevel.Remark,
                    note.Message);

            da.SetDataList(0, columns.Columns);
            da.SetDataList(1, columns.Crossings);
            da.SetDataList(2, columns.Plan);

            Message = $"{columns.Crossings.Count} crossings\n{columns.Columns.Count} columns";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
