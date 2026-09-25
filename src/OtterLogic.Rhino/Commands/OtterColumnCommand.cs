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
/// Stands a column at every crossing of the gridlines you select.
/// <para>
/// The Rhino counterpart to the Grid Columns Grasshopper component. Identical
/// engine — <see cref="GridColumnsGenerator.Generate"/> — presented as a
/// walkthrough: pick the gridlines, see where they cross, give a base and a
/// top height, then adjust the result against a live preview before anything
/// is added to the document.
/// </para>
/// <para>
/// The crossings are shown before either height is asked for, because a wrong
/// selection — a gridline missed, a column swept up — is easier to see as a
/// grid of dots than as a forest of columns, and cheaper to fix before two
/// more questions have been answered.
/// </para>
/// </summary>
public sealed class OtterColumnCommand : Command
{
    // Remembered between runs within a session, as Rhino commands normally do.
    private static double _base;

    // Null until the first run, when it becomes three metres in the document's
    // units: a storey a user can accept or overtype, rather than zero and a
    // preview of nothing.
    private static double? _top;

    private const string EnglishNameText = "OtterColumn";

    /// <summary>Root layer name, with the run number appended: OtterColumn1, OtterColumn2, ...</summary>
    private const string LayerPrefix = "OtterColumn";

    // Named for what goes on it, in the word the component's port uses.
    private const string ColumnLayer = "Column";

    private static readonly Color RootColour = Color.FromArgb(60, 60, 65);
    private static readonly Color ColumnColour = Color.FromArgb(25, 90, 150);
    private static readonly Color CrossingColour = Color.FromArgb(200, 60, 40);
    private static readonly Color PlanColour = Color.FromArgb(150, 150, 160);

    public OtterColumnCommand() => Instance = this;

    public static OtterColumnCommand? Instance { get; private set; }

    public override string EnglishName => EnglishNameText;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        // Step 1: the grid.
        Result step = Pick.Curves(doc, "Select the gridlines, then press Enter", out Curve[] gridlines);
        if (step != Result.Success) return step;

        _top ??= 3.0 * RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);

        var conduit = new WireframePreviewConduit { Enabled = true };

        try
        {
            // Step 2: where they cross, shown flat, before any height is asked.
            // Equal heights place nothing, which is exactly the preview wanted here.
            GridColumns crossings;

            try
            {
                crossings = GridColumnsGenerator.Generate(gridlines, new GridColumnsOptions
                {
                    Base = 0.0,
                    Top = 0.0,
                    Tolerance = doc.ModelAbsoluteTolerance,
                });
            }
            catch (ArgumentException ex)
            {
                RhinoApp.WriteLine($"{EnglishNameText}: {ex.Message}");
                return Result.Failure;
            }

            ShowPreview(conduit, crossings);
            doc.Views.Redraw();

            // Only the warnings here: the remark that no columns were placed
            // is true, and beside the point until a height has been given.
            foreach (FormNote note in crossings.Notes)
                if (note.Level == FormNoteLevel.Warning)
                    RhinoApp.WriteLine($"{EnglishNameText}: {note.Message}");

            if (crossings.Crossings.Count == 0)
                return Result.Nothing;

            RhinoApp.WriteLine($"{EnglishNameText}: {Count(crossings.Crossings.Count, "crossing")} found.");

            // Step 3: the foot of the columns.
            double baseHeight = _base;
            step = RhinoGet.GetNumber("Base height", true, ref baseHeight, -1e9, 1e9);
            if (step != Result.Success) return step;
            _base = baseHeight;

            // Step 4: the head.
            double topHeight = _top.Value;
            step = RhinoGet.GetNumber("Top height", true, ref topHeight, -1e9, 1e9);
            if (step != Result.Success) return step;
            _top = topHeight;

            // Step 5: the columns, adjusted against the preview until accepted.
            return PreviewAndCommit(doc, conduit, gridlines);
        }
        finally
        {
            conduit.Enabled = false;
            doc.Views.Redraw();
        }
    }

    /// <summary>
    /// Draw the result, let the user keep tuning it against that preview, and
    /// add it to the document only on Accept. Nothing is committed until then,
    /// so Esc genuinely costs nothing.
    /// </summary>
    private static Result PreviewAndCommit(RhinoDoc doc, WireframePreviewConduit conduit, Curve[] gridlines)
    {
        while (true)
        {
            GridColumns columns;

            try
            {
                columns = GridColumnsGenerator.Generate(gridlines, new GridColumnsOptions
                {
                    Base = _base,
                    Top = _top!.Value,
                    Tolerance = doc.ModelAbsoluteTolerance,
                });
            }
            catch (ArgumentException ex)
            {
                RhinoApp.WriteLine($"{EnglishNameText}: {ex.Message}");
                return Result.Failure;
            }

            ShowPreview(conduit, columns);
            doc.Views.Redraw();

            foreach (FormNote note in columns.Notes)
                RhinoApp.WriteLine($"{EnglishNameText}: {note.Message}");

            using var getter = new GetOption();
            getter.SetCommandPrompt($"{Count(columns.Columns.Count, "column")} — accept?");

            int accept = getter.AddOption("Accept");
            int changeBase = getter.AddOption("Base", _base.ToString("0.###"));
            int changeTop = getter.AddOption("Top", _top.Value.ToString("0.###"));
            getter.AcceptNothing(true);   // Enter accepts

            GetResult result = getter.Get();

            if (result == GetResult.Nothing)
                return Commit(doc, columns);

            if (result != GetResult.Option)
                return getter.CommandResult();   // Esc discards everything

            int chosen = getter.Option().Index;

            if (chosen == accept)
                return Commit(doc, columns);

            if (chosen == changeBase)
            {
                double baseHeight = _base;
                if (RhinoGet.GetNumber("Base height", true, ref baseHeight, -1e9, 1e9) == Result.Success)
                    _base = baseHeight;
            }
            else if (chosen == changeTop)
            {
                double topHeight = _top.Value;
                if (RhinoGet.GetNumber("Top height", true, ref topHeight, -1e9, 1e9) == Result.Success)
                    _top = topHeight;
            }
        }
    }

    /// <summary>
    /// The flattened grid is drawn faintly under the columns, because it is
    /// the thing to check: the columns were read from it, not from the curves
    /// as drawn, and a gridline that sat at the wrong height still lands here.
    /// </summary>
    private static void ShowPreview(WireframePreviewConduit conduit, GridColumns columns)
    {
        conduit.Clear();

        conduit.Outlines.Add((columns.Plan, PlanColour, 1));
        conduit.Layers.Add((columns.Columns, ColumnColour, 2));

        conduit.Points = columns.Crossings;
        conduit.PointColour = CrossingColour;
    }

    /// <summary>
    /// Adds the columns to one layer tree for the run. The gridlines that were
    /// picked are left exactly as they are: they are the user's grid, and the
    /// columns are a separate thing that happens to stand on it.
    /// </summary>
    private static Result Commit(RhinoDoc doc, GridColumns columns)
    {
        if (columns.Columns.Count == 0)
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

        int columnLayer = RunLayers.Sub(doc, EnglishNameText, ColumnLayer, rootId, ColumnColour, root);
        foreach (Line column in columns.Columns)
            doc.Objects.AddLine(column, RunLayers.Attributes(ColumnLayer, columnLayer));

        doc.Views.Redraw();

        RhinoApp.WriteLine(
            $"{EnglishNameText}: added {Count(columns.Columns.Count, "column")} to {name}, "
            + $"from {_base:0.###} to {_top!.Value:0.###}. The gridlines you picked were left as they are.");

        return Result.Success;
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
