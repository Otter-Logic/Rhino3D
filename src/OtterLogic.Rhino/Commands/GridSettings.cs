using OtterLogic.StructuralForm;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace OtterLogic.Rhino.Commands;

/// <summary>
/// A surface grid's settings as a command remembers them between runs, and
/// the prompts that set them.
/// <para>
/// Shared by OtterSurfaceGrid and OtterSpaceTruss, each holding an instance of
/// its own, since a grid drawn to look at and a grid drawn to truss are
/// seldom the same grid. Lifted out of the two commands when the space truss
/// prompt reached eighteen options: the grid's nine were the same nine in
/// both, and a rule about which of them to show had to be written once.
/// </para>
/// <para>
/// That rule is the point. <see cref="Offer"/> puts on the prompt only what
/// the current settings read: the diagonal rule under a triangulated grid,
/// the flip under a triangulated grid or a diagrid, the snap strictness when
/// there are snap points to be strict about, the clip when the surface is
/// trimmed. An option that would change nothing is a decision the user is
/// asked to make for no reason, and the preview would not move.
/// </para>
/// </summary>
internal sealed class GridSettings
{
    public GridPattern Pattern = GridPattern.Quad;
    public DiagonalRule Diagonals = DiagonalRule.OneWay;
    public bool Flip;
    public int DivisionsU = 6;
    public int DivisionsV = 6;
    public double SpacingU;
    public double SpacingV;
    public SnapStrictness Strictness = SnapStrictness.Relaxed;
    public bool ClipToTrim;

    // Option indices from the prompt last offered; -1 for one not offered.
    private int _pattern = -1, _divisions = -1, _spacing = -1, _swap = -1;
    private int _diagonals = -1, _flip = -1, _strictness = -1, _clip = -1;

    internal SurfaceGridOptions ToOptions(Point3d[] snapPoints, double tolerance) => new()
    {
        Pattern = Pattern,
        DivisionsU = DivisionsU,
        DivisionsV = DivisionsV,
        SpacingU = SpacingU,
        SpacingV = SpacingV,
        SnapPoints = snapPoints,
        Strictness = Strictness,
        Diagonals = Diagonals,
        Flip = Flip,
        ClipToTrim = ClipToTrim,
        Tolerance = tolerance,
    };

    /// <summary>
    /// The two questions asked before there is a preview to adjust against:
    /// the pattern, and how finely each way. Which way is U is not worth
    /// asking about in advance; it shows in the preview, and the two numbers
    /// are one option away from being swapped.
    /// </summary>
    internal Result AskUpFront()
    {
        Result step = Pick.Enum("Grid pattern", ref Pattern);
        if (step != Result.Success) return step;

        return AskDivisions();
    }

    /// <summary>
    /// Adds the grid's options to a preview prompt, with the current value
    /// beside each, and only the ones the current settings read.
    /// </summary>
    internal void Offer(GetOption getter, bool hasSnapPoints, bool trimmed)
    {
        _pattern = getter.AddOption("Pattern", Pattern.ToString());
        _divisions = getter.AddOption("Divisions", $"{DivisionsU}x{DivisionsV}");
        _spacing = getter.AddOption("Spacing", SpacingU > 0.0 || SpacingV > 0.0 ? $"{SpacingU:0.###}x{SpacingV:0.###}" : "Off");
        _swap = getter.AddOption("SwapUV");

        _diagonals = Pattern == GridPattern.Triangulated ? getter.AddOption("Diagonals", Diagonals.ToString()) : -1;
        _flip = Pattern != GridPattern.Quad ? getter.AddOption("Flip", Flip ? "Yes" : "No") : -1;
        _strictness = hasSnapPoints ? getter.AddOption("Strictness", Strictness.ToString()) : -1;
        _clip = trimmed ? getter.AddOption("ClipToTrim", ClipToTrim ? "Yes" : "No") : -1;
    }

    /// <summary>True when the chosen option was one of the grid's, and has been dealt with.</summary>
    internal bool Handle(int chosen)
    {
        if (chosen < 0) return false;

        if (chosen == _pattern)
        {
            Pick.Enum("Grid pattern", ref Pattern);
        }
        else if (chosen == _divisions)
        {
            AskDivisions();
        }
        else if (chosen == _spacing)
        {
            AskSpacing();
        }
        else if (chosen == _swap)
        {
            // The numbers change places; the surface's directions are its
            // own and stay where they are.
            (DivisionsU, DivisionsV) = (DivisionsV, DivisionsU);
            (SpacingU, SpacingV) = (SpacingV, SpacingU);
        }
        else if (chosen == _diagonals)
        {
            Pick.Enum("Diagonals of a triangulated grid", ref Diagonals);
        }
        else if (chosen == _flip)
        {
            Flip = !Flip;
        }
        else if (chosen == _strictness)
        {
            Pick.Enum("Snap strictness", ref Strictness);
        }
        else if (chosen == _clip)
        {
            ClipToTrim = !ClipToTrim;
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>U then V, Enter keeping either. Zero goes by spacing, or by the edges.</summary>
    private Result AskDivisions()
    {
        int u = DivisionsU;
        Result step = RhinoGet.GetInteger("Divisions in U (0 to go by spacing, or by the edges)", true, ref u, 0, 10000);
        if (step != Result.Success) return step;
        DivisionsU = u;

        int v = DivisionsV;
        step = RhinoGet.GetInteger("Divisions in V (0 to go by spacing, or by the edges)", true, ref v, 0, 10000);
        if (step != Result.Success) return step;
        DivisionsV = v;

        return Result.Success;
    }

    /// <summary>
    /// U then V. Divisions override spacing, so a direction given a spacing
    /// has its divisions cleared; otherwise somebody who has just typed a
    /// spacing would see nothing change.
    /// </summary>
    private void AskSpacing()
    {
        double u = SpacingU;
        if (RhinoGet.GetNumber("Panel spacing in U (0 to go by divisions)", true, ref u, 0.0, 1e9) != Result.Success) return;
        SpacingU = u;
        if (u > 0.0) DivisionsU = 0;

        double v = SpacingV;
        if (RhinoGet.GetNumber("Panel spacing in V (0 to go by divisions)", true, ref v, 0.0, 1e9) != Result.Success) return;
        SpacingV = v;
        if (v > 0.0) DivisionsV = 0;
    }
}
