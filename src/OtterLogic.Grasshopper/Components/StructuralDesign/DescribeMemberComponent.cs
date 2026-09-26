using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using OtterLogic.Core;
using OtterLogic.StructuralEngine;

namespace OtterLogic.Grasshopper.Components.StructuralDesign;

/// <summary>
/// A model's lines, surfaces and supports in; the engine's reading of it out, as
/// two tables — one row per element, one row per member — with the member, the
/// assembly, the level and the flow of every element beside them.
/// <para>
/// Adaptor only. Everything it reports is <see cref="ModelReading"/>, the same
/// reading the Structural Insight Engine clusters on, without the clustering:
/// here curves become end points, surfaces become boundary corners, and the
/// tables come back as trees with the members drawn as curves.
/// </para>
/// </summary>
public sealed class DescribeMemberComponent : GH_Component
{
    private const int LinesInput = 0;
    private const int SurfacesInput = 1;
    private const int SupportsInput = 2;
    private const int ToleranceInput = 3;
    private const int ChainInput = 4;

    public DescribeMemberComponent()
        : base("Describe Member", "Members",
               "Read a structural model the way the Structural Insight Engine does, and hand the reading "
               + "over as tables: one row per element and one row per member, with the member, the "
               + "assembly, the level and the share of the model's weight passing along every element.\n\n"
               + "Lines that carry straight on through their joints are one member; members triangulated "
               + "together in a plane are one assembly; every element's weight is drained to the supports, "
               + "which gives how much passes along each and, from the supports up, what rests on what. "
               + "Nothing is named and nothing depends on which way the model faces.\n\n"
               + "Element Features and Member Features wire straight into OtterCluster, OtterEmbed and "
               + "OtterTrain — this is the table to train a model on when the answer wanted is per "
               + "member: a release, a section group, a role. For the natural groups already found, use "
               + "the Structural Insight Engine; for what will stop an analysis, use Geometry QA.",
               Categories.Root, Categories.StructuralDesign)
    {
    }

    public override Guid ComponentGuid => new("b82184d3-14b2-452c-85e9-eccb1c73ac8f");

    // The second step: the detail behind the engine's answer, for whoever wants the tables.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords => new[] { "member", "features", "table", "level", "assembly", "load path", "flow" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("describemember", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Lines", "L",
            "Line elements — members, bars, braces. Only the end points are read, so a curve is read as the "
            + "straight element between its ends: explode a polyline to read each segment.",
            GH_ParamAccess.list);

        pManager.AddGeometryParameter("Surfaces", "S",
            "Surface elements — Breps, surfaces or meshes. Every Brep face and mesh face is one element, "
            + "numbered after the lines.",
            GH_ParamAccess.list);

        pManager.AddPointParameter("Supports", "Su",
            "Supported points. Without them support distances, load paths and levels are skipped and read -1.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Tolerance", "T",
            "Points closer than this are the same point. The document's absolute tolerance by default. Points "
            + "within ten times this are read as meant to meet, and joined.",
            GH_ParamAccess.item);

        pManager.AddBooleanParameter("Chain Members", "Ch",
            "Read lines that carry straight on through a joint as one member. On by default; off reads "
            + "every line as its own member, for a model drawn with deliberate breaks that ought to stay breaks.",
            GH_ParamAccess.item, true);

        for (int i = LinesInput; i <= ChainInput; i++)
            pManager[i].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Element Features", "EF",
            "One branch per element — lines in the order they came in, then every surface face — its "
            + "readings in model units, named by Element Feature Names. Support Distance and Level are -1 "
            + "where there is no route to a support.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Element Feature Names", "EN", "What each value of an Element Features branch is.", GH_ParamAccess.list);

        pManager.AddNumberParameter("Member Features", "MF",
            "One branch per member: what it is like as a whole, what frames into it and what it frames "
            + "into, its place in the load path and in its assembly, named by Member Feature Names. "
            + "Nothing here says where a member is in plan, so the same member recurring across the "
            + "model gets the same row.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Member Feature Names", "MN", "What each value of a Member Features branch is.", GH_ParamAccess.list);

        pManager.AddIntegerParameter("Member", "M",
            "Per element, the member it is a piece of. Members are numbered by their lowest element, and the "
            + "number is the branch of Member Features and the item of Member Curves.",
            GH_ParamAccess.list);

        pManager.AddCurveParameter("Member Curves", "MC",
            "One curve per member: the polyline through its joints in order — a whole chord in one piece, "
            + "a surface's boundary — for colouring, labelling or baking by member.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Assembly", "As",
            "Per element, the assembly it is part of: members triangulated together in one plane are one "
            + "assembly, and any other member is an assembly by itself.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Level", "Lv",
            "Per element, how many hand-overs stand between it and the ground: 0 rests on the supports, 1 "
            + "on something that does, and so on. -1 without supports, or with no route to one.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Flow", "Fw",
            "Per element, the share of the whole model's weight passing along it on its way to the supports, 0 to 1.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Orientation", "O",
            "Per line, how it stands — Level, Pitched or Plumb — read from the model's own spread of "
            + "inclinations rather than fixed angles.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Report", "Rp",
            "How the model was read: joints, members, assemblies, levels, and anything worth knowing.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? new ModelReadingOptions().Tolerance;
        da.GetData(ToleranceInput, ref tolerance);

        bool chain = true;
        da.GetData(ChainInput, ref chain);

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
        if (!SurfaceInput.TryRead(this, surfaceItems, out var boundaries, out _))
            return;

        var supportPoints = new List<Point3d>();
        da.GetDataList(SupportsInput, supportPoints);

        if (curves.Count + boundaries.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire in Lines, Surfaces, or both.");
            return;
        }

        ModelReading reading;
        try
        {
            reading = ModelReading.Read(
                curves.Count > 0 ? SurfaceInput.Rows(curves.Select(c => c.PointAtStart).ToList()) : null,
                curves.Count > 0 ? SurfaceInput.Rows(curves.Select(c => c.PointAtEnd).ToList()) : null,
                boundaries,
                supportPoints.Count > 0 ? SurfaceInput.Rows(supportPoints) : null,
                new ModelReadingOptions { Tolerance = tolerance, ChainMembers = chain });
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        foreach (string note in reading.Notes)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);

        var memberCurves = new List<Curve>(reading.MemberCount);
        for (int m = 0; m < reading.MemberCount; m++)
        {
            var run = reading.Members.Run[m];
            var points = run.Select(j => new Point3d(reading.Structure.Joints[j].X, reading.Structure.Joints[j].Y, reading.Structure.Joints[j].Z)).ToList();
            if (reading.Members.Closed[m] && points.Count > 1)
                points.Add(points[0]);
            memberCurves.Add(new PolylineCurve(points));
        }

        var report = new List<string>
        {
            $"{reading.ElementCount} elements ({reading.LineCount} lines, {reading.SurfaceCount} surface faces) on "
            + $"{reading.Structure.Joints.Length} joints, read as {reading.MemberCount} members in "
            + $"{reading.Assemblies.Count} assemblies ({reading.Assemblies.Members.Count(a => a.Length > 1)} triangulated).",
            reading.Paths.Traced
                ? $"Load paths traced to {reading.Structure.Supported.Count(s => s)} supported joints; {reading.Paths.Levels + 1} levels."
                : "No load path traced.",
        };
        report.AddRange(reading.Notes);

        da.SetDataTree(0, Trees.FromRows(reading.ElementFeatures()));
        da.SetDataList(1, ModelReading.ElementFeatureNames);
        da.SetDataTree(2, Trees.FromRows(reading.MemberFeatures()));
        da.SetDataList(3, ModelReading.MemberFeatureNames);
        da.SetDataList(4, reading.Members.Of);
        da.SetDataList(5, memberCurves);
        da.SetDataList(6, reading.Assembly());
        da.SetDataList(7, reading.Level());
        da.SetDataList(8, reading.Paths.ElementFlow);
        da.SetDataList(9, reading.Orientation.Select(o => Naming.Humanise(o)));
        da.SetDataList(10, report);

        Message = reading.Paths.Traced
            ? $"{reading.MemberCount} members\n{reading.Paths.Levels + 1} levels"
            : $"{reading.MemberCount} members";
    }
}
