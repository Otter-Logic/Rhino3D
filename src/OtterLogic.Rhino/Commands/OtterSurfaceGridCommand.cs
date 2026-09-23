using System.Drawing;
using OtterLogic.Core;
using OtterLogic.StructuralForm;
using OtterLogic.Rhino.Conduits;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace OtterLogic.Rhino.Commands;

/// <summary>
/// Lays a quad, triangulated or diagrid layout of members over a surface.
/// <para>
/// The Rhino counterpart to the Surface Grid Grasshopper component. Identical
/// engine — <see cref="SurfaceGridGenerator"/> — presented as a walkthrough:
/// pick what to grid, say how finely, then adjust against a live preview before
/// anything is added to the document.
/// </para>
/// <para>
/// One pick decides between the two ways in. A surface is gridded as it is;
/// two to four curves are taken as the outside of an area, for a stick model
/// that has no surfaces in it. Asking which was meant would be a question the
/// selection has already answered.
/// </para>
/// </summary>
public sealed class OtterSurfaceGridCommand : Command
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

    private const string EnglishNameText = "OtterSurfaceGrid";

    /// <summary>Root layer name, with the run number appended: OtterSurfaceGrid1, OtterSurfaceGrid2, ...</summary>
    private const string LayerPrefix = "OtterSurfaceGrid";

    private const string NodeLayer = "Node";
    private static readonly Color NodeColour = Color.FromArgb(200, 60, 40);
    private static readonly Color RootColour = Color.FromArgb(60, 60, 65);

    /// <summary>One sub-layer per likely section group, in the colours the preview uses.</summary>
    private static readonly (GridMemberRole Role, Color Colour, int Thickness)[] MemberLayers =
    {
        (GridMemberRole.U,        Color.FromArgb( 25,  90, 150), 2),
        (GridMemberRole.V,        Color.FromArgb( 35, 130, 110), 2),
        (GridMemberRole.Diagonal, Color.FromArgb(110,  90, 175), 2),
        (GridMemberRole.Edge,     Color.FromArgb(200, 110,  40), 3),
    };

    public OtterSurfaceGridCommand() => Instance = this;

    public static OtterSurfaceGridCommand? Instance { get; private set; }

    public override string EnglishName => EnglishNameText;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // Step 1: what to grid.
        Result step = SelectInput(doc, EnglishNameText, out Brep? surface, out Curve[] edges);
        if (step != Result.Success) return step;

        // Step 2: the pattern.
        step = Pick.Enum("Grid pattern", ref _pattern);
        if (step != Result.Success) return step;

        // Step 3: how finely, each way. Which way is U is not worth asking
        // about in advance — it shows in the preview, and the two numbers are
        // one option away from being swapped.
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

        return PreviewAndCommit(doc, surface, edges, snapPoints);
    }

    /// <summary>
    /// One surface, or two to four curves. Shared with OtterSpaceTruss, which
    /// starts from exactly the same pick and should refuse exactly the same
    /// mixtures in the same words.
    /// </summary>
    internal static Result SelectInput(RhinoDoc doc, string command, out Brep? surface, out Curve[] edges)
    {
        surface = null;
        edges = Array.Empty<Curve>();

        doc.Objects.UnselectAll();
        doc.Views.Redraw();

        using var picker = new GetObject();
        picker.SetCommandPrompt("Select one surface, or the two to four curves round an area");
        picker.GeometryFilter = ObjectType.Surface | ObjectType.PolysrfFilter | ObjectType.Curve;
        picker.SubObjectSelect = false;
        picker.EnablePreSelect(false, true);
        picker.DeselectAllBeforePostSelect = true;

        if (picker.GetMultiple(1, 4) != GetResult.Object)
            return picker.CommandResult();

        Brep[] breps = picker.Objects().Select(o => o.Brep()).OfType<Brep>().ToArray();
        edges = picker.Objects().Select(o => o.Curve()).OfType<Curve>().ToArray();

        if (breps.Length == 1 && edges.Length == 0)
        {
            surface = breps[0];
            return Result.Success;
        }

        if (breps.Length == 0 && edges.Length >= 2)
            return Result.Success;

        RhinoApp.WriteLine(
            $"{command}: pick either one surface, or two to four curves — not "
            + (breps.Length > 1 ? "several surfaces." : breps.Length == 1 ? "a mixture." : "a single curve."));

        return Result.Failure;
    }

    private static Result PreviewAndCommit(RhinoDoc doc, Brep? surface, Curve[] edges, Point3d[] snapPoints)
    {
        var conduit = new WireframePreviewConduit { Enabled = true };

        try
        {
            while (true)
            {
                SurfaceGrid grid;

                try
                {
                    var options = new SurfaceGridOptions
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
                        ? SurfaceGridGenerator.Generate(surface, options)
                        : SurfaceGridGenerator.Generate(edges, options);
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
                    $"{Naming.Humanise(_pattern)}, {grid.PanelsU} by {grid.PanelsV}, "
                    + $"{grid.Members.Count} members — accept?");

                int accept = getter.AddOption("Accept");
                int changePattern = getter.AddOption("Pattern");
                int changeU = getter.AddOption("DivisionsU");
                int changeV = getter.AddOption("DivisionsV");
                int swap = getter.AddOption("SwapUV");
                int changeSpacingU = getter.AddOption("SpacingU");
                int changeSpacingV = getter.AddOption("SpacingV");
                int changeDiagonals = getter.AddOption("Diagonals");
                int changeFlip = getter.AddOption("Flip");
                int changeStrictness = getter.AddOption("Strictness");
                int changeClip = getter.AddOption("ClipToTrim", _clip ? "Yes" : "No");
                getter.AcceptNothing(true);   // Enter accepts

                GetResult result = getter.Get();

                if (result == GetResult.Nothing)
                    return Commit(doc, grid);

                if (result != GetResult.Option)
                    return getter.CommandResult();   // Esc discards everything

                int chosen = getter.Option().Index;

                if (chosen == accept)
                    return Commit(doc, grid);

                if (chosen == changePattern)
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
                    // The numbers change places; the surface's directions are
                    // its own and stay where they are.
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

    private static void ShowPreview(WireframePreviewConduit conduit, SurfaceGrid grid)
    {
        conduit.Clear();

        foreach (var (role, colour, thickness) in MemberLayers)
        {
            var lines = grid.MembersOf(role).ToArray();
            if (lines.Length > 0)
                conduit.Layers.Add((lines, colour, thickness));
        }

        conduit.Points = grid.UsedNodes;
        conduit.PointColour = NodeColour;
    }

    /// <summary>
    /// Adds the grid to one layer tree, a sub-layer per role. The surface or
    /// curves that were picked are left exactly as they are.
    /// </summary>
    private static Result Commit(RhinoDoc doc, SurfaceGrid grid)
    {
        var byRole = MemberLayers
            .Select(m => (Name: m.Role.DisplayName(), m.Colour, Lines: grid.MembersOf(m.Role).ToList()))
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

        int nodeLayer = RunLayers.Sub(doc, EnglishNameText, NodeLayer, rootId, NodeColour, root);
        IReadOnlyList<Point3d> nodes = grid.UsedNodes;

        foreach (Point3d node in nodes)
            doc.Objects.AddPoint(node, RunLayers.Attributes(NodeLayer, nodeLayer));

        doc.Views.Redraw();

        RhinoApp.WriteLine(
            $"{EnglishNameText}: added a {grid.PanelsU} by {grid.PanelsV} {Naming.Humanise(_pattern).ToLowerInvariant()} "
            + $"grid to {name} — {members} members and {nodes.Count} nodes, split by section. "
            + "What you picked was left as it is.");

        return Result.Success;
    }
}
