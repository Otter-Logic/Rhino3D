using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using OtterLogic.StructuralForm;
using Rhino;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.StructuralForm;

/// <summary>
/// Sets out a rectangular structural grid from its bay spacings.
/// <para>
/// Adapter only. The running totals, the lines and the nodes all belong to
/// <see cref="RectangularGridGenerator"/>, which the OtterGrid Rhino command
/// calls in exactly the same way.
/// </para>
/// </summary>
public sealed class RectangularGridComponent : GH_Component
{
    public RectangularGridComponent()
        : base("Rectangular Grid", "RectGrid",
               "Set out a rectangular structural grid from its bay spacings: gridlines both "
               + "ways and a node at every crossing.\n\n"
               + "X spacings are the bays measured along the plane's X axis, so each places a "
               + "gridline running in Y; Y spacings the other way. Unequal bays are just a list. "
               + "Overhang runs every gridline past the outer ones for grid bubbles.\n\n"
               + "For a grid over a surface, use Surface Grid; for a grid about a centre, Radial "
               + "Grid; to stand columns on gridlines already drawn, Grid Columns.",
               Categories.Root, Categories.StructuralForm)
    {
    }

    public override Guid ComponentGuid => new("ae6c24e7-199b-4797-8082-d5bc22f9b52e");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap? Icon => EmbeddedIcons.Load("rectangulargrid", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddPlaneParameter("Plane", "P",
            "Where the grid sits and which way it faces. The first gridline each way passes "
            + "through the origin.",
            GH_ParamAccess.item, Plane.WorldXY);

        int x = pManager.AddNumberParameter("X Spacings", "X",
            "The bays measured along the plane's X axis, first to last. One more gridline than "
            + "spacings, each running the depth of the grid in Y.",
            GH_ParamAccess.list);
        ((Param_Number)pManager[x]).SetPersistentData(6.0, 6.0, 6.0);

        int y = pManager.AddNumberParameter("Y Spacings", "Y",
            "The bays measured along the plane's Y axis, first to last. One more gridline than "
            + "spacings, each running the width of the grid in X.",
            GH_ParamAccess.list);
        ((Param_Number)pManager[y]).SetPersistentData(8.0, 8.0);

        pManager.AddNumberParameter("Overhang", "O",
            "How far every gridline runs past the outermost gridline it crosses, at both ends. "
            + "Zero ends them at the corners.",
            GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddLineParameter("X Gridline", "X",
            "The gridlines set out along X, one per X offset, each running in Y.",
            GH_ParamAccess.list);
        pManager.AddLineParameter("Y Gridline", "Y",
            "The gridlines set out along Y, one per Y offset, each running in X.",
            GH_ParamAccess.list);
        pManager.AddPointParameter("Node", "N",
            "The crossings, one branch per X gridline; item j of branch i is where X gridline i "
            + "meets Y gridline j.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Plane plane = Plane.WorldXY;
        var xSpacings = new List<double>();
        var ySpacings = new List<double>();
        double overhang = 0.0;

        if (!da.GetData(0, ref plane)) return;
        if (!da.GetDataList(1, xSpacings)) return;
        if (!da.GetDataList(2, ySpacings)) return;
        if (!da.GetData(3, ref overhang)) return;

        var options = new RectangularGridOptions
        {
            Plane = plane,
            XSpacings = xSpacings,
            YSpacings = ySpacings,
            Overhang = overhang,
            Tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01,
        };

        try
        {
            RectangularGrid grid = RectangularGridGenerator.Generate(options);

            da.SetDataList(0, grid.XGridlines);
            da.SetDataList(1, grid.YGridlines);
            da.SetDataTree(2, Trees.FromRows(grid.Rows));

            Message = $"{xSpacings.Count} x {ySpacings.Count} bays\n{grid.Nodes.Count} nodes";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
