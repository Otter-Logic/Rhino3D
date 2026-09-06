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
/// The Rhino counterpart to the Truss 2D Grasshopper component. Identical
/// engine — <see cref="Truss2DGenerator.Generate"/> — presented as a walkthrough
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
public sealed class OtterTruss2DCommand : Command
{
    // Remembered between runs within a session, as Rhino commands normally do.
    private static TrussType _type = TrussType.Warren;
    private static bool _flip;
    private static bool _endPosts = true;
    private static int _divisions;
    private static double _spacing;

    /// <summary>Root layer name, with the run number appended: OtterTruss1, OtterTruss2, ...</summary>
    private const string LayerPrefix = "OtterTruss";

    private const string NodeLayer = "Node";
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

    public OtterTruss2DCommand() => Instance = this;

    public static OtterTruss2DCommand? Instance { get; private set; }

    public override string EnglishName => "OtterTruss2D";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // Step 1: the top chords, in the order the trusses will be built.
        Result step = SelectCurves(doc, "Select the top chords, then press Enter", out Curve[] tops);
        if (step != Result.Success) return step;

        // Step 2: the bottom chords, picked in that same order.
        step = SelectCurves(
            doc, "Select the bottom chords in the same order, then press Enter", out Curve[] bottoms);
        if (step != Result.Success) return step;

        // Pairing is by pick order, so the counts have to line up. Nothing here
        // tries to work out which bottom chord belongs to which top one: order
        // is what the user chose, and second-guessing it would only make a
        // miscount harder to spot than starting again.
        if (bottoms.Length != tops.Length)
        {
            RhinoApp.WriteLine(
                "OtterTruss2D: the top and bottom chords do not match — "
                + $"{tops.Length} top, {bottoms.Length} bottom. "
                + "Run the command again and pick the same number of each, in the same order.");

            return Result.Failure;
        }

        // Step 3: bracing pattern.
        step = SelectTrussType(ref _type);
        if (step != Result.Success) return step;

        // Step 4: how many panels. Straight after the type, because the two of
        // them are the whole shape of the truss; everything below is detail.
        int divisions = _divisions;
        step = RhinoGet.GetInteger(
            "Number of divisions (0 to take nodes from the chords themselves)",
            true, ref divisions, 0, 10000);
        if (step != Result.Success) return step;
        _divisions = divisions;

        // Step 5: mirror the bracing.
        bool flip = _flip;
        step = RhinoGet.GetBool("Flip the bracing", true, "No", "Yes", ref flip);
        if (step != Result.Success) return step;
        _flip = flip;

        // Step 6: end posts.
        bool endPosts = _endPosts;
        step = RhinoGet.GetBool("Generate end posts", true, "No", "Yes", ref endPosts);
        if (step != Result.Success) return step;
        _endPosts = endPosts;

        // Step 7: additional snap points.
        step = SelectSnapPoints(out Point3d[] snapPoints);
        if (step != Result.Success) return step;

        // Step 8: panel spacing, for when you would rather set a length than a count.
        double spacing = _spacing;
        step = RhinoGet.GetNumber(
            "Panel spacing on plan (0 to leave it to the divisions)",
            true, ref spacing, 0.0, 1e9);
        if (step != Result.Success) return step;
        _spacing = spacing;

        // Step 9: preview, adjust, accept.
        var pairs = tops.Zip(bottoms, (top, bottom) => (Top: top, Bottom: bottom)).ToArray();

        return PreviewAndCommit(doc, pairs, snapPoints);
    }

    private static Result SelectCurves(RhinoDoc doc, string prompt, out Curve[] curves)
    {
        curves = Array.Empty<Curve>();

        // GetObject honours the current selection by default, so without this the
        // second call would silently return the curves just picked instead of
        // prompting for more. Clearing the selection as well keeps the
        // walkthrough readable: exactly one set is highlighted at a time.
        doc.Objects.UnselectAll();
        doc.Views.Redraw();

        using var picker = new GetObject();
        picker.SetCommandPrompt(prompt);
        picker.GeometryFilter = ObjectType.Curve;
        picker.SubObjectSelect = false;
        picker.EnablePreSelect(false, true);
        picker.DeselectAllBeforePostSelect = true;

        if (picker.GetMultiple(1, 0) != GetResult.Object)
            return picker.CommandResult();

        // OfType rather than a cast: the geometry filter already guarantees
        // curves, so this only sweeps up anything whose geometry failed to load.
        curves = picker.Objects().Select(o => o.Curve()).OfType<Curve>().ToArray();

        return Result.Success;
    }

    /// <summary>Offers the truss types as clickable command-line options.</summary>
    private static Result SelectTrussType(ref TrussType type)
    {
        var values = Enum.GetValues<TrussType>();

        using var getter = new GetOption();

        // The prompt reads the name the way Grasshopper's menu does; the options
        // themselves keep the bare enum name, because a command-line option
        // cannot contain a space.
        getter.SetCommandPrompt($"Truss type <{Naming.Humanise(type)}>");

        var indices = new int[values.Length];
        for (int i = 0; i < values.Length; i++)
            indices[i] = getter.AddOption(values[i].ToString());

        getter.AcceptNothing(true);   // Enter keeps the remembered type

        GetResult result = getter.Get();

        if (result == GetResult.Nothing)
            return Result.Success;

        if (result != GetResult.Option)
            return getter.CommandResult();

        int chosen = getter.Option().Index;
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] != chosen) continue;
            type = values[i];
            break;
        }

        return Result.Success;
    }

    private static Result SelectSnapPoints(out Point3d[] points)
    {
        points = Array.Empty<Point3d>();

        using var picker = new GetObject();
        picker.SetCommandPrompt("Select additional snap points, or press Enter for none");
        picker.GeometryFilter = ObjectType.Point;
        picker.SubObjectSelect = false;
        picker.AcceptNothing(true);
        picker.EnablePreSelect(false, true);

        GetResult result = picker.GetMultiple(0, 0);

        if (result == GetResult.Nothing)
            return Result.Success;

        if (result != GetResult.Object)
            return picker.CommandResult();

        points = picker.Objects()
            .Select(o => o.Point()?.Location)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .ToArray();

        return Result.Success;
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
                List<Truss2D> trusses;

                try
                {
                    trusses = pairs
                        .Select(pair => Truss2DGenerator.Generate(pair.Top, pair.Bottom, new Truss2DOptions
                        {
                            Type = _type,
                            Flip = _flip,
                            GenerateEndPosts = _endPosts,
                            Divisions = _divisions,
                            AdditionalSnapPoints = snapPoints,
                            SnapSpacing = _spacing,
                            SnapTolerance = doc.ModelAbsoluteTolerance,
                        }))
                        .ToList();
                }
                catch (ArgumentException ex)
                {
                    RhinoApp.WriteLine($"OtterTruss2D: {ex.Message}");
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
                    SelectTrussType(ref _type);
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

    private static string Summarise(IReadOnlyList<Truss2D> trusses)
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
    private static void ReportNotes(IReadOnlyList<Truss2D> trusses)
    {
        for (int i = 0; i < trusses.Count; i++)
            foreach (TrussNote note in trusses[i].Notes)
                RhinoApp.WriteLine(
                    trusses.Count == 1
                        ? $"OtterTruss2D: {note.Message}"
                        : $"OtterTruss2D: truss {i + 1} — {note.Message}");
    }

    private static void ShowPreview(WireframePreviewConduit conduit, IReadOnlyList<Truss2D> trusses)
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
    /// The name for this run's layer: OtterTruss1 the first time, then
    /// OtterTruss2, and so on.
    /// <para>
    /// One name per run, not per truss. Numbering follows the highest number
    /// already in the document rather than a count, so deleting OtterTruss2 does
    /// not make the next run reuse that name and merge into what is left of it.
    /// </para>
    /// </summary>
    private static string NextTrussLayerName(RhinoDoc doc)
    {
        int highest = 0;

        foreach (Layer layer in doc.Layers)
        {
            // Root layers only: a sub-layer called OtterTruss3 inside someone
            // else's tree is their business, not a run of this command.
            if (layer.IsDeleted || layer.ParentLayerId != Guid.Empty) continue;
            if (!layer.Name.StartsWith(LayerPrefix, StringComparison.OrdinalIgnoreCase)) continue;

            if (int.TryParse(layer.Name[LayerPrefix.Length..], out int number))
                highest = Math.Max(highest, number);
        }

        return $"{LayerPrefix}{highest + 1}";
    }

    /// <summary>Adds one layer, under <paramref name="parent"/> when given. -1 if it could not be created.</summary>
    private static int AddLayer(RhinoDoc doc, string name, Guid parent, Color colour)
        => doc.Layers.Add(new Layer { Name = name, Color = colour, ParentLayerId = parent });

    /// <summary>
    /// A sub-layer of the run's layer, falling back to the run's layer itself.
    /// <para>
    /// The fallback should never fire — the parent was created moments ago, so
    /// every name under it is free — but the alternative to checking is baking
    /// with <c>LayerIndex = -1</c>, which quietly puts geometry on a layer
    /// nobody chose. Landing one level up is findable; landing anywhere is not.
    /// </para>
    /// </summary>
    private static int SubLayer(RhinoDoc doc, string name, Guid parent, Color colour, int fallback)
    {
        int index = AddLayer(doc, name, parent, colour);
        if (index >= 0) return index;

        RhinoApp.WriteLine($"OtterTruss2D: could not create the {name} sub-layer, so those objects went one level up.");
        return fallback;
    }

    private static ObjectAttributes Attributes(string name, int layerIndex, int group)
    {
        var attributes = new ObjectAttributes { Name = name, LayerIndex = layerIndex };
        attributes.AddToGroup(group);
        return attributes;
    }

    /// <summary>
    /// Adds the run — every truss, chords included — to one layer tree, as one
    /// group.
    /// <para>
    /// Trusses raised together are one thing: a bay, sized and specified as a
    /// unit. So they share a group, and they share a sub-layer per role — the
    /// Top chord layer holds the top chord of every truss in the run, which is
    /// what makes assigning a section to it a single action rather than one per
    /// truss.
    /// </para>
    /// <para>
    /// The chord members are generated copies split at every node, which is what
    /// a section wants; the curves the user picked are left untouched underneath
    /// them.
    /// </para>
    /// </summary>
    private static Result Commit(RhinoDoc doc, IReadOnlyList<Truss2D> trusses)
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
            RhinoApp.WriteLine("OtterTruss2D: nothing to add.");
            return Result.Nothing;
        }

        string name = NextTrussLayerName(doc);

        int root = AddLayer(doc, name, Guid.Empty, RootColour);
        if (root < 0)
        {
            RhinoApp.WriteLine($"OtterTruss2D: could not create the layer {name}, so nothing was added.");
            return Result.Failure;
        }

        Guid rootId = doc.Layers[root].Id;

        // Named after the layer, so the two ways of finding this run agree.
        int group = doc.Groups.Add(name);
        if (group < 0) group = doc.Groups.Add();

        int members = 0;

        foreach (var (layerName, colour, lines) in byRole)
        {
            int layer = SubLayer(doc, layerName, rootId, colour, root);

            foreach (Line line in lines)
                doc.Objects.AddLine(line, Attributes(layerName, layer, group));

            members += lines.Count;
        }

        var nodes = trusses.SelectMany(t => t.DistinctNodes).ToList();
        int nodeLayer = SubLayer(doc, NodeLayer, rootId, NodeColour, root);

        foreach (Point3d node in nodes)
            doc.Objects.AddPoint(node, Attributes(NodeLayer, nodeLayer, group));

        doc.Views.Redraw();

        RhinoApp.WriteLine(
            $"OtterTruss2D: added {trusses.Count} {(trusses.Count == 1 ? "truss" : "trusses")} "
            + $"to {name} — {members} members and {nodes.Count} nodes, one group, split by section. "
            + "The chord curves you picked were left as they are.");

        return Result.Success;
    }
}
