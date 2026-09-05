using System.Drawing;
using OtterLogic.Core.StructuralForm;
using OtterLogic.Rhino.Conduits;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace OtterLogic.Rhino.Commands;

/// <summary>
/// Builds a 2D truss through a guided sequence of command-line prompts.
/// <para>
/// The Rhino counterpart to the Truss 2D Grasshopper component. Identical
/// engine — <see cref="Truss2DGenerator.Generate"/> — presented as a walkthrough
/// rather than a node: pick the two chords, answer the setup questions in turn,
/// then adjust the result against a live preview before anything is added to the
/// document.
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

    public OtterTruss2DCommand() => Instance = this;

    public static OtterTruss2DCommand? Instance { get; private set; }

    public override string EnglishName => "OtterTruss2D";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // Step 1 and 2: the two chords.
        Result step = SelectCurve(doc, "Select the top chord", out Curve top);
        if (step != Result.Success) return step;

        step = SelectCurve(doc, "Select the bottom chord", out Curve bottom);
        if (step != Result.Success) return step;

        // Step 3: bracing pattern.
        step = SelectTrussType(ref _type);
        if (step != Result.Success) return step;

        // Step 4: mirror the bracing.
        bool flip = _flip;
        step = RhinoGet.GetBool("Flip the bracing", true, "No", "Yes", ref flip);
        if (step != Result.Success) return step;
        _flip = flip;

        // Step 5: end posts.
        bool endPosts = _endPosts;
        step = RhinoGet.GetBool("Generate end posts", true, "No", "Yes", ref endPosts);
        if (step != Result.Success) return step;
        _endPosts = endPosts;

        // Step 6: how many panels, before anything snaps.
        int divisions = _divisions;
        step = RhinoGet.GetInteger(
            "Number of divisions (0 to take nodes from the chords themselves)",
            true, ref divisions, 0, 10000);
        if (step != Result.Success) return step;
        _divisions = divisions;

        // Step 7: additional snap points.
        step = SelectSnapPoints(out Point3d[] snapPoints);
        if (step != Result.Success) return step;

        // Step 8: panel spacing, for when you would rather set a length than a count.
        double spacing = _spacing;
        step = RhinoGet.GetNumber(
            "Panel spacing (0 to leave it to the divisions)",
            true, ref spacing, 0.0, 1e9);
        if (step != Result.Success) return step;
        _spacing = spacing;

        // Step 9: preview, adjust, accept.
        return PreviewAndCommit(doc, top, bottom, snapPoints);
    }

    private static Result SelectCurve(RhinoDoc doc, string prompt, out Curve curve)
    {
        curve = null!;

        // GetObject honours the current selection by default, so without this the
        // second call would silently return the curve just picked instead of
        // prompting for another one. Clearing the selection as well keeps the
        // walkthrough readable: exactly one thing is highlighted at a time.
        doc.Objects.UnselectAll();
        doc.Views.Redraw();

        using var picker = new GetObject();
        picker.SetCommandPrompt(prompt);
        picker.GeometryFilter = ObjectType.Curve;
        picker.SubObjectSelect = false;
        picker.EnablePreSelect(false, true);
        picker.DeselectAllBeforePostSelect = true;
        picker.Get();

        if (picker.CommandResult() != Result.Success)
            return picker.CommandResult();

        curve = picker.Object(0).Curve();

        if (curve is null)
        {
            RhinoApp.WriteLine("OtterTruss2D: that object could not be read as a curve.");
            return Result.Failure;
        }

        return Result.Success;
    }

    /// <summary>Offers the truss types as clickable command-line options.</summary>
    private static Result SelectTrussType(ref TrussType type)
    {
        var values = Enum.GetValues<TrussType>();

        using var getter = new GetOption();
        getter.SetCommandPrompt($"Truss type <{type}>");

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
    /// Draw the truss, let the user keep tuning it against that preview, and add
    /// it to the document only on Accept. Nothing is committed until then, so
    /// Esc genuinely costs nothing.
    /// </summary>
    private static Result PreviewAndCommit(RhinoDoc doc, Curve top, Curve bottom, Point3d[] snapPoints)
    {
        var conduit = new WireframePreviewConduit { Enabled = true };

        try
        {
            while (true)
            {
                Truss2D truss;

                try
                {
                    truss = Truss2DGenerator.Generate(top, bottom, new Truss2DOptions
                    {
                        Type = _type,
                        Flip = _flip,
                        GenerateEndPosts = _endPosts,
                        Divisions = _divisions,
                        AdditionalSnapPoints = snapPoints,
                        SnapSpacing = _spacing,
                        SnapTolerance = doc.ModelAbsoluteTolerance,
                    });
                }
                catch (ArgumentException ex)
                {
                    RhinoApp.WriteLine($"OtterTruss2D: {ex.Message}");
                    return Result.Failure;
                }

                ShowPreview(conduit, truss);
                doc.Views.Redraw();

                if (!truss.IsPlanar)
                    RhinoApp.WriteLine("OtterTruss2D: the chords are not coplanar, so this truss is warped.");

                if (_endPosts && (truss.ChordsMeetAtStart || truss.ChordsMeetAtEnd))
                    RhinoApp.WriteLine(
                        truss.ChordsMeetAtStart && truss.ChordsMeetAtEnd
                            ? "OtterTruss2D: the chords meet at both ends, so no end posts were generated."
                            : "OtterTruss2D: the chords meet at one end, so only one end post was generated.");

                using var getter = new GetOption();
                getter.SetCommandPrompt(
                    $"{_type}, {truss.PanelCount} panels, {Bracing(truss).Count} bracing members — accept?");

                int accept = getter.AddOption("Accept");
                int changeType = getter.AddOption("Type");
                int changeFlip = getter.AddOption("Flip");
                int changeDivisions = getter.AddOption("Divisions");
                int changeSpacing = getter.AddOption("Spacing");
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

    private static void ShowPreview(WireframePreviewConduit conduit, Truss2D truss)
    {
        conduit.Clear();

        // The chords are drawn thin and pale: they show where the stations landed,
        // but they are curves the user already drew and will not be added again.
        conduit.Layers.Add((truss.TopChord.ToArray(), Color.FromArgb(160, 175, 185), 1));
        conduit.Layers.Add((truss.BottomChord.ToArray(), Color.FromArgb(160, 175, 185), 1));

        conduit.Layers.Add((truss.Web.ToArray(), Color.FromArgb(30, 90, 140), 2));
        conduit.Layers.Add((truss.EndPosts.ToArray(), Color.FromArgb(200, 110, 40), 3));
        conduit.Points = truss.Nodes;
    }

    /// <summary>
    /// The members this command actually adds: web and end posts only.
    /// <para>
    /// The chords already exist — the user drew them and then picked them — so
    /// baking the generated copies would leave two curves on top of each other.
    /// The engine still produces them, and the Grasshopper component still
    /// outputs them, because there they are the only chords there are.
    /// </para>
    /// </summary>
    private static List<TrussMember> Bracing(Truss2D truss)
        => truss.Members
            .Where(m => m.Role is TrussMemberRole.Web or TrussMemberRole.EndPost)
            .ToList();

    /// <summary>Adds the bracing as one group, each member named for its role.</summary>
    private static Result Commit(RhinoDoc doc, Truss2D truss)
    {
        List<TrussMember> bracing = Bracing(truss);

        if (bracing.Count == 0)
        {
            RhinoApp.WriteLine("OtterTruss2D: nothing to add, this truss has no bracing.");
            return Result.Nothing;
        }

        int group = doc.Groups.Add();

        foreach (TrussMember member in bracing)
        {
            var attributes = new ObjectAttributes { Name = member.Role.ToString() };
            attributes.AddToGroup(group);
            doc.Objects.AddLine(member.Line, attributes);
        }

        doc.Views.Redraw();

        RhinoApp.WriteLine(
            $"OtterTruss2D: added {bracing.Count} bracing members " +
            $"({truss.Type}, {truss.PanelCount} panels). Your chords were left as they are.");

        return Result.Success;
    }
}
