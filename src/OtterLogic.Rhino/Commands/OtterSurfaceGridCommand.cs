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
/// anything is added to the document. The settings and their prompts are
/// <see cref="GridSettings"/>, shared with OtterSpaceTruss.
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
    private static readonly GridSettings _grid = new();

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

        // Steps 2 and 3: the pattern, and how finely each way.
        step = _grid.AskUpFront();
        if (step != Result.Success) return step;

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
                    SurfaceGridOptions options = _grid.ToOptions(snapPoints, doc.ModelAbsoluteTolerance);

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
                    $"{Naming.Humanise(_grid.Pattern)}, {grid.PanelsU} by {grid.PanelsV}, "
                    + $"{grid.Members.Count} members — accept?");

                int accept = getter.AddOption("Accept");
                _grid.Offer(getter, snapPoints.Length > 0, grid.IsTrimmed);
                getter.AcceptNothing(true);   // Enter accepts

                GetResult result = getter.Get();

                if (result == GetResult.Nothing)
                    return Commit(doc, grid);

                if (result != GetResult.Option)
                    return getter.CommandResult();   // Esc discards everything

                int chosen = getter.Option().Index;

                if (chosen == accept)
                    return Commit(doc, grid);

                _grid.Handle(chosen);
            }
        }
        finally
        {
            conduit.Enabled = false;
            doc.Views.Redraw();
        }
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
            $"{EnglishNameText}: added a {grid.PanelsU} by {grid.PanelsV} {Naming.Humanise(_grid.Pattern).ToLowerInvariant()} "
            + $"grid to {name} — {members} members and {nodes.Count} nodes, split by section. "
            + "What you picked was left as it is.");

        return Result.Success;
    }
}
