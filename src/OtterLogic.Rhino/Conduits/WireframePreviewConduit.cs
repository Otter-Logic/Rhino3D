using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace OtterLogic.Rhino.Conduits;

/// <summary>
/// Draws a set of lines and points over the viewport without touching the
/// document, so a command can show a result before the user commits to it.
/// </summary>
public sealed class WireframePreviewConduit : DisplayConduit
{
    /// <summary>Lines to draw, grouped by colour and thickness.</summary>
    public List<(IReadOnlyList<Line> Lines, Color Colour, int Thickness)> Layers { get; } = new();

    /// <summary>
    /// Curves to draw, grouped the same way. For what a result wants pointed at
    /// rather than added — an outline, say — since members are always lines.
    /// </summary>
    public List<(IReadOnlyList<Curve> Curves, Color Colour, int Thickness)> Outlines { get; } = new();

    /// <summary>Points to mark, typically the nodes.</summary>
    public IReadOnlyList<Point3d> Points { get; set; } = Array.Empty<Point3d>();

    public Color PointColour { get; set; } = Color.OrangeRed;

    public void Clear()
    {
        Layers.Clear();
        Outlines.Clear();
        Points = Array.Empty<Point3d>();
    }

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
        base.CalculateBoundingBox(e);

        var box = BoundingBox.Empty;

        foreach (var (lines, _, _) in Layers)
            foreach (Line line in lines)
                box.Union(line.BoundingBox);

        foreach (var (curves, _, _) in Outlines)
            foreach (Curve curve in curves)
                box.Union(curve.GetBoundingBox(false));

        foreach (Point3d point in Points)
            box.Union(point);

        if (box.IsValid)
            e.IncludeBoundingBox(box);
    }

    protected override void PostDrawObjects(DrawEventArgs e)
    {
        foreach (var (lines, colour, thickness) in Layers)
            foreach (Line line in lines)
                e.Display.DrawLine(line, colour, thickness);

        foreach (var (curves, colour, thickness) in Outlines)
            foreach (Curve curve in curves)
                e.Display.DrawCurve(curve, colour, thickness);

        foreach (Point3d point in Points)
            e.Display.DrawPoint(point, PointStyle.RoundControlPoint, 5, PointColour);
    }
}
