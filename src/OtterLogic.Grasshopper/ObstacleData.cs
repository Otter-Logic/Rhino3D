using OtterLogic.Graphs.Planar;
using OtterLogic.Graphs.Spatial;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper;

/// <summary>
/// Turns the geometry a user draws as obstacles into the plain coordinate
/// arrays <see cref="PlanarObstacles"/> and <see cref="SolidObstacles"/> read.
/// <para>
/// Unpacking only. Whether a step is blocked is decided in Graphs, on arrays a
/// test can build with no Rhino running; all this does is flatten curves to
/// polylines and Breps to meshes, read them into arrays, and say which it had
/// to leave out.
/// </para>
/// </summary>
internal static class ObstacleData
{
    /// <summary>The wording both graph builders use for the Obstacles input, so they cannot drift.</summary>
    public const string Description =
        "Closed curves nothing may pass through, read in plan — their Z is ignored. A route may "
        + "touch an outline and run along it, so for clearance offset the curves outwards first. "
        + "Curved outlines are flattened to polylines at the document tolerance. Open curves are "
        + "left out, with a warning.";

    /// <summary>The wording for the Solids input, the three-dimensional obstacles.</summary>
    public const string SolidsDescription =
        "Optional. Meshes and Breps nothing may pass through, in three dimensions. A closed one is a "
        + "solid with an inside; an open one — a wall, a slab — blocks exactly what crosses it. A route "
        + "may run along a face and touch an edge, so for clearance offset or thicken them first. Breps "
        + "are meshed at the document tolerance. Anything else is left out, with a warning.";

    /// <summary>
    /// Reads meshes and Breps as triangle arrays. Quads are split along a diagonal;
    /// a Brep is meshed first, which is the one Rhino call in the chain.
    /// </summary>
    /// <param name="geometry">Whatever was wired in; nulls and anything neither a mesh nor a Brep are skipped.</param>
    /// <param name="tolerance">Document tolerance: how finely a Brep is meshed, and how close counts as touching.</param>
    /// <param name="skipped">How many items were not a mesh or a Brep, or had no faces.</param>
    public static SolidObstacles ReadSolids(IEnumerable<GeometryBase?> geometry, double tolerance, out int skipped)
    {
        var solids = new List<(double[,] Vertices, int[,] Faces)>();
        skipped = 0;

        foreach (GeometryBase? item in geometry)
        {
            if (item is null)
                continue;

            var meshes = item switch
            {
                Mesh mesh => new[] { mesh },
                Brep brep => Mesh.CreateFromBrep(brep, MeshingParameters.FastRenderMesh) ?? Array.Empty<Mesh>(),
                Extrusion extrusion => extrusion.ToBrep() is { } asBrep
                    ? Mesh.CreateFromBrep(asBrep, MeshingParameters.FastRenderMesh) ?? Array.Empty<Mesh>()
                    : Array.Empty<Mesh>(),
                _ => Array.Empty<Mesh>(),
            };

            var joined = new Mesh();
            foreach (Mesh mesh in meshes)
                joined.Append(mesh);

            if (joined.Faces.Count == 0)
            {
                skipped++;
                continue;
            }

            joined.Faces.ConvertQuadsToTriangles();
            joined.Compact();

            var vertices = new double[joined.Vertices.Count, 3];
            for (int v = 0; v < joined.Vertices.Count; v++)
            {
                Point3d p = joined.Vertices.Point3dAt(v);
                vertices[v, 0] = p.X;
                vertices[v, 1] = p.Y;
                vertices[v, 2] = p.Z;
            }

            var faces = new int[joined.Faces.Count, 3];
            for (int f = 0; f < joined.Faces.Count; f++)
            {
                MeshFace face = joined.Faces[f];
                faces[f, 0] = face.A;
                faces[f, 1] = face.B;
                faces[f, 2] = face.C;
            }

            solids.Add((vertices, faces));
        }

        return new SolidObstacles(solids, tolerance);
    }

    /// <summary>n x 3, x, y, z: the points as they are.</summary>
    public static double[,] InSpace(IReadOnlyList<Point3d> points)
    {
        var x = new double[points.Count, 3];
        for (int i = 0; i < points.Count; i++)
        {
            x[i, 0] = points[i].X;
            x[i, 1] = points[i].Y;
            x[i, 2] = points[i].Z;
        }

        return x;
    }

    /// <param name="curves">Whatever was wired in; nulls and open curves are skipped.</param>
    /// <param name="tolerance">Document tolerance: how closely a curve is flattened, and how close counts as touching.</param>
    /// <param name="corners">Every outline vertex as drawn, in the order <see cref="PlanarObstacles.Corners"/> returns them.</param>
    /// <param name="skipped">How many curves were open, or too degenerate to be an outline.</param>
    public static PlanarObstacles Read(IEnumerable<Curve?> curves, double tolerance, out List<Point3d> corners, out int skipped)
    {
        var outlines = new List<double[,]>();
        corners = new List<Point3d>();
        skipped = 0;

        foreach (Curve? curve in curves)
        {
            if (curve is null)
                continue;

            if (!curve.IsClosed || !TryFlatten(curve, tolerance, out Polyline polyline))
            {
                skipped++;
                continue;
            }

            // A closed polyline repeats its first point; an outline does not.
            var vertices = polyline.ToList();
            if (vertices.Count > 1 && Math.Abs(vertices[0].X - vertices[^1].X) <= tolerance
                                   && Math.Abs(vertices[0].Y - vertices[^1].Y) <= tolerance)
                vertices.RemoveAt(vertices.Count - 1);

            if (vertices.Count < 3)
            {
                skipped++;
                continue;
            }

            var outline = new double[vertices.Count, 2];
            for (int v = 0; v < vertices.Count; v++)
            {
                outline[v, 0] = vertices[v].X;
                outline[v, 1] = vertices[v].Y;
            }

            outlines.Add(outline);
            corners.AddRange(vertices);
        }

        return new PlanarObstacles(outlines, tolerance);
    }

    /// <summary>n x 2, x then y: the points read in plan.</summary>
    public static double[,] InPlan(IReadOnlyList<Point3d> points)
    {
        var x = new double[points.Count, 2];
        for (int i = 0; i < points.Count; i++)
        {
            x[i, 0] = points[i].X;
            x[i, 1] = points[i].Y;
        }

        return x;
    }

    private static bool TryFlatten(Curve curve, double tolerance, out Polyline polyline)
    {
        if (curve.TryGetPolyline(out polyline))
            return true;

        // 0.1 rad keeps a circle to a few dozen sides; the distance tolerance does the rest.
        PolylineCurve? flattened = curve.ToPolyline(tolerance, 0.1, 0.0, 0.0);
        return flattened is not null && flattened.TryGetPolyline(out polyline);
    }
}
