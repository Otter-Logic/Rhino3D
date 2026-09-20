using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper;

/// <summary>
/// Surface elements as the structural tools read them: every Brep face and every
/// mesh face is one element, handed over as its boundary corners.
/// <para>
/// Shared so the Structural Insight Engine and Geometry QA read the same surface as
/// the same elements — a panel one tool calls face 3 is face 3 to the other.
/// </para>
/// </summary>
internal static class SurfaceInput
{
    /// <summary>Pieces a curved face edge is read as when no tolerance is given to follow it more closely.</summary>
    private const int CurvedEdgePieces = 4;

    /// <summary>The most pieces a curved edge is ever cut into, however tight the tolerance.</summary>
    private const int MostCurvedEdgePieces = 512;

    /// <summary>
    /// Every Brep face and mesh face as an element: its boundary corners for the
    /// library, and the face itself to hand back. Reports and returns false on
    /// anything that is not a surface.
    /// </summary>
    /// <param name="curveTolerance">
    /// How closely a curved edge is to be followed. Given, the edge is cut into as
    /// many pieces as it takes for the boundary to sit within this of the curve, which
    /// is what a tool that cuts to the edge needs. Left out, a curved edge is read as
    /// a few pieces, which is enough to know where a face is.
    /// </param>
    public static bool TryRead(
        GH_Component component, List<IGH_GeometricGoo> items, out List<double[,]> boundaries, out List<GeometryBase> faces,
        double? curveTolerance = null)
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
                    component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Surface {i} is {(items[i] is null ? "missing" : items[i].TypeName)}. Surfaces takes Breps, surfaces and meshes.");
                    return false;
            }
        }

        return true;

        void AddFaces(Brep brep)
        {
            foreach (var face in brep.Faces)
            {
                readBoundaries.Add(Rows(Corners(face, curveTolerance)));
                readFaces.Add(face.DuplicateFace(false));
            }
        }
    }

    /// <summary>
    /// A face's outer boundary as corner points: the start of every straight edge,
    /// and points along every curved one.
    /// </summary>
    private static List<Point3d> Corners(BrepFace face, double? curveTolerance)
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
            if (curveTolerance is double tolerance && tolerance > 0.0)
                pieces = Pieces(segment, pieces, tolerance);

            var parameters = segment.DivideByCount(pieces, true) ?? Array.Empty<double>();
            points.AddRange(parameters.Take(parameters.Length - (segment.IsClosed ? 0 : 1)).Select(segment.PointAt));
        }

        return points;
    }

    /// <summary>
    /// How many pieces a curved edge has to be cut into for the boundary to sit within
    /// the tolerance of it: the count is doubled until the furthest the curve strays
    /// from its own chords is small enough, or until cutting it finer would say
    /// nothing more.
    /// </summary>
    private static int Pieces(Curve segment, int from, double tolerance)
    {
        for (int pieces = from; pieces < MostCurvedEdgePieces; pieces *= 2)
        {
            var parameters = segment.DivideByCount(pieces, true);
            if (parameters is null || parameters.Length < 2)
                return pieces;

            double worst = 0.0;
            for (int i = 0; i + 1 < parameters.Length; i++)
            {
                var a = segment.PointAt(parameters[i]);
                var b = segment.PointAt(parameters[i + 1]);
                var bulge = segment.PointAt(0.5 * (parameters[i] + parameters[i + 1]));
                worst = Math.Max(worst, bulge.DistanceTo(new Line(a, b).ClosestPoint(bulge, limitToFiniteSegment: true)));
            }

            if (worst <= tolerance)
                return pieces;
        }

        return MostCurvedEdgePieces;
    }

    public static double[,] Rows(IReadOnlyList<Point3d> points)
    {
        var rows = new double[points.Count, 3];
        for (int i = 0; i < points.Count; i++)
            (rows[i, 0], rows[i, 1], rows[i, 2]) = (points[i].X, points[i].Y, points[i].Z);

        return rows;
    }
}
