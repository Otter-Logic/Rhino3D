using OtterLogic.Graphs.Planar;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper;

/// <summary>
/// Turns the closed curves a user draws as obstacles into the plain coordinate
/// outlines <see cref="PlanarObstacles"/> reads.
/// <para>
/// Unpacking only. Whether a step is blocked is decided in Graphs, on arrays a
/// test can build with no Rhino running; all this does is flatten curves to
/// polylines, read them in plan, and say which it had to leave out.
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
