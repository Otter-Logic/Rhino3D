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
/// rather than a node: pick the chords, answer four questions, then adjust the
/// result against a live preview before anything is added to the document.
/// </para>
/// </summary>
public sealed class OtterTruss2DCommand : Command
{
    // Remembered between runs within a session, as Rhino commands normally do.
    private static TrussType _type = TrussType.Warren;
    private static bool _endPosts = true;
    private static double _spacing;

    public OtterTruss2DCommand() => Instance = this;

    public static OtterTruss2DCommand? Instance { get; private set; }

    public override string EnglishName => "OtterTruss2D";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // Step 1 and 2: the two chords.
        Result step = SelectCurve("Select the top chord", out Curve top);
        if (step != Result.Success) return step;

        step = SelectCurve("Select the bottom chord", out Curve bottom);
        if (step != Result.Success) return step;

        // Step 3: bracing pattern.
        step = SelectTrussType(ref _type);
        if (step != Result.Success) return step;

        // Step 4: end posts.
        bool endPosts = _endPosts;
        step = RhinoGet.GetBool("Generate end posts", true, "No", "Yes", ref endPosts);
        if (step != Result.Success) return step;
        _endPosts = endPosts;

        // Step 5: additional snap points.
        step = SelectSnapPoints(out Point3d[] snapPoints);
        if (step != Result.Success) return step;

        // Step 6: panel spacing.
        double spacing = _spacing;
        step = RhinoGet.GetNumber(
            "Panel spacing (0 to use only the points already on the chords)",
            true, ref spacing, 0.0, 1e9);
        if (step != Result.Success) return step;
        _spacing = spacing;

        // Step 7: preview, adjust, accept.
        return PreviewAndCommit(doc, top, bottom, snapPoints);
    }

    private static Result SelectCurve(string prompt, out Curve curve)
    {
        curve = null!;

        using var picker = new GetObject();
        picker.SetCommandPrompt(prompt);
        picker.GeometryFilter = ObjectType.Curve;
        picker.SubObjectSelect = false;
        picker.DeselectAllBeforePostSelect = false;
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
                        GenerateEndPosts = _endPosts,
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

                using var getter = new GetOption();
                getter.SetCommandPrompt(
                    $"{_type}, {truss.PanelCount} panels, {truss.Members.Count} members — accept?");

                int accept = getter.AddOption("Accept");
                int changeType = getter.AddOption("Type");
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
        conduit.Layers.Add((truss.TopChord.ToArray(), Color.FromArgb(30, 90, 140), 3));
        conduit.Layers.Add((truss.BottomChord.ToArray(), Color.FromArgb(30, 90, 140), 3));
        conduit.Layers.Add((truss.Web.ToArray(), Color.FromArgb(120, 170, 200), 2));
        conduit.Layers.Add((truss.EndPosts.ToArray(), Color.FromArgb(200, 110, 40), 3));
        conduit.Points = truss.Nodes;
    }

    /// <summary>Adds the members as one group, each named for its structural role.</summary>
    private static Result Commit(RhinoDoc doc, Truss2D truss)
    {
        int group = doc.Groups.Add();

        foreach (TrussMember member in truss.Members)
        {
            var attributes = new ObjectAttributes { Name = member.Role.ToString() };
            attributes.AddToGroup(group);
            doc.Objects.AddLine(member.Line, attributes);
        }

        doc.Views.Redraw();

        RhinoApp.WriteLine(
            $"OtterTruss2D: added {truss.Members.Count} members " +
            $"({truss.Type}, {truss.PanelCount} panels, {truss.TotalLength:0.###} units).");

        return Result.Success;
    }
}
