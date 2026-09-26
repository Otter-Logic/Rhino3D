using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using OtterLogic.Grasshopper.Parameters.Graphs;
using OtterLogic.Grasshopper.Parameters.StructuralDesign;
using OtterLogic.Grasshopper.Types;
using OtterLogic.StructuralDesign;

namespace OtterLogic.Grasshopper.Components.StructuralDesign;

/// <summary>
/// A structure's lines, surfaces and supports in; its natural groups, the level each
/// sits at between the supports and the top of the load path, the features and graph
/// they came from, and what is worth a look, out.
/// <para>
/// Adapter only. Everything it reports is decided by
/// <see cref="StructuralInsightEngine.Analyse"/>; here curves become end points,
/// Brep faces and mesh faces become boundary corners, and the answer is packed back
/// with the original geometry.
/// </para>
/// <para>
/// Cut down on 2026-09-26 from eleven inputs and twenty-two outputs to six and
/// eleven. The outputs it lost said the grouping five ways, showed how each view
/// voted, and repeated what Describe Member and Geometry QA already report; the
/// inputs it lost were tuning, and now arrive on the Settings wire. Every number
/// dropped from an output is still in Report.
/// </para>
/// </summary>
public sealed class StructuralInsightEngineComponent : GH_Component
{
    private const int LinesInput = 0;
    private const int SurfacesInput = 1;
    private const int SupportsInput = 2;
    private const int ToleranceInput = 3;
    private const int GroupsInput = 4;
    private const int SettingsInput = 5;

    public StructuralInsightEngineComponent()
        : base("Structural Insight Engine", "Insight",
               "Discover the structural intent hidden in a model's geometry: the natural groups its elements fall "
               + "into, what rests on what from the supports up, and the places it is not joined the way it looks "
               + "meant to be.\n\n"
               + "Lines, surfaces and supports in — nothing about what kind of structure it is. Frames, shells, "
               + "bridges, stadium bowls, gridshells and parametric forms all go through the same engine, which "
               + "reads the model the way it is read by eye: lines that carry straight on through their joints are "
               + "one member, members triangulated together in a plane are one assembly, and every element's weight "
               + "is drained to the supports, which gives what rests on what — Level 0 rests on the supports, Level 1 "
               + "on that, and so on. Four unsupervised views of the members are then fused into the groups they "
               + "agree on.\n\n"
               + "Nothing is named, and nothing depends on which way the model faces: gravity is the only direction "
               + "used. Groups tend to be columns, chords, webs, primary and secondary framing, bracing, shell zones "
               + "and repeated modules — reading which is which is yours, downstream. Features wire straight into "
               + "OtterCluster, OtterTrain and Write Dataset; Graph into the Graphs panel.\n\n"
               + "The defaults are meant to be left alone. To tune the views, wire in Insight Settings. For the "
               + "member, assembly and flow of every element as tables, use Describe Member; for what will stop an "
               + "analysis, use Geometry QA.",
               Categories.Root, Categories.StructuralDesign)
    {
    }

    public override Guid ComponentGuid => new("d41c7b93-58e2-4a06-b7f4-2e9a6c18d0b5");

    // The first step: geometry in, the reading out.
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords => new[] { "insight", "groups", "clustering", "hierarchy", "level", "load path" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("structuralinsight", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Lines", "L",
            "Line elements — members, bars, cables. Only the end points are read, so a curve is read as the straight "
            + "element between its ends: explode a polyline to read each segment.",
            GH_ParamAccess.list);

        pManager.AddGeometryParameter("Surfaces", "S",
            "Surface elements — Breps, surfaces or meshes. Every Brep face and every mesh face is one element, so a "
            + "shell meshed into panels is read panel by panel, which is what lets zones of it be found.",
            GH_ParamAccess.list);

        pManager.AddPointParameter("Supports", "Su",
            "Supported points. Without them no load path is traced: Level is -1 throughout, Hierarchy has one "
            + "level, and the no-path check is skipped.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Tolerance", "T",
            "Points closer than this are the same point. The document's absolute tolerance by default. Points within "
            + "ten times this are read as meant to meet, joined, and reported as near misses.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Groups", "G",
            "A fixed number of natural groups. Zero, the default, lets the views' agreement decide.",
            GH_ParamAccess.item, 0);

        pManager.AddParameter(new InsightSettingsParameter(), "Settings", "St",
            "Optional. Tuning from an Insight Settings component: how many groups to consider, how each view votes, "
            + "how lines are chained into members. Unwired, every setting is at its default.",
            GH_ParamAccess.item);

        for (int i = LinesInput; i <= SettingsInput; i++)
            pManager[i].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Group", "G",
            "The natural group of every element: the lines in the order they came in, then every surface face. "
            + "Groups are numbered largest first.",
            GH_ParamAccess.list);

        pManager.AddGeometryParameter("Grouped Geometry", "GG",
            "The lines and surface faces sorted into their groups, one branch per group, numbered like Group — "
            + "wire into a Custom Preview to see the groups, or Bake By Group to put them on layers.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Level", "Lv",
            "Per element, how many hand-overs stand between it and the ground: 0 rests on the supports, 1 rests on "
            + "something that does, and so on up. Members triangulated together share a level, and so do members "
            + "that lean on each other. -1 without supports, or with no route to one.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Hierarchy", "H",
            "Element indices in branches {level; group}: each level's elements sorted into their natural groups, "
            + "the way a member schedule is laid out — lines first, then faces.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Confidence", "C",
            "Per element, how far the views agreed about the elements it belongs with, 0 to 1. Low is the element "
            + "to look at, and it is listed under Issue Reasons.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Features", "F",
            "One branch per element, its raw features in model units, named by Feature Names — ready to wire into "
            + "OtterCluster's Data, OtterTrain's Inputs or Write Dataset. Support Distance and Level are -1 where "
            + "there is no route to a support.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Feature Names", "FN", "What each value of a Features branch is.", GH_ParamAccess.list);

        pManager.AddParameter(new GraphParameter(), "Graph", "Gr",
            "The element graph: a node per element at its centroid, a connection where two elements meet. Wire it "
            + "into OtterPath or Deconstruct Graph.",
            GH_ParamAccess.item);

        pManager.AddPointParameter("Issue Points", "IP",
            "Where to look for each thing worth a look: a duplicate, a free end, an outlier, an element the views "
            + "disagree about. Item for item with Issue Reasons.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Issue Reasons", "IR",
            "What was found at each Issue Point, naming the element it is about.", GH_ParamAccess.list);

        pManager.AddTextParameter("Report", "Rp",
            "How the model was read, how the views voted, what each group is like, the level and group of every "
            + "branch of Hierarchy, and every issue by kind.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? new StructuralInsightOptions().Tolerance;
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

        int groups = 0;
        da.GetData(GroupsInput, ref groups);

        GH_InsightSettings? wired = null;
        var settings = da.GetData(SettingsInput, ref wired) && wired?.Value is not null
            ? wired.Value
            : new StructuralInsightOptions();

        if (curves.Count + boundaries.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire in Lines, Surfaces, or both.");
            return;
        }

        StructuralInsightResult result;
        try
        {
            result = StructuralInsightEngine.Analyse(
                curves.Count > 0 ? SurfaceInput.Rows(curves.Select(c => c.PointAtStart).ToList()) : null,
                curves.Count > 0 ? SurfaceInput.Rows(curves.Select(c => c.PointAtEnd).ToList()) : null,
                boundaries,
                supportPoints.Count > 0 ? SurfaceInput.Rows(supportPoints) : null,
                settings with { Tolerance = tolerance, Groups = groups > 0 ? groups : null });
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var members = result.Members();
        var grouped = new DataTree<GeometryBase>();
        for (int g = 0; g < members.Length; g++)
        {
            var path = new GH_Path(g);
            grouped.EnsurePath(path);
            foreach (int e in members[g])
                grouped.Add(e < result.LineCount ? curves[e] : faces[e - result.LineCount], path);
        }

        var hierarchy = new DataTree<int>();
        foreach (var (level, group, elements) in result.Hierarchy())
            hierarchy.AddRange(elements, new GH_Path(level, group));

        // Node positions are the element centroids, the first three feature columns,
        // so the graph draws itself over the model and routes come back as polylines.
        var centroids = new Point3d[result.ElementCount];
        for (int e = 0; e < centroids.Length; e++)
            centroids[e] = new Point3d(result.Features[e, 0], result.Features[e, 1], result.Features[e, 2]);

        if (result.ComponentCount > 1)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"The model is not one connected structure: {result.ComponentCount} separate pieces. See Issue Points, or run Geometry QA.");
        if (!result.HasSupports)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "No supports wired, so no load path was traced: Level is -1 throughout and Hierarchy has one level.");
        if (result.Issues.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{result.Issues.Count} issue(s) worth a look — see Issue Points and Report.");
        foreach (string note in result.Notes)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);

        da.SetDataList(0, result.Labels);
        da.SetDataTree(1, grouped);
        da.SetDataList(2, result.Level);
        da.SetDataTree(3, hierarchy);
        da.SetDataList(4, result.Agreement);
        da.SetDataTree(5, Trees.FromRows(result.Features));
        da.SetDataList(6, result.FeatureNames);
        da.SetData(7, new GH_Graph(new PlacedGraph(result.Connectivity, centroids)));
        da.SetDataList(8, result.Issues.Select(issue => new Point3d(issue.X, issue.Y, issue.Z)));
        da.SetDataList(9, result.Issues.Select(issue => issue.Reason));
        da.SetData(10, result.Report());

        Message = result.Levels >= 0
            ? $"{result.Groups} groups\n{result.Levels + 1} levels\n{result.Issues.Count} issues"
            : $"{result.Groups} groups\n{result.Issues.Count} issues";
    }
}
