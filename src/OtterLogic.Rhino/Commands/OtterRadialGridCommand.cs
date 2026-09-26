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
/// Sets out a radial structural grid from typed ring spacings, a sweep and a
/// bay count.
/// <para>
/// The Rhino counterpart to the Radial Grid Grasshopper component. Identical
/// engine, <see cref="RadialGridGenerator.Generate"/>, presented as a
/// walkthrough: pick the centre, type the rings, give the hole in the
/// middle, the sweep and the number of bays, then adjust against a live
/// preview before anything is added to the document.
/// </para>
/// <para>
/// The hole and the sweep are asked before the preview rather than left to
/// it because they are the questions that change the shape most: a stadium
/// round an oval pitch, a full circle and a quarter are different buildings,
/// and a user who wanted a quarter should not have to see a circle first.
/// </para>
/// </summary>
public sealed class OtterRadialGridCommand : Command
{
    // Remembered between runs within a session, as Rhino commands normally do.
    private static IReadOnlyList<double>? _ringSpacings;
    private static double _innerU;
    private static double _innerV;
    private static double _sweep = 360.0;
    private static double _startAngle;
    private static int _bays = 12;
    private static double _overhang;

    private const string EnglishNameText = "OtterRadialGrid";

    // Named for what goes on them, in the words the component's ports use.
    private const string RayLayer = "Ray";
    private const string RingLayer = "Ring";
    private const string NodeLayer = "Node";

    public OtterRadialGridCommand() => Instance = this;

    public static OtterRadialGridCommand? Instance { get; private set; }

    public override string EnglishName => EnglishNameText;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        double metre = RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);
        _ringSpacings ??= new[] { 6.0 * metre, 6.0 * metre, 6.0 * metre };

        Plane cplane = OtterGridCommand.ConstructionPlane(doc);

        // Step 1: the centre.
        Result step = Ask.Point("Grid centre", cplane.Origin, out Point3d centre);
        if (step != Result.Success) return step;

        // Step 2: the rings, as bays outward from the centre.
        step = Ask.Bays("Ring spacings, outward from the centre", ref _ringSpacings);
        if (step != Result.Success) return step;

        // Steps 3 and 4: the hole in the middle, as half-axes from the centre.
        // Both zero is no hole; equal is round; different is the stadium.
        double innerU = _innerU;
        step = RhinoGet.GetNumber("Inner U: half the hole's width along the construction plane's X axis (0 runs the rays to the centre)", true, ref innerU, 0.0, 1e9);
        if (step != Result.Success) return step;
        _innerU = innerU;

        // V defaults to U, so a round hole is one number and Enter.
        double innerV = _innerV > 0.0 ? _innerV : _innerU;
        step = RhinoGet.GetNumber("Inner V: half the hole's depth along its Y axis (equal to U for a round hole)", true, ref innerV, 0.0, 1e9);
        if (step != Result.Success) return step;
        _innerV = innerV;

        // Step 5: how much of the way round.
        double sweep = _sweep;
        step = RhinoGet.GetNumber("Sweep in degrees (360 for the whole way round, 90 for a quarter)", true, ref sweep, 1e-6, 360.0);
        if (step != Result.Success) return step;
        _sweep = sweep;

        // Step 6: how many bays across it.
        int bays = _bays;
        step = RhinoGet.GetInteger("Number of bays across the sweep", true, ref bays, 1, 3600);
        if (step != Result.Success) return step;
        _bays = bays;

        var conduit = new WireframePreviewConduit { Enabled = true };

        try
        {
            // Step 7: the grid, adjusted against the preview until accepted.
            return PreviewAndCommit(doc, conduit, cplane, centre);
        }
        finally
        {
            conduit.Enabled = false;
            doc.Views.Redraw();
        }
    }

    /// <summary>
    /// Draw the result, let the user keep tuning it against that preview, and
    /// add it to the document only on Accept. Nothing is committed until then,
    /// so Esc genuinely costs nothing.
    /// </summary>
    private static Result PreviewAndCommit(RhinoDoc doc, WireframePreviewConduit conduit, Plane cplane, Point3d centre)
    {
        while (true)
        {
            RadialGrid grid;

            try
            {
                Plane plane = cplane;
                plane.Origin = centre;

                grid = RadialGridGenerator.Generate(new RadialGridOptions
                {
                    Plane = plane,
                    RingSpacings = _ringSpacings!,
                    InnerU = _innerU,
                    InnerV = _innerV,
                    Sweep = _sweep,
                    StartAngle = _startAngle,
                    Bays = _bays,
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

            foreach (FormNote note in grid.Notes)
                RhinoApp.WriteLine($"{EnglishNameText}: {note.Message}");

            using var getter = new GetOption();
            getter.SetCommandPrompt(
                $"{OtterGridCommand.Count(grid.Rays.Count, "ray")}, {OtterGridCommand.Count(grid.Rings.Count, "ring")}, "
                + $"{OtterGridCommand.Count(grid.Nodes.Count, "node")} — accept?");

            int accept = getter.AddOption("Accept");
            int changeRings = getter.AddOption("Rings", Spacings.Describe(_ringSpacings!, ","));
            int changeSweep = getter.AddOption("Sweep", _sweep.ToString("0.###"));
            int changeBays = getter.AddOption("Bays", _bays.ToString());
            int changeInnerU = getter.AddOption("InnerU", _innerU.ToString("0.###"));
            int changeInnerV = getter.AddOption("InnerV", _innerV.ToString("0.###"));
            int changeStart = getter.AddOption("StartAngle", _startAngle.ToString("0.###"));
            int changeOverhang = getter.AddOption("Overhang", _overhang.ToString("0.###"));
            int changeCentre = getter.AddOption("Centre");
            getter.AcceptNothing(true);   // Enter accepts

            GetResult result = getter.Get();

            if (result == GetResult.Nothing)
                return Commit(doc, grid);

            if (result != GetResult.Option)
                return getter.CommandResult();   // Esc discards everything

            int chosen = getter.Option().Index;

            if (chosen == accept)
                return Commit(doc, grid);

            if (chosen == changeRings)
            {
                Ask.Bays("Ring spacings", ref _ringSpacings!);
            }
            else if (chosen == changeSweep)
            {
                double sweep = _sweep;
                if (RhinoGet.GetNumber("Sweep in degrees", true, ref sweep, 1e-6, 360.0) == Result.Success)
                    _sweep = sweep;
            }
            else if (chosen == changeBays)
            {
                int bays = _bays;
                if (RhinoGet.GetInteger("Number of bays across the sweep", true, ref bays, 1, 3600) == Result.Success)
                    _bays = bays;
            }
            else if (chosen == changeInnerU)
            {
                double inner = _innerU;
                if (RhinoGet.GetNumber("Inner U: half the hole's width along X (0 with V for no hole)", true, ref inner, 0.0, 1e9) == Result.Success)
                    _innerU = inner;
            }
            else if (chosen == changeInnerV)
            {
                double inner = _innerV;
                if (RhinoGet.GetNumber("Inner V: half the hole's depth along Y (equal to U for a round hole)", true, ref inner, 0.0, 1e9) == Result.Success)
                    _innerV = inner;
            }
            else if (chosen == changeStart)
            {
                double start = _startAngle;
                if (RhinoGet.GetNumber("Start angle in degrees from the construction plane's X axis", true, ref start, -360.0, 360.0) == Result.Success)
                    _startAngle = start;
            }
            else if (chosen == changeOverhang)
            {
                double overhang = _overhang;
                if (RhinoGet.GetNumber("Overhang past the outer ring", true, ref overhang, 0.0, 1e9) == Result.Success)
                    _overhang = overhang;
            }
            else if (chosen == changeCentre)
            {
                if (Ask.Point("Grid centre", centre, out Point3d moved) == Result.Success)
                    centre = moved;
            }
        }
    }

    /// <summary>Rays and rings in the two colours the rectangular grid uses for its two directions.</summary>
    private static void ShowPreview(WireframePreviewConduit conduit, RadialGrid grid)
    {
        conduit.Clear();

        conduit.Layers.Add((grid.Rays, OtterGridCommand.GridlineColour, 2));
        conduit.Outlines.Add((grid.Rings, OtterGridCommand.CrossColour, 2));

        conduit.Points = grid.Nodes;
        conduit.PointColour = OtterGridCommand.NodeColour;
    }

    /// <summary>
    /// Adds the rays, rings and nodes to one layer tree for the run, numbered
    /// on from any rectangular grid already in the document: they share
    /// <see cref="OtterGridCommand.LayerPrefix"/>.
    /// </summary>
    private static Result Commit(RhinoDoc doc, RadialGrid grid)
    {
        string name = RunLayers.NextName(doc, OtterGridCommand.LayerPrefix);

        int root = RunLayers.Add(doc, name, Guid.Empty, OtterGridCommand.RootColour);
        if (root < 0)
        {
            RhinoApp.WriteLine($"{EnglishNameText}: could not create the layer {name}, so nothing was added.");
            return Result.Failure;
        }

        Guid rootId = doc.Layers[root].Id;

        int rayLayer = RunLayers.Sub(doc, EnglishNameText, RayLayer, rootId, OtterGridCommand.GridlineColour, root);
        foreach (Line ray in grid.Rays)
            doc.Objects.AddLine(ray, RunLayers.Attributes(RayLayer, rayLayer));

        int ringLayer = RunLayers.Sub(doc, EnglishNameText, RingLayer, rootId, OtterGridCommand.CrossColour, root);
        foreach (Curve ring in grid.Rings)
            doc.Objects.AddCurve(ring, RunLayers.Attributes(RingLayer, ringLayer));

        int nodeLayer = RunLayers.Sub(doc, EnglishNameText, NodeLayer, rootId, OtterGridCommand.NodeColour, root);
        foreach (Point3d node in grid.Nodes)
            doc.Objects.AddPoint(node, RunLayers.Attributes(NodeLayer, nodeLayer));

        doc.Views.Redraw();

        RhinoApp.WriteLine(
            $"{EnglishNameText}: added {OtterGridCommand.Count(grid.Rays.Count, "ray")}, "
            + $"{OtterGridCommand.Count(grid.Rings.Count, "ring")} and {OtterGridCommand.Count(grid.Nodes.Count, "node")} to {name}: "
            + $"{Spacings.Describe(_ringSpacings!)} over {_sweep:0.###} degrees in {_bays} bays"
            + (grid.Centre.HasValue ? " from the centre." : $" round a hole {_innerU:0.###} by {_innerV:0.###}."));

        return Result.Success;
    }
}
