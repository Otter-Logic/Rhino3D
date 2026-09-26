using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using OtterLogic.StructuralForm;
using Rhino;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.StructuralForm;

/// <summary>
/// Sets out a radial structural grid from its ring spacings, a sweep and a
/// bay count.
/// <para>
/// Adapter only. The rays, rings and nodes all belong to
/// <see cref="RadialGridGenerator"/>, which the OtterRadialGrid Rhino command
/// calls in exactly the same way.
/// </para>
/// </summary>
public sealed class RadialGridComponent : GH_Component
{
    public RadialGridComponent()
        : base("Radial Grid", "RadGrid",
               "Set out a radial structural grid: rays outward from a centre or from the hole in "
               + "the middle, rings about it, and a node wherever a ray meets a ring.\n\n"
               + "Inner U and Inner V are half the hole's width and depth along the plane's X and "
               + "Y: both zero runs the rays into the centre, which is then one node; equal is a "
               + "round hole; different is an oval one, the stadium, with every ring an oval. Ring "
               + "spacings are the bays measured outward from there. Sweep is how much of the way "
               + "round the grid covers, in degrees: 360 closes the rings and draws no second ray "
               + "on the first, 90 is a quarter. Bays is how many bays the sweep is cut into.\n\n"
               + "For a grid on straight gridlines, use Rectangular Grid; for a grid over a "
               + "surface, Surface Grid.",
               Categories.Root, Categories.StructuralForm)
    {
    }

    public override Guid ComponentGuid => new("9a908a04-60f9-4374-8465-ac342c3a33a9");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap? Icon => EmbeddedIcons.Load("radialgrid", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddPlaneParameter("Plane", "P",
            "The centre of the grid and the plane it lies in. Angles are measured from the "
            + "plane's X axis, anticlockwise about its Z.",
            GH_ParamAccess.item, Plane.WorldXY);

        int rings = pManager.AddNumberParameter("Ring Spacings", "R",
            "The bays measured outward along a ray, first to last, from the inner ring or the "
            + "centre. A ring at the end of each.",
            GH_ParamAccess.list);
        ((Param_Number)pManager[rings]).SetPersistentData(6.0, 6.0, 6.0);

        pManager.AddNumberParameter("Inner U", "U",
            "Half the hole's width, from the centre along the plane's X axis: where the inner "
            + "ring crosses that axis. Zero with Inner V zero runs the rays into the centre.",
            GH_ParamAccess.item, 0.0);

        pManager.AddNumberParameter("Inner V", "V",
            "Half the hole's depth, from the centre along the plane's Y axis. Equal to Inner U "
            + "is a round hole; different is an oval.",
            GH_ParamAccess.item, 0.0);

        pManager.AddNumberParameter("Sweep", "S",
            "How much of the way round the grid covers, in degrees, from the start angle "
            + "anticlockwise. 360 is the whole way; 90 is a quarter.",
            GH_ParamAccess.item, 360.0);

        pManager.AddIntegerParameter("Bays", "B",
            "How many bays the sweep is cut into. One more ray than bays, except on a full "
            + "sweep.",
            GH_ParamAccess.item, 12);

        pManager.AddNumberParameter("Start Angle", "A",
            "Where the first ray points, in degrees from the plane's X axis.",
            GH_ParamAccess.item, 0.0);

        pManager.AddNumberParameter("Overhang", "O",
            "How far every ray runs on past the outer ring. Rings are never extended.",
            GH_ParamAccess.item, 0.0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddLineParameter("Ray", "L",
            "One line per ray, in angle order, from the inner ring (or the centre) to the outer "
            + "ring plus the overhang.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Ring", "C",
            "One curve per ring, innermost first: arcs or circles round a round hole, ovals "
            + "round an oval one.",
            GH_ParamAccess.list);
        pManager.AddPointParameter("Node", "N",
            "The nodes, one branch per ray. When the rays meet at the centre, item 0 of every "
            + "branch is the centre, so item k + 1 is always ring k.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Plane plane = Plane.WorldXY;
        var ringSpacings = new List<double>();
        double innerU = 0.0;
        double innerV = 0.0;
        double sweep = 360.0;
        int bays = 12;
        double startAngle = 0.0;
        double overhang = 0.0;

        if (!da.GetData(0, ref plane)) return;
        if (!da.GetDataList(1, ringSpacings)) return;
        if (!da.GetData(2, ref innerU)) return;
        if (!da.GetData(3, ref innerV)) return;
        if (!da.GetData(4, ref sweep)) return;
        if (!da.GetData(5, ref bays)) return;
        if (!da.GetData(6, ref startAngle)) return;
        if (!da.GetData(7, ref overhang)) return;

        var options = new RadialGridOptions
        {
            Plane = plane,
            RingSpacings = ringSpacings,
            InnerU = innerU,
            InnerV = innerV,
            Sweep = sweep,
            StartAngle = startAngle,
            Bays = bays,
            Overhang = overhang,
            Tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01,
        };

        try
        {
            RadialGrid grid = RadialGridGenerator.Generate(options);

            foreach (FormNote note in grid.Notes)
                AddRuntimeMessage(
                    note.Level == FormNoteLevel.Warning
                        ? GH_RuntimeMessageLevel.Warning
                        : GH_RuntimeMessageLevel.Remark,
                    note.Message);

            da.SetDataList(0, grid.Rays);
            da.SetDataList(1, grid.Rings);
            da.SetDataTree(2, Trees.FromRows(grid.Rows));

            Message = $"{bays} bays x {grid.Rings.Count} rings\n{grid.Nodes.Count} nodes";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
