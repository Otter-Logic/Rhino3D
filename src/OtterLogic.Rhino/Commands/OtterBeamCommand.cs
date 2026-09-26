using System.Drawing;
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
/// Runs primary beams along gridlines between the columns you select.
/// <para>
/// The Rhino counterpart to the Grid Beams Grasshopper component. Identical
/// engine, <see cref="GridBeamsGenerator.Generate"/>, presented as a
/// walkthrough: pick the columns, pick the gridlines, give a level or pick a
/// surface, then adjust the result against a live preview before anything is
/// added to the document.
/// </para>
/// <para>
/// The level is asked as a number with a Surface option on the same prompt,
/// rather than as two questions, because it is one question: where do the
/// beams go. Most floors are a height; a roof is a surface.
/// </para>
/// </summary>
public sealed class OtterBeamCommand : Command
{
    // Remembered between runs within a session, as Rhino commands normally do.
    // The surface is not: it is a document object picked for one run.
    private static double _level;
    private static double _reach;

    private const string EnglishNameText = "OtterBeam";

    /// <summary>Root layer name, with the run number appended: OtterBeam1, OtterBeam2, ...</summary>
    private const string LayerPrefix = "OtterBeam";

    // Named for what goes on them, in the words the component's ports use.
    private const string BeamLayer = "Beam";
    private const string NodeLayer = "Node";

    private static readonly Color RootColour = Color.FromArgb(60, 60, 65);
    private static readonly Color BeamColour = Color.FromArgb(25, 90, 150);
    private static readonly Color NodeColour = Color.FromArgb(200, 60, 40);
    private static readonly Color PlanColour = Color.FromArgb(150, 150, 160);

    public OtterBeamCommand() => Instance = this;

    public static OtterBeamCommand? Instance { get; private set; }

    public override string EnglishName => EnglishNameText;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // Step 1: the columns.
        Result step = Pick.Curves(doc, "Select the columns the beams connect into, then press Enter", out Curve[] columns);
        if (step != Result.Success) return step;

        // Step 2: the gridlines.
        step = Pick.Curves(doc, "Select the gridlines the beams run along, then press Enter", out Curve[] gridlines);
        if (step != Result.Success) return step;

        // Step 3: where the beams go, as a height or a surface.
        step = AskLevelOrSurface(doc, out Brep? surface);
        if (step != Result.Success) return step;

        var conduit = new WireframePreviewConduit { Enabled = true };

        try
        {
            // Step 4: the beams, adjusted against the preview until accepted.
            return PreviewAndCommit(doc, conduit, columns, gridlines, surface);
        }
        finally
        {
            conduit.Enabled = false;
            doc.Views.Redraw();
        }
    }

    /// <summary>
    /// A height, or the Surface option to pick a surface instead. Enter keeps
    /// the remembered height.
    /// </summary>
    private static Result AskLevelOrSurface(RhinoDoc doc, out Brep? surface)
    {
        surface = null;

        using var getter = new GetNumber();
        getter.SetCommandPrompt("Level of the beams as a height, or Surface to pick one for them to follow");
        getter.SetDefaultNumber(_level);
        int pickSurface = getter.AddOption("Surface");
        getter.AcceptNothing(true);

        GetResult result = getter.Get();

        if (result == GetResult.Nothing)
            return Result.Success;

        if (result == GetResult.Number)
        {
            _level = getter.Number();
            return Result.Success;
        }

        if (result == GetResult.Option && getter.OptionIndex() == pickSurface)
            return PickSurface(doc, out surface);

        return getter.CommandResult();
    }

    private static Result PickSurface(RhinoDoc doc, out Brep? surface)
    {
        surface = null;

        doc.Objects.UnselectAll();
        doc.Views.Redraw();

        using var picker = new GetObject();
        picker.SetCommandPrompt("Select the surface the beams follow");
        picker.GeometryFilter = ObjectType.Surface | ObjectType.PolysrfFilter;
        picker.SubObjectSelect = false;
        picker.EnablePreSelect(false, true);

        if (picker.Get() != GetResult.Object)
            return picker.CommandResult();

        surface = picker.Object(0).Brep();

        if (surface is null)
        {
            RhinoApp.WriteLine($"{EnglishNameText}: that object has no surface to project onto.");
            return Result.Failure;
        }

        return Result.Success;
    }

    /// <summary>
    /// Draw the result, let the user keep tuning it against that preview, and
    /// add it to the document only on Accept. Nothing is committed until then,
    /// so Esc genuinely costs nothing.
    /// </summary>
    private static Result PreviewAndCommit(RhinoDoc doc, WireframePreviewConduit conduit, Curve[] columns, Curve[] gridlines, Brep? surface)
    {
        while (true)
        {
            GridBeams beams;

            try
            {
                beams = GridBeamsGenerator.Generate(columns, gridlines, new GridBeamsOptions
                {
                    Level = _level,
                    Surface = surface,
                    Reach = _reach,
                    Tolerance = doc.ModelAbsoluteTolerance,
                });
            }
            catch (ArgumentException ex)
            {
                RhinoApp.WriteLine($"{EnglishNameText}: {ex.Message}");
                return Result.Failure;
            }

            ShowPreview(conduit, beams);
            doc.Views.Redraw();

            foreach (FormNote note in beams.Notes)
                RhinoApp.WriteLine($"{EnglishNameText}: {note.Message}");

            using var getter = new GetOption();
            getter.SetCommandPrompt(
                $"{Count(beams.Beams.Count, "beam")}, {Count(beams.Nodes.Count, "node")}"
                + (surface is null ? $" at {_level:0.###}" : " on the surface") + " — accept?");

            int accept = getter.AddOption("Accept");
            int changeLevel = getter.AddOption("Level", surface is null ? _level.ToString("0.###") : "Off");
            int changeSurface = getter.AddOption("Surface", surface is null ? "None" : "Picked");
            int changeReach = getter.AddOption("Reach", _reach > 0.0 ? _reach.ToString("0.###") : "Tolerance");
            getter.AcceptNothing(true);   // Enter accepts

            GetResult result = getter.Get();

            if (result == GetResult.Nothing)
                return Commit(doc, beams);

            if (result != GetResult.Option)
                return getter.CommandResult();   // Esc discards everything

            int chosen = getter.Option().Index;

            if (chosen == accept)
                return Commit(doc, beams);

            if (chosen == changeLevel)
            {
                // A level and a surface are two answers to one question, so
                // typing a level puts the surface aside.
                double level = _level;
                if (RhinoGet.GetNumber("Level of the beams", true, ref level, -1e9, 1e9) == Result.Success)
                {
                    _level = level;
                    surface = null;
                }
            }
            else if (chosen == changeSurface)
            {
                if (PickSurface(doc, out Brep? picked) == Result.Success)
                    surface = picked;
            }
            else if (chosen == changeReach)
            {
                double reach = _reach;
                if (RhinoGet.GetNumber("How far a column may sit off a gridline (0 for the document tolerance)", true, ref reach, 0.0, 1e9) == Result.Success)
                    _reach = reach;
            }
        }
    }

    /// <summary>
    /// The flattened grid is drawn faintly under the beams, because it is
    /// the thing to check when a beam is missing: the columns were looked
    /// for on it, not on the curves as drawn.
    /// </summary>
    private static void ShowPreview(WireframePreviewConduit conduit, GridBeams beams)
    {
        conduit.Clear();

        conduit.Outlines.Add((beams.Plan, PlanColour, 1));
        conduit.Outlines.Add((beams.Beams, BeamColour, 3));

        conduit.Points = beams.Nodes;
        conduit.PointColour = NodeColour;
    }

    /// <summary>
    /// Adds the beams and nodes to one layer tree for the run. The columns
    /// and gridlines that were picked are left exactly as they are.
    /// </summary>
    private static Result Commit(RhinoDoc doc, GridBeams beams)
    {
        if (beams.Beams.Count == 0)
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

        int beamLayer = RunLayers.Sub(doc, EnglishNameText, BeamLayer, rootId, BeamColour, root);
        foreach (Curve beam in beams.Beams)
            doc.Objects.AddCurve(beam, RunLayers.Attributes(BeamLayer, beamLayer));

        int nodeLayer = RunLayers.Sub(doc, EnglishNameText, NodeLayer, rootId, NodeColour, root);
        foreach (Point3d node in beams.Nodes)
            doc.Objects.AddPoint(node, RunLayers.Attributes(NodeLayer, nodeLayer));

        doc.Views.Redraw();

        RhinoApp.WriteLine(
            $"{EnglishNameText}: added {Count(beams.Beams.Count, "beam")} and {Count(beams.Nodes.Count, "node")} to {name}. "
            + "The columns and gridlines you picked were left as they are.");

        return Result.Success;
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
