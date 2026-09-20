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
/// Fills the panels between a floor's primary beams with secondary members.
/// <para>
/// The Rhino counterpart to the Beam Infill Grasshopper component. Identical
/// engine — <see cref="BeamInfillGenerator.Generate"/> — presented as a
/// walkthrough: window-select the floor, say how it is divided, then adjust the
/// result against a live preview before anything is added to the document.
/// </para>
/// <para>
/// One selection, in no order, is the whole input. Working out which beams
/// bound which panel is the tedious part of doing this by hand, and asking the
/// user to say so panel by panel would hand it straight back to them.
/// </para>
/// </summary>
public sealed class OtterBeamInfillCommand : Command
{
    // Remembered between runs within a session, as Rhino commands normally do.
    private static int _divisions = 3;
    private static double _spacing;
    private static bool _flip;

    private const string EnglishNameText = "OtterBeamInfill";

    /// <summary>Root layer name, with the run number appended: OtterBeamInfill1, OtterBeamInfill2, ...</summary>
    private const string LayerPrefix = "OtterBeamInfill";

    // Named for what goes on them, in the words the component's ports use.
    private const string MemberLayer = "Secondary";
    private const string NodeLayer = "Node";

    private static readonly Color RootColour = Color.FromArgb(60, 60, 65);
    private static readonly Color MemberColour = Color.FromArgb(35, 130, 110);
    private static readonly Color NodeColour = Color.FromArgb(200, 60, 40);
    private static readonly Color PanelColour = Color.FromArgb(150, 150, 160);
    private static readonly Color SkippedColour = Color.FromArgb(220, 120, 30);

    public OtterBeamInfillCommand() => Instance = this;

    public static OtterBeamInfillCommand? Instance { get; private set; }

    public override string EnglishName => EnglishNameText;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // Step 1: the floor.
        Result step = Pick.Curves(
            doc, "Select the primary beams of one floor, then press Enter", out Curve[] beams);
        if (step != Result.Success) return step;

        // Step 2: how many bays per panel.
        int divisions = _divisions;
        step = RhinoGet.GetInteger(
            "Divisions per panel (0 to go by spacing instead)", true, ref divisions, 0, 10000);
        if (step != Result.Success) return step;
        _divisions = divisions;

        // Step 3: the same question by length, and only when it was not
        // already answered by count — divisions override it, so asking after
        // one was given would be asking for a number that does nothing.
        if (_divisions == 0)
        {
            double spacing = _spacing;
            step = RhinoGet.GetNumber("Member spacing", true, ref spacing, 0.0, 1e9);
            if (step != Result.Success) return step;
            _spacing = spacing;
        }

        // Direction is left to the preview: which way is long is easier to
        // see than to predict, and Flip is one click from there.
        return PreviewAndCommit(doc, beams);
    }

    /// <summary>
    /// Draw the result, let the user keep tuning it against that preview, and
    /// add it to the document only on Accept. Nothing is committed until then,
    /// so Esc genuinely costs nothing.
    /// </summary>
    private static Result PreviewAndCommit(RhinoDoc doc, Curve[] beams)
    {
        var conduit = new WireframePreviewConduit { Enabled = true };

        try
        {
            while (true)
            {
                BeamInfill infill;

                try
                {
                    infill = BeamInfillGenerator.Generate(beams, new BeamInfillOptions
                    {
                        Divisions = _divisions,
                        Spacing = _spacing,
                        Flip = _flip,
                        Tolerance = doc.ModelAbsoluteTolerance,
                    });
                }
                catch (ArgumentException ex)
                {
                    RhinoApp.WriteLine($"{EnglishNameText}: {ex.Message}");
                    return Result.Failure;
                }

                ShowPreview(conduit, infill);
                doc.Views.Redraw();

                foreach (FormNote note in infill.Notes)
                    RhinoApp.WriteLine($"{EnglishNameText}: {note.Message}");

                using var getter = new GetOption();
                getter.SetCommandPrompt(
                    $"{infill.Panels.Count} panels, {infill.Members.Count()} members — accept?");

                int accept = getter.AddOption("Accept");
                int changeDivisions = getter.AddOption("Divisions");
                int changeSpacing = getter.AddOption("Spacing");
                int changeFlip = getter.AddOption("Flip", _flip ? "ShortWay" : "LongWay");
                getter.AcceptNothing(true);   // Enter accepts

                GetResult result = getter.Get();

                if (result == GetResult.Nothing)
                    return Commit(doc, infill);

                if (result != GetResult.Option)
                    return getter.CommandResult();   // Esc discards everything

                int chosen = getter.Option().Index;

                if (chosen == accept)
                    return Commit(doc, infill);

                if (chosen == changeFlip)
                {
                    _flip = !_flip;
                }
                else if (chosen == changeDivisions)
                {
                    int divisions = _divisions;
                    if (RhinoGet.GetInteger("Divisions per panel", true, ref divisions, 0, 10000) == Result.Success)
                        _divisions = divisions;
                }
                else if (chosen == changeSpacing)
                {
                    double spacing = _spacing;
                    if (RhinoGet.GetNumber("Member spacing", true, ref spacing, 0.0, 1e9) == Result.Success)
                    {
                        _spacing = spacing;

                        // Divisions override spacing, so somebody who has just
                        // typed a spacing would otherwise see nothing change.
                        if (_spacing > 0.0) _divisions = 0;
                    }
                }
            }
        }
        finally
        {
            conduit.Enabled = false;
            doc.Views.Redraw();
        }
    }

    /// <summary>
    /// The panels are drawn as well as the members, faintly, because they are
    /// the thing to check: a bay missing its outline is a bay whose beams do
    /// not quite meet. The ones handed back unfilled are drawn loudly.
    /// </summary>
    private static void ShowPreview(WireframePreviewConduit conduit, BeamInfill infill)
    {
        conduit.Clear();

        conduit.Outlines.Add((infill.Panels.Select(p => p.Outline).ToArray(), PanelColour, 1));
        conduit.Outlines.Add((infill.SkippedPanels.ToArray(), SkippedColour, 3));
        conduit.Layers.Add((infill.Members.ToArray(), MemberColour, 2));

        conduit.Points = infill.Nodes;
        conduit.PointColour = NodeColour;
    }

    /// <summary>
    /// Adds the members and their nodes to one layer tree for the run.
    /// <para>
    /// The beams that were picked are left exactly as they are — not split at
    /// the new nodes, not moved onto these layers. They are the user's model;
    /// the nodes are there so that splitting them is a decision taken
    /// knowingly, with whatever tool the rest of the model is split by.
    /// </para>
    /// </summary>
    private static Result Commit(RhinoDoc doc, BeamInfill infill)
    {
        var members = infill.Members.ToList();

        if (members.Count == 0)
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

        int memberLayer = RunLayers.Sub(doc, EnglishNameText, MemberLayer, rootId, MemberColour, root);
        foreach (Line member in members)
            doc.Objects.AddLine(member, RunLayers.Attributes(MemberLayer, memberLayer));

        int nodeLayer = RunLayers.Sub(doc, EnglishNameText, NodeLayer, rootId, NodeColour, root);
        foreach (Point3d node in infill.Nodes)
            doc.Objects.AddPoint(node, RunLayers.Attributes(NodeLayer, nodeLayer));

        doc.Views.Redraw();

        RhinoApp.WriteLine(
            $"{EnglishNameText}: added {members.Count} members and {infill.Nodes.Count} nodes to {name}, "
            + $"across {infill.Panels.Count} panels. The beams you picked were left as they are.");

        return Result.Success;
    }
}
