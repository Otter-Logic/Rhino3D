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
/// Builds a triangular or box truss through a guided sequence of prompts.
/// <para>
/// The Rhino counterpart to the Box Truss Grasshopper component, and the 3D
/// sibling of OtterFlatTruss: the same questions in the same order, with one
/// more for the faces a flat truss does not have. Identical engine —
/// <see cref="BoxTrussGenerator.Generate"/>.
/// </para>
/// <para>
/// One truss per run, where OtterFlatTruss takes a whole bay of them. A flat
/// truss is two picks and pairs off by order; this is three or four, and
/// sorting a heap of chords into trusses would mean guessing which belong
/// together.
/// </para>
/// </summary>
public sealed class OtterBoxTrussCommand : Command
{
    // Remembered between runs within a session, as Rhino commands normally do.
    private static TrussType _type = TrussType.Warren;
    private static TrussType _lacing = TrussType.WarrenWithVerticals;
    private static bool _flip;
    private static bool _flipLacing;
    private static bool _endPosts = true;
    private static int _divisions;
    private static double _spacing;
    private static bool _onPlan;
    private static SnapStrictness _strictness = SnapStrictness.Relaxed;

    private const string EnglishNameText = "OtterBoxTruss";

    /// <summary>Root layer name, with the run number appended: OtterBoxTruss1, OtterBoxTruss2, ...</summary>
    private const string LayerPrefix = "OtterBoxTruss";

    private const string TopNodeLayer = "Top node";
    private const string BottomNodeLayer = "Bottom node";
    private static readonly Color NodeColour = Color.FromArgb(200, 60, 40);
    private static readonly Color RootColour = Color.FromArgb(60, 60, 65);

    /// <summary>
    /// One sub-layer per likely section group, as OtterFlatTruss has, in the
    /// same colours for the roles the two share — so a model holding both reads
    /// as one family.
    /// </summary>
    private static readonly (TrussMemberRole Role, Color Colour, int Thickness)[] MemberLayers =
    {
        (TrussMemberRole.TopChord,    Color.FromArgb( 25,  90, 150), 3),
        (TrussMemberRole.BottomChord, Color.FromArgb( 35, 130, 110), 3),
        (TrussMemberRole.Vertical,    Color.FromArgb(110,  90, 175), 2),
        (TrussMemberRole.Diagonal,    Color.FromArgb( 30,  90, 140), 2),
        (TrussMemberRole.EndPost,     Color.FromArgb(200, 110,  40), 3),
        (TrussMemberRole.Strut,       Color.FromArgb(150, 120,  60), 2),
        (TrussMemberRole.Lacing,      Color.FromArgb(120, 140,  70), 1),
    };

    public OtterBoxTrussCommand() => Instance = this;

    public static OtterBoxTrussCommand? Instance { get; private set; }

    public override string EnglishName => EnglishNameText;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // Steps 1 and 2: the chords. One or two of each; which shape comes out
        // follows from how many were picked, so it is never asked.
        Result step = Pick.Curves(doc, "Select one or two top chords, then press Enter", out Curve[] tops, 2);
        if (step != Result.Success) return step;

        step = Pick.Curves(doc, "Select one or two bottom chords, then press Enter", out Curve[] bottoms, 2);
        if (step != Result.Success) return step;

        if (tops.Length + bottoms.Length < 3)
        {
            RhinoApp.WriteLine(
                $"{EnglishNameText}: one top and one bottom chord is a flat truss — use OtterFlatTruss. "
                + "A box truss needs a second chord on at least one side.");

            return Result.Failure;
        }

        // Step 3: the side faces' bracing, then the lacing faces'.
        step = Pick.Enum("Side truss type", ref _type);
        if (step != Result.Success) return step;

        step = Pick.Enum("Lacing type, between twin chords", ref _lacing);
        if (step != Result.Success) return step;

        // Step 4: the division, asked exactly as OtterFlatTruss asks it.
        int divisions = _divisions;
        step = RhinoGet.GetInteger(
            "Number of divisions (0 to take nodes from the chords themselves)",
            true, ref divisions, 0, 10000);
        if (step != Result.Success) return step;
        _divisions = divisions;

        double spacing = _spacing;
        step = RhinoGet.GetNumber(
            "Panel spacing (0 to leave it to the divisions)", true, ref spacing, 0.0, 1e9);
        if (step != Result.Success) return step;
        _spacing = spacing;

        if (_divisions > 0 || _spacing > 0.0)
        {
            bool onPlan = _onPlan;
            step = RhinoGet.GetBool(
                "Measure the panels along the chords, or on plan as for a roof truss",
                true, "AlongChords", "OnPlan", ref onPlan);
            if (step != Result.Success) return step;
            _onPlan = onPlan;
        }

        // Step 5: snap points, and what the division owes them.
        step = Pick.SnapPoints(out Point3d[] snapPoints);
        if (step != Result.Success) return step;

        if (_divisions > 0 || _spacing > 0.0)
        {
            step = Pick.Enum("Snap strictness", ref _strictness);
            if (step != Result.Success) return step;
        }

        // Flips and end members are left to the preview, where what they do
        // can be seen: four faces make them hard to answer blind.
        return PreviewAndCommit(doc, tops, bottoms, snapPoints);
    }

    private static Result PreviewAndCommit(RhinoDoc doc, Curve[] tops, Curve[] bottoms, Point3d[] snapPoints)
    {
        var conduit = new WireframePreviewConduit { Enabled = true };

        try
        {
            while (true)
            {
                BoxTruss truss;

                try
                {
                    truss = BoxTrussGenerator.Generate(tops, bottoms, new BoxTrussOptions
                    {
                        Sides = new FlatTrussOptions
                        {
                            Type = _type,
                            Flip = _flip,
                            GenerateEndPosts = _endPosts,
                            Divisions = _divisions,
                            Spacing = _spacing,
                            MeasureOnPlan = _onPlan,
                            AdditionalSnapPoints = snapPoints,
                            Strictness = _strictness,
                            SnapTolerance = doc.ModelAbsoluteTolerance,
                        },
                        LacingType = _lacing,
                        FlipLacing = _flipLacing,
                    });
                }
                catch (ArgumentException ex)
                {
                    RhinoApp.WriteLine($"{EnglishNameText}: {ex.Message}");
                    return Result.Failure;
                }

                ShowPreview(conduit, truss);
                doc.Views.Redraw();

                foreach (FormNote note in truss.Notes)
                    RhinoApp.WriteLine($"{EnglishNameText}: {note.Message}");

                using var getter = new GetOption();
                getter.SetCommandPrompt(
                    $"{(truss.ChordCount == 3 ? "Triangular" : "Box")} truss, {Naming.Humanise(_type)} sides, "
                    + $"{truss.PanelCount} panels, {truss.Members.Count} members — accept?");

                int accept = getter.AddOption("Accept");
                int changeType = getter.AddOption("Type");
                int changeLacing = getter.AddOption("Lacing");
                int changeFlip = getter.AddOption("Flip");
                int changeFlipLacing = getter.AddOption("FlipLacing");
                int changeDivisions = getter.AddOption("Divisions");
                int changeSpacing = getter.AddOption("Spacing");
                int changeOnPlan = getter.AddOption("OnPlan", _onPlan ? "Yes" : "No");
                int changeStrictness = getter.AddOption("Strictness");
                int changeEnds = getter.AddOption("EndPosts");
                getter.AcceptNothing(true);   // Enter accepts

                GetResult result = getter.Get();

                if (result == GetResult.Nothing)
                    return Commit(doc, truss);

                if (result != GetResult.Option)
                    return getter.CommandResult();   // Esc discards everything

                int chosen = getter.Option().Index;

                if (chosen == accept)
                    return Commit(doc, truss);

                if (chosen == changeType)
                {
                    Pick.Enum("Side truss type", ref _type);
                }
                else if (chosen == changeLacing)
                {
                    Pick.Enum("Lacing type", ref _lacing);
                }
                else if (chosen == changeStrictness)
                {
                    Pick.Enum("Snap strictness", ref _strictness);
                }
                else if (chosen == changeFlip)
                {
                    _flip = !_flip;
                }
                else if (chosen == changeFlipLacing)
                {
                    _flipLacing = !_flipLacing;
                }
                else if (chosen == changeOnPlan)
                {
                    _onPlan = !_onPlan;
                }
                else if (chosen == changeEnds)
                {
                    _endPosts = !_endPosts;
                }
                else if (chosen == changeDivisions)
                {
                    int divisions = _divisions;
                    if (RhinoGet.GetInteger("Number of divisions", true, ref divisions, 0, 10000) == Result.Success)
                        _divisions = divisions;
                }
                else if (chosen == changeSpacing)
                {
                    double spacing = _spacing;
                    if (RhinoGet.GetNumber("Panel spacing", true, ref spacing, 0.0, 1e9) == Result.Success)
                        _spacing = spacing;
                }
            }
        }
        finally
        {
            conduit.Enabled = false;
            doc.Views.Redraw();
        }
    }

    private static void ShowPreview(WireframePreviewConduit conduit, BoxTruss truss)
    {
        conduit.Clear();

        // Same colours the layers get, so the preview reads as a rehearsal of
        // what lands in the document rather than a separate drawing of it.
        foreach (var (role, colour, thickness) in MemberLayers)
        {
            var lines = truss.MembersOf(role).ToArray();
            if (lines.Length > 0)
                conduit.Layers.Add((lines, colour, thickness));
        }

        conduit.Points = truss.DistinctNodes;
        conduit.PointColour = NodeColour;
    }

    /// <summary>
    /// Adds the truss to one layer tree, a sub-layer per role. The chord
    /// members are generated copies split at every node; the curves the user
    /// picked are left untouched underneath them.
    /// </summary>
    private static Result Commit(RhinoDoc doc, BoxTruss truss)
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

        int nodes = 0;

        foreach (var (layerName, points) in new[]
        {
            (TopNodeLayer, truss.TopNodes.SelectMany(chord => chord)),
            (BottomNodeLayer, truss.BottomNodes.SelectMany(chord => chord)),
        })
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
            $"{EnglishNameText}: added a {(truss.ChordCount == 3 ? "triangular" : "box")} truss to {name} — "
            + $"{members} members and {nodes} nodes, split by section. "
            + "The chord curves you picked were left as they are.");

        return Result.Success;
    }
}
