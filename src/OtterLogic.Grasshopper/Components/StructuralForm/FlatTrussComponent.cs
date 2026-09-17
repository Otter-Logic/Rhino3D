using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using OtterLogic.Core;
using OtterLogic.StructuralForm;
using Rhino;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.StructuralForm;

/// <summary>
/// Builds a flat truss between two chords.
/// <para>
/// Adapter only. Every decision about where nodes land and which diagonals get
/// drawn belongs to <see cref="FlatTrussGenerator"/>, which the OtterFlatTruss Rhino
/// command calls in exactly the same way.
/// </para>
/// </summary>
public sealed class FlatTrussComponent : GH_Component
{
    public FlatTrussComponent()
        : base("Flat Truss", "FlatTruss",
               "Generate a flat truss between a top and a bottom chord.",
               Categories.Root, Categories.StructuralForm)
    {
    }

    public override Guid ComponentGuid => new("6a430957-4853-4f58-bd90-71a07fcf248a");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap? Icon => EmbeddedIcons.Load("flattruss", 24);

    /// <summary>
    /// Inputs in the order the truss is decided: the two chords, the bracing
    /// pattern, how it is divided, what that division has to respect, then how
    /// the ends are closed.
    /// <para>
    /// Divisions and Spacing sit together because they answer the same question
    /// two ways round, and Strictness follows the points because it decides
    /// what the division owes them.
    /// </para>
    /// </summary>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Top Chord", "T",
            "Top chord. Line, polyline or curve.", GH_ParamAccess.item);

        pManager.AddCurveParameter("Bottom Chord", "B",
            "Bottom chord. Reversed automatically if it runs against the top chord.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Type", "Ty",
            "Web bracing pattern. Wire a Truss Type dropdown in, or right-click for the same "
            + "list as a menu.", GH_ParamAccess.item, (int)TrussType.Warren);

        pManager.AddIntegerParameter("Divisions", "D",
            "Number of panels, set out evenly by plan distance. This is the primary control: it "
            + "sets how many verticals and diagonals there are, and snap points then move those "
            + "members rather than adding to them. Zero hands control to the geometry, where "
            + "every detected point becomes a node in its own right.",
            GH_ParamAccess.item, 0);

        pManager.AddNumberParameter("Spacing", "S",
            "Target panel width on plan, in model units — the secondary way to say Divisions, "
            + "for when you care about panel length rather than count. The span is divided by "
            + "this and rounded to whole panels. Divisions overrides it whenever both are set, "
            + "and zero from both leaves the panel count to the geometry.",
            GH_ParamAccess.item, 0.0);

        pManager.AddPointParameter("Snap Points", "P",
            "Extra points to put a node on. Each has to lie on the top or bottom chord — one "
            + "that does not is discounted, with a message saying so. Secondary to the polyline "
            + "vertices and curve kinks on the chords themselves, which are checked first and "
            + "take every node they can reach.",
            GH_ParamAccess.list);
        pManager[5].Optional = true;

        pManager.AddIntegerParameter("Strictness", "SS",
            "What happens to a Snap Point the even layout cannot reach. Relaxed keeps the "
            + "spacing regular and leaves such a point unused, saying so in a message. Strict "
            + "puts a node on every point and shares the panels out between them, growing the "
            + "count only if there are more points than panels. Right-click for the list.",
            GH_ParamAccess.item, (int)SnapStrictness.Relaxed);

        pManager.AddBooleanParameter("End Posts", "E",
            "Close the truss with a post at each end.", GH_ParamAccess.item, true);

        pManager.AddBooleanParameter("Flip", "F",
            "Mirror every diagonal within its own panel. Pratt becomes Howe, and the Warren "
            + "zigzag starts the other way up. No effect on Vierendeel or cross-braced.",
            GH_ParamAccess.item, false);

        // Right-click either enum input for a readable menu instead of raw
        // integers. Truss Type also has a dropdown to drop on the canvas; both
        // read the same choices, so they cannot come to disagree about what one
        // is called.
        var typeParam = (Param_Integer)pManager[2];
        foreach (var (label, value) in EnumChoices.Of<TrussType>())
            typeParam.AddNamedValue(label, value);

        var strictnessParam = (Param_Integer)pManager[6];
        foreach (var (label, value) in EnumChoices.Of<SnapStrictness>())
            strictnessParam.AddNamedValue(label, value);
    }

    /// <summary>
    /// One port per section group, in the same order and under the same names
    /// as the layers the OtterFlatTruss command bakes onto. Whatever sizes the top
    /// chord sizes all of it and nothing else, so a port feeds a section
    /// straight through with no sorting in between.
    /// <para>
    /// The nodes come out per chord rather than as one merged list, because the
    /// index is the useful thing about them: top <c>i</c> and bottom <c>i</c>
    /// are the pair at one station. Merged, that correspondence is gone and a
    /// downstream definition has to rediscover it by comparing coordinates.
    /// </para>
    /// </summary>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddLineParameter("Top Chord", "T", "Top chord members.", GH_ParamAccess.list);
        pManager.AddLineParameter("Bottom Chord", "B", "Bottom chord members.", GH_ParamAccess.list);
        pManager.AddLineParameter("Vertical", "V",
            "Web members running from a top node to the bottom node paired with it.",
            GH_ParamAccess.list);
        pManager.AddLineParameter("Diagonal", "D",
            "Web members running across a panel, from one chord to the other.",
            GH_ParamAccess.list);
        pManager.AddLineParameter("End Post", "E", "End posts.", GH_ParamAccess.list);
        pManager.AddPointParameter("Top Node", "TN",
            "Panel points on the top chord, one per station, running start to end.",
            GH_ParamAccess.list);
        pManager.AddPointParameter("Bottom Node", "BN",
            "Panel points on the bottom chord. Item i pairs with item i of Top Node — the two "
            + "ends of the vertical at that station — so the two lists can be zipped straight "
            + "into anything that wants the truss as a ladder. Where the chords meet, the pair "
            + "is one point in two places.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Curve? top = null;
        Curve? bottom = null;
        int type = (int)TrussType.Warren;
        int divisions = 0;
        double spacing = 0.0;
        var snapPoints = new List<GH_Point>();
        int strictness = (int)SnapStrictness.Relaxed;
        bool endPosts = true;
        bool flip = false;

        if (!da.GetData(0, ref top)) return;
        if (!da.GetData(1, ref bottom)) return;
        if (!da.GetData(2, ref type)) return;
        if (!da.GetData(3, ref divisions)) return;
        if (!da.GetData(4, ref spacing)) return;
        da.GetDataList(5, snapPoints);
        if (!da.GetData(6, ref strictness)) return;
        if (!da.GetData(7, ref endPosts)) return;
        if (!da.GetData(8, ref flip)) return;

        if (top is null || !top.IsValid || bottom is null || !bottom.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Both chords must be valid curves.");
            return;
        }

        // Type, Strictness, Divisions and Spacing are not checked here.
        // The generator validates its own options and throws ArgumentException
        // carrying the message to show, so a second copy of those rules on the
        // canvas would only be a second thing to keep in step with them.
        var options = new FlatTrussOptions
        {
            Type = (TrussType)type,
            GenerateEndPosts = endPosts,
            Flip = flip,
            Divisions = divisions,
            Spacing = spacing,
            AdditionalSnapPoints = snapPoints.Select(p => p.Value).ToArray(),
            Strictness = (SnapStrictness)strictness,
            SnapTolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01,
        };

        try
        {
            FlatTruss truss = FlatTrussGenerator.Generate(top, bottom, options);

            // The truss decides what is worth saying; the component only
            // decides how loudly to say it. The Rhino command reads the same list.
            foreach (TrussNote note in truss.Notes)
                AddRuntimeMessage(
                    note.Level == TrussNoteLevel.Warning
                        ? GH_RuntimeMessageLevel.Warning
                        : GH_RuntimeMessageLevel.Remark,
                    note.Message);

            da.SetDataList(0, truss.TopChord);
            da.SetDataList(1, truss.BottomChord);
            da.SetDataList(2, truss.Verticals);
            da.SetDataList(3, truss.Diagonals);
            da.SetDataList(4, truss.EndPosts);
            da.SetDataList(5, truss.TopNodes);
            da.SetDataList(6, truss.BottomNodes);

            Message = $"{Naming.Humanise(truss.Type)}\n{truss.PanelCount} panels";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
