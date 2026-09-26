using System.Drawing;
using OtterLogic.StructuralForm;
using OtterLogic.Rhino.Conduits;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace OtterLogic.Rhino.Commands;

/// <summary>
/// Sets out a rectangular structural grid from typed bay spacings.
/// <para>
/// The Rhino counterpart to the Rectangular Grid Grasshopper component.
/// Identical engine, <see cref="RectangularGridGenerator.Generate"/>,
/// presented as a walkthrough: pick an origin, type the bays each way, then
/// adjust against a live preview before anything is added to the document.
/// </para>
/// <para>
/// The grid lies in the active construction plane, through the picked
/// origin. That is how Rhino already expresses "which way is the building
/// facing": a skewed block is a rotated CPlane, and the grid should follow it
/// rather than carry a second notion of direction. <c>Angle</c> in the
/// preview turns the grid within that plane for the case where the CPlane is
/// the drawing's and only the grid is skewed.
/// </para>
/// </summary>
public sealed class OtterGridCommand : Command
{
    // Remembered between runs within a session, as Rhino commands normally do.
    // Null until the first run, when they become six and eight metres in the
    // document's units: bays a user can accept or overtype, rather than
    // nothing and a preview of nothing.
    private static IReadOnlyList<double>? _xSpacings;
    private static IReadOnlyList<double>? _ySpacings;
    private static double _overhang;
    private static double _angle;

    private const string EnglishNameText = "OtterGrid";

    /// <summary>
    /// Root layer name, with the run number appended: OtterGrid1, OtterGrid2,
    /// ... Shared with <see cref="OtterRadialGridCommand"/>, so a rectangular
    /// grid and a radial one number on from each other: they are both grids,
    /// and a document with one of each has two grids, not a first of each.
    /// </summary>
    internal const string LayerPrefix = "OtterGrid";

    // Named for what goes on them, in the words the component's ports use.
    private const string XLayer = "X gridline";
    private const string YLayer = "Y gridline";
    private const string NodeLayer = "Node";

    internal static readonly Color RootColour = Color.FromArgb(60, 60, 65);
    internal static readonly Color GridlineColour = Color.FromArgb(25, 90, 150);
    internal static readonly Color CrossColour = Color.FromArgb(45, 140, 110);
    internal static readonly Color NodeColour = Color.FromArgb(200, 60, 40);

    public OtterGridCommand() => Instance = this;

    public static OtterGridCommand? Instance { get; private set; }

    public override string EnglishName => EnglishNameText;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        double metre = RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);
        _xSpacings ??= new[] { 6.0 * metre, 6.0 * metre, 6.0 * metre };
        _ySpacings ??= new[] { 8.0 * metre, 8.0 * metre };

        Plane cplane = ConstructionPlane(doc);

        // Step 1: where the grid starts.
        Result step = Ask.Point("Grid origin", cplane.Origin, out Point3d origin);
        if (step != Result.Success) return step;

        // Steps 2 and 3: the bays each way.
        step = Ask.Bays("X spacings, along the construction plane's X axis", ref _xSpacings);
        if (step != Result.Success) return step;

        step = Ask.Bays("Y spacings, along its Y axis", ref _ySpacings);
        if (step != Result.Success) return step;

        var conduit = new WireframePreviewConduit { Enabled = true };

        try
        {
            // Step 4: the grid, adjusted against the preview until accepted.
            return PreviewAndCommit(doc, conduit, cplane, origin);
        }
        finally
        {
            conduit.Enabled = false;
            doc.Views.Redraw();
        }
    }

    /// <summary>
    /// The active viewport's construction plane, or world XY when there is no
    /// view to ask, which happens when a command is run from a script.
    /// </summary>
    internal static Plane ConstructionPlane(RhinoDoc doc)
        => doc.Views.ActiveView?.ActiveViewport.ConstructionPlane() ?? Plane.WorldXY;

    /// <summary>The construction plane moved to the origin and turned by the remembered angle.</summary>
    private static Plane GridPlane(Plane cplane, Point3d origin)
    {
        Plane plane = cplane;
        plane.Origin = origin;
        plane.Rotate(RhinoMath.ToRadians(_angle), plane.ZAxis);
        return plane;
    }

    /// <summary>
    /// Draw the result, let the user keep tuning it against that preview, and
    /// add it to the document only on Accept. Nothing is committed until then,
    /// so Esc genuinely costs nothing.
    /// </summary>
    private static Result PreviewAndCommit(RhinoDoc doc, WireframePreviewConduit conduit, Plane cplane, Point3d origin)
    {
        while (true)
        {
            RectangularGrid grid;

            try
            {
                grid = RectangularGridGenerator.Generate(new RectangularGridOptions
                {
                    Plane = GridPlane(cplane, origin),
                    XSpacings = _xSpacings!,
                    YSpacings = _ySpacings!,
                    Overhang = _overhang,
                    Tolerance = doc.ModelAbsoluteTolerance,
                });
            }
            catch (ArgumentException ex)
            {
                RhinoApp.WriteLine($"{EnglishNameText}: {ex.Message}");
                return Result.Failure;
            }

            ShowPreview(conduit, grid);
            doc.Views.Redraw();

            using var getter = new GetOption();
            getter.SetCommandPrompt(
                $"{grid.XGridlines.Count} x {grid.YGridlines.Count} gridlines, {Count(grid.Nodes.Count, "node")} — accept?");

            int accept = getter.AddOption("Accept");
            int changeX = getter.AddOption("XSpacings", Spacings.Describe(_xSpacings!, ","));
            int changeY = getter.AddOption("YSpacings", Spacings.Describe(_ySpacings!, ","));
            int changeOverhang = getter.AddOption("Overhang", _overhang.ToString("0.###"));
            int changeAngle = getter.AddOption("Angle", _angle.ToString("0.###"));
            int changeOrigin = getter.AddOption("Origin");
            getter.AcceptNothing(true);   // Enter accepts

            GetResult result = getter.Get();

            if (result == GetResult.Nothing)
                return Commit(doc, grid);

            if (result != GetResult.Option)
                return getter.CommandResult();   // Esc discards everything

            int chosen = getter.Option().Index;

            if (chosen == accept)
                return Commit(doc, grid);

            if (chosen == changeX)
            {
                Ask.Bays("X spacings", ref _xSpacings!);
            }
            else if (chosen == changeY)
            {
                Ask.Bays("Y spacings", ref _ySpacings!);
            }
            else if (chosen == changeOverhang)
            {
                double overhang = _overhang;
                if (RhinoGet.GetNumber("Overhang past the outer gridlines", true, ref overhang, 0.0, 1e9) == Result.Success)
                    _overhang = overhang;
            }
            else if (chosen == changeAngle)
            {
                double angle = _angle;
                if (RhinoGet.GetNumber("Angle in degrees from the construction plane's X axis", true, ref angle, -360.0, 360.0) == Result.Success)
                    _angle = angle;
            }
            else if (chosen == changeOrigin)
            {
                if (Ask.Point("Grid origin", origin, out Point3d moved) == Result.Success)
                    origin = moved;
            }
        }
    }

    /// <summary>
    /// The two directions in two colours, so a grid whose X and Y spacings
    /// were typed the wrong way round shows it before it is baked.
    /// </summary>
    private static void ShowPreview(WireframePreviewConduit conduit, RectangularGrid grid)
    {
        conduit.Clear();

        conduit.Layers.Add((grid.XGridlines, GridlineColour, 2));
        conduit.Layers.Add((grid.YGridlines, CrossColour, 2));

        conduit.Points = grid.Nodes;
        conduit.PointColour = NodeColour;
    }

    /// <summary>
    /// Adds the gridlines and nodes to one layer tree for the run: the X
    /// gridlines, the Y gridlines and the nodes each on their own sub-layer,
    /// so a direction can be hidden, coloured or labelled on its own.
    /// </summary>
    private static Result Commit(RhinoDoc doc, RectangularGrid grid)
    {
        string name = RunLayers.NextName(doc, LayerPrefix);

        int root = RunLayers.Add(doc, name, Guid.Empty, RootColour);
        if (root < 0)
        {
            RhinoApp.WriteLine($"{EnglishNameText}: could not create the layer {name}, so nothing was added.");
            return Result.Failure;
        }

        Guid rootId = doc.Layers[root].Id;

        int xLayer = RunLayers.Sub(doc, EnglishNameText, XLayer, rootId, GridlineColour, root);
        foreach (Line gridline in grid.XGridlines)
            doc.Objects.AddLine(gridline, RunLayers.Attributes(XLayer, xLayer));

        int yLayer = RunLayers.Sub(doc, EnglishNameText, YLayer, rootId, CrossColour, root);
        foreach (Line gridline in grid.YGridlines)
            doc.Objects.AddLine(gridline, RunLayers.Attributes(YLayer, yLayer));

        int nodeLayer = RunLayers.Sub(doc, EnglishNameText, NodeLayer, rootId, NodeColour, root);
        foreach (Point3d node in grid.Nodes)
            doc.Objects.AddPoint(node, RunLayers.Attributes(NodeLayer, nodeLayer));

        doc.Views.Redraw();

        RhinoApp.WriteLine(
            $"{EnglishNameText}: added {Count(grid.Gridlines.Count, "gridline")} and {Count(grid.Nodes.Count, "node")} to {name}: "
            + $"{Spacings.Describe(_xSpacings!)} by {Spacings.Describe(_ySpacings!)}.");

        return Result.Success;
    }

    internal static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
