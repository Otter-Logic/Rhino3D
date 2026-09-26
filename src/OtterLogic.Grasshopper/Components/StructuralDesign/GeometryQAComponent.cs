using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using OtterLogic.StructuralDesign;

namespace OtterLogic.Grasshopper.Components.StructuralDesign;

/// <summary>
/// A model's lines and surfaces in; the connectivity problems that will stop an
/// analysis, or give answers nobody meant, out — each with where to look.
/// <para>
/// Adapter only. Everything it reports is decided by <see cref="GeometryQA.Check"/>;
/// here curves become end points, surfaces become boundary corners, and each issue
/// is handed back with the geometry it involves.
/// </para>
/// <para>
/// Counts, Elements and Element Issue Count came off on 2026-09-26: a count is the
/// length of a Findings branch, the elements are what Geometry holds, and the
/// per-element count is whether Element Issues is empty.
/// </para>
/// </summary>
public sealed class GeometryQAComponent : GH_Component
{
    private const int LinesInput = 0;
    private const int SurfacesInput = 1;
    private const int SupportsInput = 2;
    private const int ToleranceInput = 3;

    /// <summary>Kinds that stop a solver outright, rather than letting it run to a wrong answer.</summary>
    private static readonly GeometryIssueKind[] Blocking =
    {
        GeometryIssueKind.SeparatePart, GeometryIssueKind.Unsupported, GeometryIssueKind.StrandedSupport,
        GeometryIssueKind.NearMiss, GeometryIssueKind.UnnodedBearing,
    };

    public GeometryQAComponent()
        : base("Geometry QA", "GeoQA",
               "A first-pass health check before analysis: find where a model's connectivity will stop a solver, "
               + "or give answers nobody meant — ends that miss by millimetres, members resting on others with no "
               + "node, parts floating free or hanging by a thread, lines crossing unjoined, duplicates, overlaps, "
               + "slivers, and members slightly off their level or gridline.\n\n"
               + "Read strictly, as a solver reads it: points join only within the tolerance. Everything else is "
               + "judged against the model's own habits rather than a rulebook — what counts as a gap, a sliver or "
               + "a weak attachment is read from the model, so the check means the same thing in millimetres and "
               + "metres, on a frame or a gridshell. It reports; it does not fix, and it cannot know intent: a "
               + "deliberate movement joint is reported like an accidental gap.",
               Categories.Root, Categories.StructuralDesign)
    {
    }

    public override Guid ComponentGuid => new("2c07ce03-c715-4473-900f-a1c63aa001c9");

    // The first step, beside the engine: geometry in, what is wrong with it out.
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("geometryqa", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Lines", "L",
            "Line elements — members, bars, braces. Only the end points are read, so a curve is read as the "
            + "straight element between its ends.",
            GH_ParamAccess.list);

        pManager.AddGeometryParameter("Surfaces", "S",
            "Surface elements — Breps, surfaces or meshes. Every Brep face and mesh face is one element, as an "
            + "analysis mesh would be, so a panel whose corner lands mid-way along a neighbour's edge is found.",
            GH_ParamAccess.list);

        pManager.AddPointParameter("Supports", "Su",
            "Optional. Supported points. With them, parts with no support and supports at no node are reported.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Tolerance", "T",
            "Points closer than this are one node, as a solver will join them — the document's absolute tolerance "
            + "by default. The only distance given; everything else is read from the model.",
            GH_ParamAccess.item);

        for (int i = LinesInput; i <= ToleranceInput; i++)
            pManager[i].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Report", "Rp",
            "The whole check in words: every issue type found, most serious first, how many of each, and each "
            + "finding. Read it in a panel.",
            GH_ParamAccess.item);

        pManager.AddTextParameter("Issue Types", "T",
            "Every type of issue found, most serious first. Item k here is branch {k} of Geometry, Locations and "
            + "Findings — wire a slider into the Path of a Tree Branch on Geometry to step through the types, or "
            + "Explode Tree to give each type its own preview colour.",
            GH_ParamAccess.list);

        pManager.AddGeometryParameter("Geometry", "G",
            "One branch per issue type: every line and surface face involved, each once. Wire into a Custom Preview "
            + "to see all the elements with that problem at once.",
            GH_ParamAccess.tree);

        pManager.AddPointParameter("Locations", "L",
            "One branch per issue type: where each finding is, one point per finding.", GH_ParamAccess.tree);

        pManager.AddTextParameter("Findings", "F",
            "One branch per issue type: each finding in words, matching Locations item for item.", GH_ParamAccess.tree);

        pManager.AddTextParameter("Element Issues", "EI",
            "Per element, in the order the elements came in — lines first, then surface faces: which types of issue "
            + "it has, such as \"near miss; off level\". Empty for a clean element — colour the model by it.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? new GeometryQAOptions().Tolerance;
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

        int curved = curves.Count(c => !c.IsLinear(Math.Max(tolerance, 1e-9)));
        if (curved > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{curved} line(s) are not straight and are read as the straight element between their ends.");

        var surfaceItems = new List<IGH_GeometricGoo>();
        da.GetDataList(SurfacesInput, surfaceItems);
        if (!SurfaceInput.TryRead(this, surfaceItems, out var boundaries, out var faces))
            return;

        var supportPoints = new List<Point3d>();
        da.GetDataList(SupportsInput, supportPoints);

        if (curves.Count + boundaries.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire in Lines, Surfaces, or both.");
            return;
        }

        GeometryQAResult result;
        try
        {
            result = GeometryQA.Check(
                curves.Count > 0 ? SurfaceInput.Rows(curves.Select(c => c.PointAtStart).ToList()) : null,
                curves.Count > 0 ? SurfaceInput.Rows(curves.Select(c => c.PointAtEnd).ToList()) : null,
                boundaries,
                supportPoints.Count > 0 ? SurfaceInput.Rows(supportPoints) : null,
                new GeometryQAOptions { Tolerance = tolerance });
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        int blocking = result.Issues.Count(i => Blocking.Contains(i.Kind));
        if (blocking > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{blocking} issue(s) likely to stop an analysis — separate parts, near misses, ends with no node, "
                + "missing supports. See Report.");
        else if (result.Issues.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{result.Issues.Count} issue(s) worth a look, none likely to stop an analysis. See Report.");

        foreach (string note in result.Notes)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);

        // One branch per issue type, the same branch number in every tree, so item k of
        // Issue Types and branch {k} of Geometry, Locations and Findings all describe
        // the same type.
        var groups = result.Groups();
        var geometry = new DataTree<GeometryBase>();
        var locations = new DataTree<Point3d>();
        var findings = new DataTree<string>();

        for (int k = 0; k < groups.Count; k++)
        {
            var path = new GH_Path(k);
            foreach (int e in groups[k].Elements)
                geometry.Add(e < result.LineCount ? curves[e] : faces[e - result.LineCount], path);

            foreach (var issue in groups[k].Issues)
            {
                locations.Add(new Point3d(issue.Location[0], issue.Location[1], issue.Location[2]), path);
                findings.Add(issue.Message, path);
            }

            // A kind whose findings involve no element — a support at no node — still
            // gets its branch, so branch k stays type k.
            geometry.EnsurePath(path);
        }

        da.SetData(0, result.Summary());
        da.SetDataList(1, groups.Select(g => g.Name));
        da.SetDataTree(2, geometry);
        da.SetDataTree(3, locations);
        da.SetDataTree(4, findings);
        da.SetDataList(5, result.IssueLabels());

        Message = result.Issues.Count == 0 ? "clean" : $"{result.Issues.Count} issues\n{groups.Count} type(s)";
    }
}
