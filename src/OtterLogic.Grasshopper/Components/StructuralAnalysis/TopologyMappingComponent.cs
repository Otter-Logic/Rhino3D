using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Rhino;
using Rhino.Geometry;
using OtterLogic.StructuralAnalysis;

namespace OtterLogic.Grasshopper.Components.StructuralAnalysis;

/// <summary>
/// A structural model's lines and supports in, before any analysis; the load
/// path hierarchy's categories, ranks and parts, each split further into the
/// likely initial section groups its own members' connectivity and geometry
/// support.
/// <para>
/// Adapter only. Everything it reports is decided by
/// <see cref="TopologyMapping.Analyse"/> — which itself calls
/// <see cref="LoadPathHierarchy.Analyse"/> for the categories, ranks and parts
/// — here the curves become end points and the answer is packed back onto the
/// canvas.
/// </para>
/// </summary>
public sealed class TopologyMappingComponent : GH_Component
{
    private const int LinesInput = 0;
    private const int SupportsInput = 1;
    private const int ConnectionsInput = 2;
    private const int ToleranceInput = 3;
    private const int MinimumGroupSizeInput = 4;
    private const int MaximumSectionGroupsInput = 5;

    public TopologyMappingComponent()
        : base("Topology Mapping", "Topology",
               "Automatically identify frames, trusses, bracing and secondary members from a structural "
               + "model's lines and supports alone, then group each role's members into likely initial "
               + "section sizes by connectivity and geometric alignment together.\n\n"
               + "The categories, ranks and parts are Load Path Hierarchy's own reading. What is new here is "
               + "the fourth level: within one role — every primary beam, say — members that touch and run the "
               + "same way are read as one likely section, rather than every member of a role being assumed "
               + "to share a size just because it shares a role.\n\n"
               + "Use Load Path Hierarchy instead when only the load path, outliers and releases are wanted.",
               Categories.Root, Categories.StructuralAnalysis)
    {
    }

    public override Guid ComponentGuid => new("6f2a4d18-3b7e-4c92-8e15-9a1d6f4b2c83");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("topologymapping", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Lines", "L",
            "Every member of the model as a line. Only the end points are read, so a curve is read as the "
            + "straight member between its ends.",
            GH_ParamAccess.list);

        pManager.AddPointParameter("Supports", "S",
            "The supported points. Leave it empty and the lowest joints of the model are taken as supported.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Connections", "C",
            "How the members are connected, which decides the releases. Simple construction by default. Plug "
            + "in Connection Style for a dropdown.",
            GH_ParamAccess.item, (int)ConnectionStyle.Simple);

        pManager.AddNumberParameter("Tolerance", "T",
            "Member ends closer than this are joined in the model. The document's absolute tolerance by "
            + "default.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Minimum Group Size", "MG",
            "Fewest members a role needs before section grouping is attempted on it. Below this, every "
            + "member of the role gets section group 0.",
            GH_ParamAccess.item, 4);

        pManager.AddIntegerParameter("Maximum Section Groups", "MS",
            "Most section groups considered within one role. The count actually used is read from the "
            + "model's own geometry, up to this ceiling.",
            GH_ParamAccess.item, 6);

        pManager[SupportsInput].Optional = true;
        pManager[ToleranceInput].Optional = true;
        pManager[MinimumGroupSizeInput].Optional = true;
        pManager[MaximumSectionGroupsInput].Optional = true;

        var connections = (Param_Integer)pManager[ConnectionsInput];
        foreach (var (label, value) in EnumChoices.Of<ConnectionStyle>())
            connections.AddNamedValue(label, value);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Groups", "G",
            "The lines sorted into the tree. Branch {category; rank; carrier; part; section group}: category 0 "
            + "column, 1 beam, 2 truss, 3 rafter, 4 brace, 5 strut, 6 hanger, 7 unsupported, 8 excluded; rank "
            + "and part as Load Path Hierarchy; carrier is the spanning member the group rests on, numbered "
            + "from 1 in tier order and 0 for columns and supports — see Group Names; section group is 0 for "
            + "the largest likely section within that role, ascending.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Group Names", "N",
            "The name of each line's group, grouped like Groups.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Indices", "I",
            "The original index of each line, grouped like Groups.",
            GH_ParamAccess.tree);

        pManager.AddBooleanParameter("Grouped", "GD",
            "Whether each line's role had enough members for section grouping to be attempted, rather than "
            + "landing in section group 0 for want of a population to read. In the order the lines came in.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Report", "!",
            "The hierarchy and its section groups, and what falls outside the load path.",
            GH_ParamAccess.item);

        // Appended after Report rather than beside Groups, so a definition
        // wired against the outputs above by position is not disturbed.
        pManager.AddBooleanParameter("Releases", "R",
            "One release pattern per branch of Groups — six values, Fx, Fy, Fz, Mx, My, Mz in the members' "
            + "local axes, true where released. Start and end are pooled together, since a real member's two "
            + "ends are almost always symmetric: this is whichever pattern most ends in the group actually "
            + "have. Ready to select the group's members and place manually. See Release Exceptions for any "
            + "end that disagrees with it.",
            GH_ParamAccess.tree);

        pManager.AddPointParameter("Release Exceptions", "RX",
            "Line ends whose actual release disagrees with their own group's pattern in Releases — empty for "
            + "an ordinary model, where a group's members genuinely share one release condition.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Release Exception Reasons", "RXR",
            "What differs from the group's own pattern at each point in Release Exceptions, item for item.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var curves = new List<Curve>();
        if (!da.GetDataList(LinesInput, curves))
            return;

        for (int i = 0; i < curves.Count; i++)
        {
            if (curves[i] is null || !curves[i].IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Line {i} is missing or invalid. Remove it, or every index after it shifts.");
                return;
            }
        }

        var supportPoints = new List<Point3d>();
        da.GetDataList(SupportsInput, supportPoints);

        int connections = (int)ConnectionStyle.Simple;
        if (!da.GetData(ConnectionsInput, ref connections))
            return;
        if (!Enum.IsDefined(typeof(ConnectionStyle), connections))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Connections must be one of "
                + string.Join(", ", EnumChoices.Of<ConnectionStyle>().Select(c => $"{c.Value} ({c.Label})")) + ".");
            return;
        }

        double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? new LoadPathHierarchyOptions().Tolerance;
        da.GetData(ToleranceInput, ref tolerance);

        int minimumGroupSize = 4;
        int maximumSectionGroups = 6;
        if (!da.GetData(MinimumGroupSizeInput, ref minimumGroupSize)) return;
        if (!da.GetData(MaximumSectionGroupsInput, ref maximumSectionGroups)) return;

        TopologyMappingResult result;
        try
        {
            result = TopologyMapping.Analyse(
                Rows(curves.Select(c => c.PointAtStart).ToList()), Rows(curves.Select(c => c.PointAtEnd).ToList()),
                supportPoints.Count > 0 ? Rows(supportPoints) : null,
                new TopologyMappingOptions
                {
                    Hierarchy = new LoadPathHierarchyOptions { Tolerance = tolerance, Connections = (ConnectionStyle)connections },
                    MinimumGroupSize = minimumGroupSize,
                    MaximumSectionGroups = maximumSectionGroups,
                });
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var groups = new DataTree<Curve>();
        var names = new DataTree<string>();
        var indices = new DataTree<int>();
        foreach (var group in result.Groups)
        {
            var path = new GH_Path((int)group.Category, group.Rank, group.Carrier, (int)group.Part, group.SectionGroup);
            foreach (int line in group.Lines)
            {
                groups.Add(curves[line], path);
                names.Add(group.Name, path);
                indices.Add(line, path);
            }
        }

        if (result.Hierarchy.SupportsInferred)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "No supports given, so the lowest joints were taken as supported. Wire in Supports if the "
                + "model stands on anything else.");

        int ungrouped = result.Grouped.Count(g => !g);
        if (ungrouped > 0 && ungrouped < result.LineCount)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{ungrouped} line(s) belong to a role with too few members to section-group: they sit alone "
                + "in section group 0. See Grouped.");

        var releases = new DataTree<bool>();
        foreach (var group in result.Groups)
            releases.AddRange(group.Release, new GH_Path((int)group.Category, group.Rank, group.Carrier, (int)group.Part, group.SectionGroup));

        if (result.ReleaseExceptions.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{result.ReleaseExceptions.Count} line end(s) have a release that disagrees with their own "
                + "group's pattern. See Release Exceptions.");

        da.SetDataTree(0, groups);
        da.SetDataTree(1, names);
        da.SetDataTree(2, indices);
        da.SetDataList(3, result.Grouped);
        da.SetData(4, result.Hierarchy.Report());
        da.SetDataTree(5, releases);
        da.SetDataList(6, result.ReleaseExceptions.Select(e => new Point3d(e.X, e.Y, e.Z)));
        da.SetDataList(7, result.ReleaseExceptions.Select(e => e.Reason));

        Message = $"{result.Groups.Count} groups";
    }

    private static double[,] Rows(IReadOnlyList<Point3d> points)
    {
        var rows = new double[points.Count, 3];
        for (int i = 0; i < points.Count; i++)
            (rows[i, 0], rows[i, 1], rows[i, 2]) = (points[i].X, points[i].Y, points[i].Z);

        return rows;
    }
}
