using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using OtterLogic.Core;
using OtterLogic.StructuralDesign;

namespace OtterLogic.Grasshopper.Components.StructuralDesign;

/// <summary>
/// A structure's lines, surfaces and supports in; its natural groups, the features
/// and graph they came from, and everything worth a look, out.
/// <para>
/// Adapter only. Everything it reports is decided by
/// <see cref="StructuralInsightEngine.Analyse"/>; here curves become end points,
/// Brep faces and mesh faces become boundary corners, and the answer is packed back
/// with the original geometry.
/// </para>
/// </summary>
public sealed class StructuralInsightEngineComponent : GH_Component
{
    private const int LinesInput = 0;
    private const int SurfacesInput = 1;
    private const int SupportsInput = 2;
    private const int ToleranceInput = 3;
    private const int GroupsInput = 4;
    private const int MaximumGroupsInput = 5;
    private const int MinimumGroupSizeInput = 6;
    private const int ConnectivityWeightInput = 7;
    private const int GeometryWeightInput = 8;
    private const int DensityWeightInput = 9;

    /// <summary>Pieces a curved face edge is read as, so a curved panel's boundary is followed rather than cut across.</summary>
    private const int CurvedEdgePieces = 4;

    public StructuralInsightEngineComponent()
        : base("Structural Insight Engine", "Insight",
               "Discover the structural intent hidden in a model's geometry: the natural groups its elements fall "
               + "into, and the places it is not joined the way it looks meant to be.\n\n"
               + "Lines, surfaces and supports in — nothing about what kind of structure it is. Frames, shells, "
               + "bridges, stadium bowls, gridshells and parametric forms all go through the same engine: the "
               + "geometry becomes a graph of which elements meet, every element gets features (size, extent along "
               + "each axis, connections, distance to a support, centrality, aspect ratio), and three unsupervised "
               + "views — spectral clustering of the graph, hierarchical clustering of the features, HDBSCAN for "
               + "outliers — are fused into the groups they agree on.\n\n"
               + "Nothing is named. Groups tend to be primary and secondary framing, bracing systems, diaphragm and "
               + "shell zones, stiff and flexible regions, repeated modules and load-path communities — reading "
               + "which is which is yours, downstream. Features and Connectivity plug straight into the Unsupervised "
               + "Learning components to take it further.",
               Categories.Root, Categories.StructuralDesign)
    {
    }

    public override Guid ComponentGuid => new("d41c7b93-58e2-4a06-b7f4-2e9a6c18d0b5");

    public override GH_Exposure Exposure => GH_Exposure.primary;

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
            "Supported points. Without them the support distance and no-path checks are skipped, and every "
            + "unsupported base reads as a free end.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Tolerance", "T",
            "Points closer than this are the same point. The document's absolute tolerance by default. Points within "
            + "ten times this are read as meant to meet, joined, and reported as near misses.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Groups", "G",
            "A fixed number of natural groups. Zero, the default, lets the views' agreement decide.",
            GH_ParamAccess.item, 0);

        pManager.AddIntegerParameter("Maximum Groups", "MaxG", "Most groups each view and the fusion consider.",
            GH_ParamAccess.item, 10);

        pManager.AddIntegerParameter("Minimum Group Size", "MinS",
            "Groups smaller than this merge into the connected group their elements agree with most. One merges nothing.",
            GH_ParamAccess.item, 1);

        pManager.AddNumberParameter("Connectivity Weight", "Wc",
            "Vote of the connectivity view — groups of connected elements that are alike. Zero skips it.",
            GH_ParamAccess.item, 1.0);

        pManager.AddNumberParameter("Geometry Weight", "Wg",
            "Vote of the geometry view — elements alike wherever they are, so repeated elements group. Zero skips it.",
            GH_ParamAccess.item, 1.0);

        pManager.AddNumberParameter("Density Weight", "Wd",
            "Vote of the density view — dense groups, and outliers outside them. Zero skips it and its outlier flags.",
            GH_ParamAccess.item, 1.0);

        for (int i = LinesInput; i <= DensityWeightInput; i++)
            pManager[i].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Group", "G",
            "The natural group of every element: the lines in the order they came in, then every surface face.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Indices", "I",
            "One branch per natural group, largest first, holding its elements' indices — lines first, then faces.",
            GH_ParamAccess.tree);

        pManager.AddCurveParameter("Line Groups", "LG",
            "The line elements sorted into their groups, one branch per group, numbered like Indices.",
            GH_ParamAccess.tree);

        pManager.AddGeometryParameter("Surface Groups", "SG",
            "The surface faces sorted into their groups, one branch per group, numbered like Indices.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Agreement", "A",
            "Per element, how far the three views agreed about the elements it belongs with, 0 to 1. Low is the "
            + "element to look at.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Connectivity Groups", "CG",
            "The connectivity view's own group per element, or -1 where it did not run.", GH_ParamAccess.list);

        pManager.AddIntegerParameter("Geometry Groups", "GeG",
            "The geometry view's own group per element, or -1 where it did not run.", GH_ParamAccess.list);

        pManager.AddIntegerParameter("Density Groups", "DG",
            "The density view's own group per element, or -1 for an outlier.", GH_ParamAccess.list);

        pManager.AddNumberParameter("Features", "F",
            "One branch per element, its raw features in model units, named by Feature Names — ready to wire into "
            + "Training Inputs of any Unsupervised Learning component. Support Distance is -1 where there is no route.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Feature Names", "FN", "What each value of a Features branch is.", GH_ParamAccess.list);

        pManager.AddIntegerParameter("Connectivity", "C",
            "The element graph: one branch per element listing the elements it meets — the Connectivity input of the "
            + "graph components.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Issue Elements", "IE", "The element each issue is about.", GH_ParamAccess.list);

        pManager.AddPointParameter("Issue Points", "IP", "Where to look for each issue.", GH_ParamAccess.list);

        pManager.AddTextParameter("Issue Reasons", "IR", "What was found, item for item with Issue Points.", GH_ParamAccess.list);

        pManager.AddTextParameter("Flags", "Fl",
            "Per element, everything flagged at it, comma separated; empty where nothing was.", GH_ParamAccess.list);

        pManager.AddTextParameter("Report", "!",
            "How the model was read, how the views voted, what each group is like, and what is worth a look.",
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
        if (!TryReadSurfaces(surfaceItems, out var boundaries, out var faces))
            return;

        var supportPoints = new List<Point3d>();
        da.GetDataList(SupportsInput, supportPoints);

        int groups = 0, maximumGroups = 10, minimumGroupSize = 1;
        double connectivityWeight = 1.0, geometryWeight = 1.0, densityWeight = 1.0;
        da.GetData(GroupsInput, ref groups);
        da.GetData(MaximumGroupsInput, ref maximumGroups);
        da.GetData(MinimumGroupSizeInput, ref minimumGroupSize);
        da.GetData(ConnectivityWeightInput, ref connectivityWeight);
        da.GetData(GeometryWeightInput, ref geometryWeight);
        da.GetData(DensityWeightInput, ref densityWeight);

        if (curves.Count + boundaries.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire in Lines, Surfaces, or both.");
            return;
        }

        StructuralInsightResult result;
        try
        {
            result = StructuralInsightEngine.Analyse(
                curves.Count > 0 ? Rows(curves.Select(c => c.PointAtStart).ToList()) : null,
                curves.Count > 0 ? Rows(curves.Select(c => c.PointAtEnd).ToList()) : null,
                boundaries,
                supportPoints.Count > 0 ? Rows(supportPoints) : null,
                new StructuralInsightOptions
                {
                    Tolerance = tolerance,
                    Groups = groups > 0 ? groups : null,
                    MaximumGroups = maximumGroups,
                    MinimumGroupSize = minimumGroupSize,
                    ConnectivityWeight = connectivityWeight,
                    GeometryWeight = geometryWeight,
                    DensityWeight = densityWeight,
                });
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var members = result.Members();
        var lineGroups = new DataTree<Curve>();
        var surfaceGroups = new DataTree<GeometryBase>();
        for (int g = 0; g < members.Length; g++)
        {
            var path = new GH_Path(g);
            lineGroups.EnsurePath(path);
            surfaceGroups.EnsurePath(path);
            foreach (int e in members[g])
            {
                if (e < result.LineCount)
                    lineGroups.Add(curves[e], path);
                else
                    surfaceGroups.Add(faces[e - result.LineCount], path);
            }
        }

        if (result.ComponentCount > 1)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"The model is not one connected structure: {result.ComponentCount} separate pieces. See the Disconnected and Isolated issues.");
        if (result.Issues.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{result.Issues.Count} issue(s) worth a look — see Issue Points and Report.");
        foreach (string note in result.Notes)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);

        var flagNames = Enum.GetValues<InsightFlag>().Where(f => f != InsightFlag.None).ToArray();

        da.SetDataList(0, result.Labels);
        da.SetDataTree(1, Trees.FromBuckets(members));
        da.SetDataTree(2, lineGroups);
        da.SetDataTree(3, surfaceGroups);
        da.SetDataList(4, result.Agreement);
        da.SetDataList(5, result.ConnectivityLabels);
        da.SetDataList(6, result.GeometryLabels);
        da.SetDataList(7, result.DensityLabels);
        da.SetDataTree(8, Trees.FromRows(result.Features));
        da.SetDataList(9, result.FeatureNames);
        da.SetDataTree(10, GraphData.ToTrees(result.Connectivity).Connectivity);
        da.SetDataList(11, result.Issues.Select(issue => issue.Element));
        da.SetDataList(12, result.Issues.Select(issue => new Point3d(issue.X, issue.Y, issue.Z)));
        da.SetDataList(13, result.Issues.Select(issue => issue.Reason));
        da.SetDataList(14, result.Flags.Select(flags =>
            string.Join(", ", flagNames.Where(f => flags.HasFlag(f)).Select(f => Naming.Humanise(f)))));
        da.SetData(15, result.Report());

        Message = $"{result.Groups} groups\n{result.Issues.Count} issues";
    }

    /// <summary>
    /// Every Brep face and mesh face as an element: its boundary corners for the
    /// engine, and the face itself to hand back grouped.
    /// </summary>
    private bool TryReadSurfaces(List<IGH_GeometricGoo> items, out List<double[,]> boundaries, out List<GeometryBase> faces)
    {
        // Locals, because the local function below cannot capture out parameters.
        var readBoundaries = new List<double[,]>();
        var readFaces = new List<GeometryBase>();
        boundaries = readBoundaries;
        faces = readFaces;

        for (int i = 0; i < items.Count; i++)
        {
            switch (items[i])
            {
                case GH_Mesh { Value: { } mesh }:
                    for (int f = 0; f < mesh.Faces.Count; f++)
                    {
                        var face = mesh.Faces[f];
                        var corners = (face.IsQuad ? new[] { face.A, face.B, face.C, face.D } : new[] { face.A, face.B, face.C })
                            .Select(v => (Point3d)mesh.Vertices[v]).ToList();

                        var single = new Mesh();
                        foreach (var corner in corners)
                            single.Vertices.Add(corner);
                        if (face.IsQuad)
                            single.Faces.AddFace(0, 1, 2, 3);
                        else
                            single.Faces.AddFace(0, 1, 2);
                        single.Normals.ComputeNormals();

                        boundaries.Add(Rows(corners));
                        faces.Add(single);
                    }

                    break;

                case GH_Brep { Value: { } brep }:
                    AddFaces(brep);
                    break;

                case GH_Surface { Value: { } surface }:
                    AddFaces(surface);
                    break;

                default:
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Surface {i} is {(items[i] is null ? "missing" : items[i].TypeName)}. Surfaces takes Breps, surfaces and meshes.");
                    return false;
            }
        }

        return true;

        void AddFaces(Brep brep)
        {
            foreach (var face in brep.Faces)
            {
                readBoundaries.Add(Rows(Corners(face)));
                readFaces.Add(face.DuplicateFace(false));
            }
        }
    }

    /// <summary>
    /// A face's outer boundary as corner points: the start of every straight edge,
    /// and a few points along every curved one.
    /// </summary>
    private static List<Point3d> Corners(BrepFace face)
    {
        var points = new List<Point3d>();
        var loop = face.OuterLoop.To3dCurve();
        var segments = loop.DuplicateSegments();
        if (segments.Length == 0)
            segments = new[] { loop };

        foreach (var segment in segments)
        {
            if (segment.IsLinear() && !segment.IsClosed)
            {
                points.Add(segment.PointAtStart);
                continue;
            }

            int pieces = segment.IsClosed ? 4 * CurvedEdgePieces : CurvedEdgePieces;
            var parameters = segment.DivideByCount(pieces, true) ?? Array.Empty<double>();
            points.AddRange(parameters.Take(parameters.Length - (segment.IsClosed ? 0 : 1)).Select(segment.PointAt));
        }

        return points;
    }

    private static double[,] Rows(IReadOnlyList<Point3d> points)
    {
        var rows = new double[points.Count, 3];
        for (int i = 0; i < points.Count; i++)
            (rows[i, 0], rows[i, 1], rows[i, 2]) = (points[i].X, points[i].Y, points[i].Z);

        return rows;
    }
}
