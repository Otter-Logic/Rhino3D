using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using OtterLogic.Fabrication;

// Windows Forms has a Panel too, and this project enables it. The alias also steps
// round the namespace trap: inside Components.Fabrication, a bare "Fabrication"
// binds here rather than to the toolkit.
using FabricatedPanel = global::OtterLogic.Fabrication.Panel;

namespace OtterLogic.Grasshopper.Components.Fabrication;

/// <summary>
/// Surfaces and a machine's rectangle in; the panels to make and the types they
/// repeat, out — grouped per surface, per type.
/// <para>
/// Adapter only. The layout and the types belong to
/// <see cref="PanelTypology.Layout"/>; here surfaces become boundary corners and
/// panels come back as rectangles.
/// </para>
/// </summary>
public sealed class PanelTypologyComponent : GH_Component
{
    private const int SurfacesInput = 0;
    private const int LengthInput = 1;
    private const int WidthInput = 2;
    private const int GapInput = 3;
    private const int StandardisationInput = 4;
    private const int TypeToleranceInput = 5;
    private const int ToleranceInput = 6;

    public PanelTypologyComponent()
        : base("Panel Typology", "Panels",
               "Break surfaces into panels a machine can make, and find the types that repeat: the surface laid out "
               + "in rows of the machine's rectangle, cut where its edge runs through one, and every panel grouped "
               + "with the ones it is the same as.\n\n"
               + "Give the largest rectangle the machine can cast and the gap between panels. Each surface is laid "
               + "out in its own frame, so a panel's length runs along the surface's long edge however it is turned "
               + "in space. Nothing is fitted to the edge — a panel the edge crosses is simply cut there — so a "
               + "curve is followed as closely as it is drawn, a slope comes out as triangles, and the panels "
               + "together cover the whole surface but for the joints.\n\n"
               + "Standardisation is the judgement, and it is yours: hold to the machine's full panel and take a "
               + "sliver at the end of each row, or share the leftover out and take fewer, bespoke sizes "
               + "instead.\n\n"
               + "Type Tolerance is the rationalisation lever: no two panels of a type are further apart than it, "
               + "measured around their whole outlines, so raising it merges near-identical panels and tells you "
               + "what accepting that would save.",
               Categories.Root, Categories.Fabrication)
    {
    }

    public override Guid ComponentGuid => new("021c88c5-ab81-484d-a280-8a90d70baf93");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("paneltypology", 24);

    private static readonly PanelTypologyOptions Defaults = new();

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGeometryParameter("Surfaces", "S",
            "The surfaces to panelise — Breps, surfaces or meshes. Every Brep face and mesh face is one surface, "
            + "panelised on its own.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Panel Length", "L",
            "The longest panel the machine can make. Panels run this way along each surface's long axis.",
            GH_ParamAccess.item, 8000.0);

        pManager.AddNumberParameter("Panel Width", "W",
            "The widest panel the machine can make. Rows of this width are laid across each surface.",
            GH_ParamAccess.item, 3000.0);

        pManager.AddNumberParameter("Gap", "G",
            "The joint between one panel and the next. Panels sit flush to the edges of the surface.",
            GH_ParamAccess.item, 0.0);

        pManager.AddNumberParameter("Standardisation", "R",
            "How hard to hold to the machine's full panel, 0 to 1. At 1 a row is cut into as many full-size panels "
            + "as it will take and the leftover is left as it falls, however small: the most repeats, and slivers "
            + "at the edge. At 0 the leftover is shared out instead, so a row comes out as a few equal panels of no "
            + "particular size: fewer types, no slivers, nothing standard. Slide it to see the trade.",
            GH_ParamAccess.item, 1.0);

        pManager.AddNumberParameter("Type Tolerance", "T",
            "How far two panels may differ and still be the same type — the fabrication tolerance. Measured "
            + "around their whole outlines rather than on their length and width, so a notch or a raked corner "
            + "counts as a difference and two panels of a type really are the same panel. Raise it to see how "
            + "many types the job would need if that much standardising were accepted.",
            GH_ParamAccess.item, Defaults.TypeTolerance);

        pManager.AddNumberParameter("Tolerance", "t",
            "Points closer than this are the same point, and a surface flatter than this is flat. The document's "
            + "absolute tolerance by default.",
            GH_ParamAccess.item);

        pManager[StandardisationInput].Optional = true;
        pManager[TypeToleranceInput].Optional = true;
        pManager[ToleranceInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Panels", "P",
            "The panels as closed outlines, grouped {surface;type}: branch {2;0} is every panel of the first type on "
            + "the third surface. Four corners for a square-cut panel, and as many as the edge takes for one cut to "
            + "it. Wire into a Custom Preview, or Boundary Surfaces for faces.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Types", "T",
            "One name per type, the machine's full panel first then the most used: P1, P2 for square-cut panels, "
            + "T1, T2 for triangles, C1, C2 for anything else cut to the edge. Item k here is the second path "
            + "number of every Panels branch.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Type Length", "TL",
            "Each type's length, matching Types — the stock it is cut from, along the surface's long axis.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Type Width", "TW",
            "Each type's width, matching Types — the stock it is cut from, across.", GH_ParamAccess.list);

        pManager.AddTextParameter("Type Shape", "TS",
            "Each type's shape, matching Types: Rectangle for a square-cut panel, Triangle for a wedge, and Cut for "
            + "a panel the edge of the surface has been cut through some other way.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Counts", "N",
            "How many panels of each type over all the surfaces, matching Types.", GH_ParamAccess.list);

        pManager.AddIntegerParameter("Surface Counts", "SN",
            "One branch per surface: how many panels of each type it takes, matching Types.", GH_ParamAccess.tree);

        pManager.AddNumberParameter("Coverage", "C",
            "Per surface, the share of it covered by panels, 0 to 1. With no gap asked for this is 1: every part of "
            + "the surface is in some panel, and what is not covered is the joints.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Summary", "S",
            "The types with their sizes and counts, then what each surface is made of. Read it in a panel.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? Defaults.Tolerance;
        da.GetData(ToleranceInput, ref tolerance);

        var items = new List<IGH_GeometricGoo>();
        da.GetDataList(SurfacesInput, items);
        // The tolerance earns its keep here as nowhere else: panels are cut to the
        // outline, so a curved edge has to be read closely enough to cut to.
        if (!SurfaceInput.TryRead(this, items, out var boundaries, out _, tolerance))
            return;

        if (boundaries.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire the surfaces to panelise into Surfaces.");
            return;
        }

        double length = 8000.0, width = 3000.0, gap = 0.0;
        double standardisation = Defaults.Standardisation, typeTolerance = Defaults.TypeTolerance;
        if (!da.GetData(LengthInput, ref length)) return;
        if (!da.GetData(WidthInput, ref width)) return;
        if (!da.GetData(GapInput, ref gap)) return;
        if (!da.GetData(StandardisationInput, ref standardisation)) return;
        if (!da.GetData(TypeToleranceInput, ref typeTolerance)) return;

        PanelTypologyResult result;
        try
        {
            result = PanelTypology.Layout(boundaries, new PanelTypologyOptions
            {
                Length = length,
                Width = width,
                Gap = gap,
                Standardisation = standardisation,
                TypeTolerance = typeTolerance,
                Tolerance = tolerance,
            });
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        foreach (string note in result.Notes)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);

        // {surface;type}, so a branch is every panel of one type on one surface — and a
        // surface that never uses a type simply has no branch there.
        var panels = new DataTree<Curve>();
        for (int s = 0; s < result.SurfaceCount; s++)
            for (int t = 0; t < result.Types.Count; t++)
            {
                var path = new GH_Path(s, t);
                foreach (int p in result.Of(s, t))
                    panels.Add(Outline(result.Panels[p]), path);
            }

        var tally = result.Tally();
        var surfaceCounts = new DataTree<int>();
        for (int s = 0; s < result.SurfaceCount; s++)
            for (int t = 0; t < result.Types.Count; t++)
                surfaceCounts.Add(tally[s, t], new GH_Path(s));

        da.SetDataTree(0, panels);
        da.SetDataList(1, result.Types.Select(t => t.Name));
        da.SetDataList(2, result.Types.Select(t => t.Length));
        da.SetDataList(3, result.Types.Select(t => t.Width));
        da.SetDataList(4, result.Types.Select(t => t.Shape.ToString()));
        da.SetDataList(5, result.Types.Select(t => t.Count));
        da.SetDataTree(6, surfaceCounts);
        da.SetDataList(7, result.Coverage());
        da.SetData(8, result.Summary());

        int cut = result.Panels.Count(p => p.Shape != PanelShape.Rectangle);
        Message = $"{result.Panels.Count} panels\n{result.Types.Count} types, {result.Standard} full, {cut} cut";
    }

    /// <summary>A panel's corners as a closed outline — four of them, or as many as the edge took.</summary>
    private static Curve Outline(FabricatedPanel panel)
    {
        var corners = Enumerable.Range(0, panel.Corners.GetLength(0))
            .Select(c => new Point3d(panel.Corners[c, 0], panel.Corners[c, 1], panel.Corners[c, 2])).ToList();
        corners.Add(corners[0]);
        return new PolylineCurve(corners);
    }
}
