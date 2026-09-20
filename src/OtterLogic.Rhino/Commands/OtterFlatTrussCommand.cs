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
/// Builds 2D trusses through a guided sequence of command-line prompts.
/// <para>
/// The Rhino counterpart to the Flat Truss Grasshopper component. Identical
/// engine — <see cref="FlatTrussGenerator.Generate"/> — presented as a walkthrough
/// rather than a node: pick the chords, answer the setup questions in turn, then
/// adjust the result against a live preview before anything is added to the
/// document.
/// </para>
/// <para>
/// Chords are picked in sets rather than one at a time, because a bay of
/// parallel trusses is the normal case and running the command five times over
/// to answer the same five questions is not. Top chord <c>i</c> is paired with
/// bottom chord <c>i</c>, so the order they are picked in is the order the
/// trusses come out in.
/// </para>
/// </summary>
public sealed class OtterFlatTrussCommand : Command
{
    // Remembered between runs within a session, as Rhino commands normally do.
    private static TrussType _type = TrussType.Warren;
    private static bool _flip;
    private static bool _endPosts = true;
    private static int _divisions;
    private static double _spacing;
    private static bool _onPlan;
    private static SnapStrictness _strictness = SnapStrictness.Relaxed;

    /// <summary>Root layer name, with the run number appended: OtterFlatTruss1, OtterFlatTruss2, ...</summary>
    private const string LayerPrefix = "OtterFlatTruss";

    // Per chord, matching the component's ports: the pairing by index is what
    // makes the nodes worth having, and one merged layer throws it away.
    private const string TopNodeLayer = "Top node";
    private const string BottomNodeLayer = "Bottom node";
    private static readonly Color NodeColour = Color.FromArgb(200, 60, 40);
    private static readonly Color RootColour = Color.FromArgb(60, 60, 65);

    /// <summary>
    /// The sub-layers a truss is baked onto, in the order they are created,
    /// each with the colour and weight it also carries in the preview.
    /// <para>
    /// One layer per likely section group: whatever sizes the top chord sizes
    /// all of it and nothing else. That is what lets an analysis or scheduling
    /// tool downstream pick the members up by layer, with no re-sorting and no
    /// guessing at a member's role from its direction. The names come from the
    /// domain, so the Grasshopper output ports read the same.
    /// </para>
    /// </summary>
    private static readonly (TrussMemberRole Role, Color Colour, int Thickness)[] MemberLayers =
    {
        (TrussMemberRole.TopChord,    Color.FromArgb( 25,  90, 150), 3),
        (TrussMemberRole.BottomChord, Color.FromArgb( 35, 130, 110), 3),
        (TrussMemberRole.Vertical,    Color.FromArgb(110,  90, 175), 2),
        (TrussMemberRole.Diagonal,    Color.FromArgb( 30,  90, 140), 2),
        (TrussMemberRole.EndPost,     Color.FromArgb(200, 110,  40), 3),
    };

    public OtterFlatTrussCommand() => Instance = this;

    public static OtterFlatTrussCommand? Instance { get; private set; }

    private const string EnglishNameText = "OtterFlatTruss";

    public override string EnglishName => EnglishNameText;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // Step 1: the top chords, in the order the trusses will be built.
        Result step = Pick.Curves(doc, "Select the top chords, then press Enter", out Curve[] tops);
        if (step != Result.Success) return step;

        // Step 2: the bottom chords, picked in that same order.
        step = Pick.Curves(
            doc, "Select the bottom chords in the same order, then press Enter", out Curve[] bottoms);
        if (step != Result.Success) return step;

        // Pairing is by pick order, so the counts have to line up. Nothing here
        // tries to work out which bottom chord belongs to which top one: order
        // is what the user chose, and second-guessing it would only make a
        // miscount harder to spot than starting again.
        if (bottoms.Length != tops.Length)
        {
            RhinoApp.WriteLine(
                "OtterFlatTruss: the top and bottom chords do not match — "
                + $"{tops.Length} top, {bottoms.Length} bottom. "
                + "Run the command again and pick the same number of each, in the same order.");

            return Result.Failure;
        }

        // Step 3: bracing pattern.
        step = Pick.Enum("Truss type", ref _type);
        if (step != Result.Success) return step;

        // Step 4: how many panels. Straight after the type, because the two of
        // them are the whole shape of the truss; everything below is detail.
        int divisions = _divisions;
        step = RhinoGet.GetInteger(
            "Number of divisions (0 to take nodes from the chords themselves)",
            true, ref divisions, 0, 10000);
        if (step != Result.Success) return step;
        _divisions = divisions;

        // Step 5: the same question by length rather than count, asked here
        // because it is the one the division falls back on.
        double spacing = _spacing;
        step = RhinoGet.GetNumber(
            "Panel spacing (0 to leave it to the divisions)",
            true, ref spacing, 0.0, 1e9);
        if (step != Result.Success) return step;
        _spacing = spacing;

        // What those two are measured along. Only asked when one of them is
        // set: with the nodes taken from the chords there is nothing to measure.
        if (_divisions > 0 || _spacing > 0.0)
        {
            bool onPlan = _onPlan;
            step = RhinoGet.GetBool(
                "Measure the panels along the chords, or on plan as for a roof truss",
                true, "AlongChords", "OnPlan", ref onPlan);
            if (step != Result.Success) return step;
            _onPlan = onPlan;
        }

        // Step 6: additional snap points. The picker already filters to points,
        // and the generator discounts any that are not on a chord.
        step = Pick.SnapPoints(out Point3d[] snapPoints);
        if (step != Result.Success) return step;

        // Step 7: what the division owes the snap points. Only worth asking
        // when there is a division for them to argue with - with the panel
        // count left to the geometry every point is a node already.
        if (_divisions > 0 || _spacing > 0.0)
        {
            step = Pick.Enum("Snap strictness", ref _strictness);
            if (step != Result.Success) return step;
        }

        // Step 8: mirror the bracing.
        bool flip = _flip;
        step = RhinoGet.GetBool("Flip the bracing", true, "No", "Yes", ref flip);
        if (step != Result.Success) return step;
        _flip = flip;

        // Step 9: end posts.
        bool endPosts = _endPosts;
        step = RhinoGet.GetBool("Generate end posts", true, "No", "Yes", ref endPosts);
        if (step != Result.Success) return step;
        _endPosts = endPosts;

        // Step 10: preview, adjust, accept.
        var pairs = tops.Zip(bottoms, (top, bottom) => (Top: top, Bottom: bottom)).ToArray();

        return PreviewAndCommit(doc, pairs, snapPoints);
    }

    /// <summary>
    /// Draw the trusses, let the user keep tuning them against that preview, and
    /// add them to the document only on Accept. Nothing is committed until then,
    /// so Esc genuinely costs nothing.
    /// <para>
    /// Every setting applies to the whole set: one bay of trusses is one design
    /// decision, and tuning them apart from each other is what the Grasshopper
    /// component is for.
    /// </para>
    /// </summary>
    private static Result PreviewAndCommit(
        RhinoDoc doc, (Curve Top, Curve Bottom)[] pairs, Point3d[] snapPoints)
    {
        var conduit = new WireframePreviewConduit { Enabled = true };

        try
        {
            while (true)
            {
                List<FlatTruss> trusses;

                try
                {
                    trusses = pairs
                        .Select(pair => FlatTrussGenerator.Generate(pair.Top, pair.Bottom, new FlatTrussOptions
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
                        }))
                        .ToList();
                }
                catch (ArgumentException ex)
                {
                    RhinoApp.WriteLine($"OtterFlatTruss: {ex.Message}");
                    return Result.Failure;
                }

                ShowPreview(conduit, trusses);
                doc.Views.Redraw();
                ReportNotes(trusses);

                using var getter = new GetOption();
                getter.SetCommandPrompt($"{Summarise(trusses)} — accept?");

                int accept = getter.AddOption("Accept");
                int changeType = getter.AddOption("Type");
                int changeFlip = getter.AddOption("Flip");
                int changeDivisions = getter.AddOption("Divisions");
                int changeSpacing = getter.AddOption("Spacing");
                int changeOnPlan = getter.AddOption("OnPlan", _onPlan ? "Yes" : "No");
                int changeStrictness = getter.AddOption("Strictness");
                int changeEnds = getter.AddOption("EndPosts");
                getter.AcceptNothing(true);   // Enter accepts

                GetResult result = getter.Get();

                if (result == GetResult.Nothing)
                    return Commit(doc, trusses);

                if (result != GetResult.Option)
                    return getter.CommandResult();   // Esc discards everything

                int chosen = getter.Option().Index;

                if (chosen == accept)
                    return Commit(doc, trusses);

                if (chosen == changeType)
                {
                    Pick.Enum("Truss type", ref _type);
                }
                else if (chosen == changeStrictness)
                {
                    Pick.Enum("Snap strictness", ref _strictness);
                }
                else if (chosen == changeFlip)
                {
                    _flip = !_flip;
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
                else if (chosen == changeOnPlan)
                {
                    _onPlan = !_onPlan;
                }
                else if (chosen == changeEnds)
                {
                    _endPosts = !_endPosts;
                }
            }
        }
        finally
        {
            conduit.Enabled = false;
            doc.Views.Redraw();
        }
    }

    private static string Summarise(IReadOnlyList<FlatTruss> trusses)
    {
        string type = Naming.Humanise(_type);
        int members = trusses.Sum(t => t.Members.Count);

        return trusses.Count == 1
            ? $"{type}, {trusses[0].PanelCount} panels, {members} members"
            : $"{type}, {trusses.Count} trusses, {members} members";
    }

    /// <summary>
    /// Repeat what each truss has to say about itself. The wording is the
    /// domain's, so the Grasshopper component says exactly the same things in
    /// its own bubbles; only the truss number is this front-end's addition.
    /// </summary>
    private static void ReportNotes(IReadOnlyList<FlatTruss> trusses)
    {
        for (int i = 0; i < trusses.Count; i++)
            foreach (FormNote note in trusses[i].Notes)
                RhinoApp.WriteLine(
                    trusses.Count == 1
                        ? $"OtterFlatTruss: {note.Message}"
                        : $"OtterFlatTruss: truss {i + 1} — {note.Message}");
    }

    private static void ShowPreview(WireframePreviewConduit conduit, IReadOnlyList<FlatTruss> trusses)
    {
        conduit.Clear();

        // Same colours the layers get, so the preview reads as a rehearsal of
        // what lands in the document rather than a separate drawing of it.
        foreach (var (role, colour, thickness) in MemberLayers)
        {
            var lines = trusses.SelectMany(t => t.MembersOf(role)).ToArray();
            if (lines.Length > 0)
                conduit.Layers.Add((lines, colour, thickness));
        }

        conduit.Points = trusses.SelectMany(t => t.DistinctNodes).ToArray();
        conduit.PointColour = NodeColour;
    }

    /// <summary>
    /// Adds the run — every truss, chords included — to one layer tree.
    /// <para>
    /// Trusses raised together are one thing: a bay, sized and specified as a
    /// unit. So they share a sub-layer per role — the Top chord layer holds the
    /// top chord of every truss in the run, which is what makes assigning a
    /// section to it a single action rather than one per truss.
    /// </para>
    /// <para>
    /// The layer tree is the whole of that organisation. Nothing is grouped:
    /// a group over geometry already sorted into named sub-layers only adds a
    /// second thing to select through and gets in the way of picking a single
    /// member.
    /// </para>
    /// <para>
    /// The chord members are generated copies split at every node, which is what
    /// a section wants; the curves the user picked are left untouched underneath
    /// them.
    /// </para>
    /// </summary>
    private static Result Commit(RhinoDoc doc, IReadOnlyList<FlatTruss> trusses)
    {
        // Gathered before anything is created, so a run with nothing in it
        // leaves no empty layers behind. Roles with no members are dropped here
        // too: a Vierendeel has no diagonals to put anywhere.
        var byRole = MemberLayers
            .Select(m => (Name: m.Role.DisplayName(), m.Colour,
                          Lines: trusses.SelectMany(t => t.MembersOf(m.Role)).ToList()))
            .Where(m => m.Lines.Count > 0)
            .ToList();

        if (byRole.Count == 0)
        {
            RhinoApp.WriteLine("OtterFlatTruss: nothing to add.");
            return Result.Nothing;
        }

        string name = RunLayers.NextName(doc, LayerPrefix);

        int root = RunLayers.Add(doc, name, Guid.Empty, RootColour);
        if (root < 0)
        {
            RhinoApp.WriteLine($"OtterFlatTruss: could not create the layer {name}, so nothing was added.");
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
            (TopNodeLayer, trusses.SelectMany(t => t.TopNodes)),
            (BottomNodeLayer, trusses.SelectMany(t => t.BottomNodes)),
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
            $"OtterFlatTruss: added {trusses.Count} {(trusses.Count == 1 ? "truss" : "trusses")} "
            + $"to {name} — {members} members and {nodes} nodes, split by section. "
            + "The chord curves you picked were left as they are.");

        return Result.Success;
    }
}
