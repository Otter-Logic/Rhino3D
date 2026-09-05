using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace OtterLogic.Rhino.Conduits;

/// <summary>
/// Draws the in-progress relaxation without touching the document. Nothing is
/// committed until the user accepts, so Esc genuinely costs nothing.
/// </summary>
public sealed class RelaxationConduit : DisplayConduit
{
    private readonly DisplayMaterial _material = new(Color.FromArgb(120, 190, 220), 0.35);

    /// <summary>Mesh to preview. Assign a new one each iteration.</summary>
    public Mesh? Mesh { get; set; }

    /// <summary>Anchored points, drawn so the user can see what is pinned.</summary>
    public IReadOnlyList<Point3d> Anchors { get; set; } = Array.Empty<Point3d>();

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
        base.CalculateBoundingBox(e);
        if (Mesh is not null)
            e.IncludeBoundingBox(Mesh.GetBoundingBox(false));
    }

    protected override void PostDrawObjects(DrawEventArgs e)
    {
        if (Mesh is null) return;

        e.Display.DrawMeshShaded(Mesh, _material);
        e.Display.DrawMeshWires(Mesh, Color.FromArgb(40, 70, 90));

        foreach (Point3d anchor in Anchors)
            e.Display.DrawPoint(anchor, PointStyle.RoundControlPoint, 5, Color.OrangeRed);
    }
}
