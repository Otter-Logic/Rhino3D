using System.Drawing;
using OtterLogic.Core;
using OtterLogic.StructuralForm;
using OtterLogic.Rhino.Conduits;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace OtterLogic.Rhino.Commands;

/// <summary>
/// Builds a double-layer space truss on a surface.
/// <para>
/// The Rhino counterpart to the Space Truss Grasshopper component. Identical
/// engines — <see cref="SurfaceGridGenerator"/> for the top layer, then
/// <see cref="SpaceTrussGenerator"/> on what it made — presented as a
/// walkthrough: pick what to grid, say how finely and how deep, then adjust
/// against a live preview before anything is added to the document.
/// </para>
/// <para>
/// Grasshopper takes the grid on a wire from Surface Grid; a command has no
/// wire, so this one asks Surface Grid's questions itself and then its own.
/// The two commands pick their input the same way and remember their own
/// settings separately, since a grid drawn to look at and a grid drawn to
/// truss are seldom the same grid.
/// </para>
/// </summary>
public sealed class OtterSpaceTrussCommand : Command
{
    // Remembered between runs within a session, as Rhino commands normally do.
    private static GridPattern _pattern = GridPattern.Quad;
    private static DiagonalRule _diagonals = DiagonalRule.OneWay;
    private static bool _flip;
    private static int _divisionsU = 6;
    private static int _divisionsV = 6;
    private static double _spacingU;
    private static double _spacingV;
    private static SnapStrictness _strictness = SnapStrictness.Relaxed;
    private static bool _clip;

    private static double _depth;
    private static SpaceTrussType _type = SpaceTrussType.Offset;
    private static TrussType _web = TrussType.Warren;
    private static bool _flipWeb;
    private static bool _endPosts = true;
    private static DepthDirection _depthAlong = DepthDirection.SurfaceNormal;
    private static bool _flipDepth;

    private const string EnglishNameText = "OtterSpaceTruss";

    /// <summary>Root layer name, with the run number appended: OtterSpaceTruss1, OtterSpaceTruss2, ...</summary>
    private const string LayerPrefix = "OtterSpaceTruss";

    // Per layer, matching the component's ports: which layer a node is in is
    // the useful thing about it, and one merged layer throws it away.
    private const string TopNodeLayer = "Top node";
    private const string BottomNodeLayer = "Bottom node";
    private static readonly Color NodeColour = Color.FromArgb(200, 60, 40);
    private static readonly Color RootColour = Color.FromArgb(60, 60, 65);

    /// <summary>
    /// One sub-layer per likely section group, in the colours Flat Truss uses
    /// for the same roles, so a truss reads the same whichever tool drew it.
    /// </summary>
    private static readonly (TrussMemberRole Role, Color Colour, int Thickness)[] MemberLayers =
    {
        (TrussMemberRole.TopChord,    Color.FromArgb( 25,  90, 150), 3),
        (TrussMemberRole.BottomChord, Color.FromArgb( 35, 130, 110), 3),
        (TrussMemberRole.Vertical,    Color.FromArgb(110,  90, 175), 2),
        (TrussMemberRole.Diagonal,    Color.FromArgb( 30,  90, 140), 2),
        (TrussMemberRole.EndPost,     Color.FromArgb(200, 110,  40), 3),
    };

    public OtterSpaceTrussCommand() => Instance = this;

    public static OtterSpaceTrussCommand? Instance { get; private set; }

    public override string EnglishName => EnglishNameText;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // Step 1: what to truss — the same pick Surface Grid takes.
        Result step = OtterSurfaceGridCommand.SelectInput(doc, EnglishNameText, out Brep? surface, out Curve[] edges);
        if (step != Result.Success) return step;

        // Step 2: the top layer's pattern.
        step = Pick.Enum("Grid pattern", ref _pattern);
        if (step != Result.Success) return step;

        // Step 3: how finely, each way. Which way is U shows in the preview,
        // and the two numbers are one option away from being swapped.
        int divisionsU = _divisionsU;
        step = RhinoGet.GetInteger("Divisions in U (0 to go by spacing, or by the edges)", true, ref divisionsU, 0, 10000);
        if (step != Result.Success) return step;
        _divisionsU = divisionsU;

        int divisionsV = _divisionsV;
        step = RhinoGet.GetInteger("Divisions in V (0 to go by spacing, or by the edges)", true, ref divisionsV, 0, 10000);
        if (step != Result.Success) return step;
        _divisionsV = divisionsV;

        // Step 4: points to run grid lines through.
        step = Pick.SnapPoints(out Point3d[] snapPoints);
        if (step != Result.Success) return step;

        // Step 5: how deep. No default the first time: a depth is a design
        // decision, and one made up here would look exactly like one taken.
        double depth = _depth;
        step = RhinoGet.GetNumber(
            "Depth of the truss, between its two layers", _depth > 0.0, ref depth, RhinoMath.ZeroTolerance, 1e9);
        if (step != Result.Success) return step;
        _depth = depth;

        // Step 6: how the layers sit. Everything else is detail for the preview.
        step = Pick.Enum("Space truss type", ref _type);
        if (step != Result.Success) return step;

        return PreviewAndCommit(doc, surface, edges, snapPoints);
    }

    private static Result PreviewAndCommit(RhinoDoc doc, Brep? surface, Curve[] edges, Point3d[] snapPoints)
    {
        var conduit = new WireframePreviewConduit { Enabled = true };

        try
        {
            while (true)
            {
                SurfaceGrid grid;
                SpaceTruss truss;

                try
                {
                    var gridOptions = new SurfaceGridOptions
                    {
                        Pattern = _pattern,
                        DivisionsU = _divisionsU,
                        DivisionsV = _divisionsV,
                        SpacingU = _spacingU,
                        SpacingV = _spacingV,
                        SnapPoints = snapPoints,
                        Strictness = _strictness,
                        Diagonals = _diagonals,
                        Flip = _flip,
                        ClipToTrim = _clip,
                        Tolerance = doc.ModelAbsoluteTolerance,
                    };

                    grid = surface is not null
                        ? SurfaceGridGenerator.Generate(surface, gridOptions)
                        : SurfaceGridGenerator.Generate(edges, gridOptions);

                    truss = SpaceTrussGenerator.Generate(grid, new SpaceTrussOptions
                    {
                        Depth = _depth,
                        Type = _type,
                        Web = _web,
                        FlipWeb = _flipWeb,
                        GenerateEndPosts = _endPosts,
                        DepthAlong = _depthAlong,
                        FlipDepth = _flipDepth,
                    });
                }
                catch (ArgumentException ex)
                {
                    RhinoApp.WriteLine($"{EnglishNameText}: {ex.Message}");
                    return Result.Failure;
                }

                ShowPreview(conduit, truss);
                doc.Views.Redraw();

                // The grid's notes and then the truss's: the wording is the
                // domain's, so the two Grasshopper components say the same.
                foreach (FormNote note in grid.Notes.Concat(truss.Notes))
                    RhinoApp.WriteLine($"{EnglishNameText}: {note.Message}");

                using var getter = new GetOption();
                getter.SetCommandPrompt(
                    $"{Naming.Humanise(_type)} space truss, {grid.PanelsU} by {grid.PanelsV}, "
                    + $"{truss.Members.Count} members — accept?");

                int accept = getter.AddOption("Accept");
                int changeDepth = getter.AddOption("Depth");
                int changeType = getter.AddOption("Type");
                int changeWeb = getter.AddOption("Web");
                int changeFlipWeb = getter.AddOption("FlipWeb", _flipWeb ? "Yes" : "No");
                int changeEnds = getter.AddOption("EndPosts", _endPosts ? "Yes" : "No");
                int changeDepthAlong = getter.AddOption("DepthAlong");
                int changeFlipDepth = getter.AddOption("FlipDepth", _flipDepth ? "Yes" : "No");
                int changePattern = getter.AddOption("Pattern");
                int changeU = getter.AddOption("DivisionsU");
                int changeV = getter.AddOption("DivisionsV");
                int swap = getter.AddOption("SwapUV");
                int changeSpacingU = getter.AddOption("SpacingU");
                int changeSpacingV = getter.AddOption("SpacingV");
                int changeDiagonals = getter.AddOption("Diagonals");
                int changeFlip = getter.AddOption("Flip", _flip ? "Yes" : "No");
                int changeStrictness = getter.AddOption("Strictness");
                int changeClip = getter.AddOption("ClipToTrim", _clip ? "Yes" : "No");
                getter.AcceptNothing(true);   // Enter accepts

                GetResult result = getter.Get();

                if (result == GetResult.Nothing)
                    return Commit(doc, truss);

                if (result != GetResult.Option)
                    return getter.CommandResult();   // Esc discards everything

                int chosen = getter.Option().Index;

                if (chosen == accept)
                    return Commit(doc, truss);

                if (chosen == changeDepth)
                {
                    double depth = _depth;
                    if (RhinoGet.GetNumber("Depth of the truss", true, ref depth, RhinoMath.ZeroTolerance, 1e9) == Result.Success)
                        _depth = depth;
                }
                else if (chosen == changeType)
                {
                    Pick.Enum("Space truss type", ref _type);
                }
                else if (chosen == changeWeb)
                {
                    Pick.Enum("Web pattern of an aligned truss", ref _web);
                }
                else if (chosen == changeFlipWeb)
                {
                    _flipWeb = !_flipWeb;
                }
                else if (chosen == changeEnds)
                {
                    _endPosts = !_endPosts;
                }
                else if (chosen == changeDepthAlong)
                {
                    Pick.Enum("Measure the depth along", ref _depthAlong);
                }
                else if (chosen == changeFlipDepth)
                {
                    _flipDepth = !_flipDepth;
                }
                else if (chosen == changePattern)
                {
                    Pick.Enum("Grid pattern", ref _pattern);
                }
                else if (chosen == changeDiagonals)
                {
                    Pick.Enum("Diagonals of a triangulated grid", ref _diagonals);
                }
                else if (chosen == changeStrictness)
                {
                    Pick.Enum("Snap strictness", ref _strictness);
                }
                else if (chosen == changeFlip)
                {
                    _flip = !_flip;
                }
                else if (chosen == changeClip)
                {
                    _clip = !_clip;
                }
                else if (chosen == swap)
                {
                    (_divisionsU, _divisionsV) = (_divisionsV, _divisionsU);
                    (_spacingU, _spacingV) = (_spacingV, _spacingU);
                }
                else if (chosen == changeU)
                {
                    AskDivisions("Divisions in U", ref _divisionsU);
                }
                else if (chosen == changeV)
                {
                    AskDivisions("Divisions in V", ref _divisionsV);
                }
                else if (chosen == changeSpacingU)
                {
                    AskSpacing("Panel spacing in U", ref _spacingU, ref _divisionsU);
                }
                else if (chosen == changeSpacingV)
                {
                    AskSpacing("Panel spacing in V", ref _spacingV, ref _divisionsV);
                }
            }
        }
        finally
        {
            conduit.Enabled = false;
            doc.Views.Redraw();
        }
    }

    private static void AskDivisions(string prompt, ref int divisions)
    {
        int value = divisions;
        if (RhinoGet.GetInteger(prompt, true, ref value, 0, 10000) == Result.Success)
            divisions = value;
    }

    private static void AskSpacing(string prompt, ref double spacing, ref int divisions)
    {
        double value = spacing;
        if (RhinoGet.GetNumber(prompt, true, ref value, 0.0, 1e9) != Result.Success) return;

        spacing = value;

        // Divisions override spacing, so somebody who has just typed a spacing
        // would otherwise see nothing change.
        if (spacing > 0.0) divisions = 0;
    }

    private static void ShowPreview(WireframePreviewConduit conduit, SpaceTruss truss)
    {
        conduit.Clear();

        foreach (var (role, colour, thickness) in MemberLayers)
        {
            var lines = truss.MembersOf(role).ToArray();
            if (lines.Length > 0)
                conduit.Layers.Add((lines, colour, thickness));
        }

        conduit.Points = truss.UsedNodes;
        conduit.PointColour = NodeColour;
    }

    /// <summary>
    /// The nodes members meet at, split by layer: the first half of the index
    /// range is the top layer, the rest the bottom.
    /// </summary>
    private static (List<Point3d> Top, List<Point3d> Bottom) UsedNodesByLayer(SpaceTruss truss)
    {
        IReadOnlyList<Point3d> nodes = truss.Nodes;
        int topCount = truss.TopNodes.Count;

        var top = new List<Point3d>();
        var bottom = new List<Point3d>();

        foreach (int index in truss.Members.SelectMany(m => new[] { m.StartNode, m.EndNode }).Distinct().OrderBy(k => k))
            (index < topCount ? top : bottom).Add(nodes[index]);

        return (top, bottom);
    }

    /// <summary>
    /// Adds the truss to one layer tree, a sub-layer per role and one per
    /// layer of nodes. The surface or curves that were picked are left exactly
    /// as they are.
    /// </summary>
    private static Result Commit(RhinoDoc doc, SpaceTruss truss)
    {
        var byRole = MemberLayers
            .Select(m => (Name: m.Role.DisplayName(), m.Colour, Lines: truss.MembersOf(m.Role).ToList()))
            .Where(m => m.Lines.Count > 0)
            .ToList();

        if (byRole.Count == 0)
        {
            RhinoApp.WriteLine($"{EnglishNameText}: nothing to add.");
            return Result.Nothing;
        }

        string name = RunLayers.NextName(doc, LayerPrefix);

        int root = RunLayers.Add(doc, name, Guid.Empty, RootColour);
        if (root < 0)
        {
            RhinoApp.WriteLine($"{EnglishNameText}: could not create the layer {name}, so nothing was added.");
            return Result.Failure;
        }

        Guid rootId = doc.Layers[root].Id;
        int members = 0;

        foreach (var (layerName, colour, lines) in byRole)
        {
            int layer = RunLayers.Sub(doc, EnglishNameText, layerName, rootId, colour, root);

            foreach (Line line in lines)
                doc.Objects.AddLine(line, RunLayers.Attributes(layerName, layer));

            members += lines.Count;
        }

        (List<Point3d> topNodes, List<Point3d> bottomNodes) = UsedNodesByLayer(truss);
        int nodes = 0;

        foreach (var (layerName, points) in new[] { (TopNodeLayer, topNodes), (BottomNodeLayer, bottomNodes) })
        {
            int layer = RunLayers.Sub(doc, EnglishNameText, layerName, rootId, NodeColour, root);

            foreach (Point3d node in points)
            {
                doc.Objects.AddPoint(node, RunLayers.Attributes(layerName, layer));
                nodes++;
            }
        }

        doc.Views.Redraw();

        RhinoApp.WriteLine(
            $"{EnglishNameText}: added a {Naming.Humanise(_type).ToLowerInvariant()} space truss, "
            + $"{truss.Grid.PanelsU} by {truss.Grid.PanelsV}, to {name} — {members} members and {nodes} nodes, "
            + "split by section. What you picked was left as it is.");

        return Result.Success;
    }
}
