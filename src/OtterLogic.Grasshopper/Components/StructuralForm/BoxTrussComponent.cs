using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using OtterLogic.Core;
using OtterLogic.StructuralForm;
using Rhino;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.StructuralForm;

/// <summary>
/// Builds a triangular or box truss on three or four chords.
/// <para>
/// Adapter only. Every decision about where nodes land and which members get
/// drawn belongs to <see cref="BoxTrussGenerator"/>, which the OtterBoxTruss
/// Rhino command calls in exactly the same way.
/// </para>
/// </summary>
public sealed class BoxTrussComponent : GH_Component
{
    public BoxTrussComponent()
        : base("Box Truss", "BoxTruss",
               "Generate a 3D truss on one or two top chords and one or two bottom chords: two "
               + "and one for a triangular truss, two and two for a box.\n\n"
               + "Divided, snapped and braced exactly as Flat Truss is, with one more pattern for "
               + "the lacing between twin chords. For a single top and a single bottom chord, use "
               + "Flat Truss.",
               Categories.Root, Categories.StructuralForm)
    {
    }

    public override Guid ComponentGuid => new("51e90ece-6cc8-4a43-9597-4c4e9c37690d");
    public override GH_Exposure Exposure => GH_Exposure.tertiary;
    protected override Bitmap? Icon => EmbeddedIcons.Load("boxtruss", 24);

    /// <summary>
    /// Flat Truss's inputs under Flat Truss's names, so that knowing one is
    /// knowing the other; the two that are new sit beside the input they echo.
    /// </summary>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Top Chords", "T",
            "One or two top chords. Lines, polylines or curves.", GH_ParamAccess.list);

        pManager.AddCurveParameter("Bottom Chords", "B",
            "One or two bottom chords, in any order and drawn in either direction: each is "
            + "matched to the top chord it sits under and turned to run the same way.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Type", "Ty",
            "Web bracing pattern of the side faces, top chord to bottom chord. Wire a Truss Type "
            + "dropdown in, or right-click for the same list as a menu.",
            GH_ParamAccess.item, (int)TrussType.Warren);

        pManager.AddIntegerParameter("Lacing", "L",
            "Bracing pattern of the lacing faces, between the two top chords and between the two "
            + "bottom ones. The same patterns, with struts where a side face has verticals; "
            + "Vierendeel gives struts alone.",
            GH_ParamAccess.item, (int)TrussType.WarrenWithVerticals);

        pManager.AddIntegerParameter("Divisions", "D",
            "Number of panels, set out evenly along the chords — or on plan, under On Plan. Every "
            + "chord is divided at the same stations. Zero hands control to the geometry, where "
            + "every detected point becomes a panel point.",
            GH_ParamAccess.item, 0);

        pManager.AddNumberParameter("Spacing", "S",
            "Target panel width, in model units — the secondary way to say Divisions. Divisions "
            + "overrides it whenever both are set.",
            GH_ParamAccess.item, 0.0);

        pManager.AddPointParameter("Snap Points", "P",
            "Extra points to put a panel point on. Each has to lie on one of the chords.",
            GH_ParamAccess.list);
        pManager[6].Optional = true;

        pManager.AddIntegerParameter("Strictness", "SS",
            "What happens to a Snap Point the even layout cannot reach: Relaxed leaves it unused "
            + "and says so, Strict puts a panel point on it. Right-click for the list.",
            GH_ParamAccess.item, (int)SnapStrictness.Relaxed);

        pManager.AddBooleanParameter("End Posts", "E",
            "Close every face at its ends: a post on each side face, a strut on each lacing face.",
            GH_ParamAccess.item, true);

        pManager.AddBooleanParameter("Flip", "F",
            "Mirror every side-face diagonal within its own panel.", GH_ParamAccess.item, false);

        pManager.AddBooleanParameter("Flip Lacing", "FL",
            "Mirror every lacing diagonal within its own panel.", GH_ParamAccess.item, false);

        pManager.AddBooleanParameter("On Plan", "OP",
            "Measure Divisions and Spacing on plan instead of along the chords. Turn it on for a "
            + "roof truss whose chords pitch or curve differently from one another.",
            GH_ParamAccess.item, false);

        foreach (int input in new[] { 2, 3 })
        {
            var param = (Param_Integer)pManager[input];
            foreach (var (label, value) in EnumChoices.Of<TrussType>())
                param.AddNamedValue(label, value);
        }

        var strictnessParam = (Param_Integer)pManager[7];
        foreach (var (label, value) in EnumChoices.Of<SnapStrictness>())
            strictnessParam.AddNamedValue(label, value);
    }

    /// <summary>
    /// One port per section group, under the names the OtterBoxTruss command
    /// gives its layers. The nodes come out as trees with a branch per chord,
    /// because item <c>i</c> of every branch is the same cross-section through
    /// the truss, and that correspondence is the useful thing about them.
    /// </summary>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddLineParameter("Top Chord", "T", "Top chord members.", GH_ParamAccess.list);
        pManager.AddLineParameter("Bottom Chord", "B", "Bottom chord members.", GH_ParamAccess.list);
        pManager.AddLineParameter("Vertical", "V",
            "Side-face members from a top node to the bottom node paired with it.", GH_ParamAccess.list);
        pManager.AddLineParameter("Diagonal", "D",
            "Side-face members running across a panel.", GH_ParamAccess.list);
        pManager.AddLineParameter("End Post", "E", "Posts closing the side faces.", GH_ParamAccess.list);
        pManager.AddLineParameter("Strut", "St",
            "Lacing-face members square across, between twin chords at one station.",
            GH_ParamAccess.list);
        pManager.AddLineParameter("Lacing", "L",
            "Lacing-face members running across a panel, from a chord to its twin.",
            GH_ParamAccess.list);
        pManager.AddPointParameter("Top Node", "TN",
            "Panel points on the top chords, one branch per chord.", GH_ParamAccess.tree);
        pManager.AddPointParameter("Bottom Node", "BN",
            "Panel points on the bottom chords, one branch per chord. Item i of every branch, "
            + "top and bottom, is the same cross-section through the truss; branch k sits under "
            + "branch k of Top Node.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var tops = new List<Curve>();
        var bottoms = new List<Curve>();
        int type = (int)TrussType.Warren;
        int lacing = (int)TrussType.WarrenWithVerticals;
        int divisions = 0;
        double spacing = 0.0;
        var snapPoints = new List<GH_Point>();
        int strictness = (int)SnapStrictness.Relaxed;
        bool endPosts = true;
        bool flip = false;
        bool flipLacing = false;
        bool onPlan = false;

        if (!da.GetDataList(0, tops)) return;
        if (!da.GetDataList(1, bottoms)) return;
        if (!da.GetData(2, ref type)) return;
        if (!da.GetData(3, ref lacing)) return;
        if (!da.GetData(4, ref divisions)) return;
        if (!da.GetData(5, ref spacing)) return;
        da.GetDataList(6, snapPoints);
        if (!da.GetData(7, ref strictness)) return;
        if (!da.GetData(8, ref endPosts)) return;
        if (!da.GetData(9, ref flip)) return;
        if (!da.GetData(10, ref flipLacing)) return;
        if (!da.GetData(11, ref onPlan)) return;

        // How many chords, whether they are valid, and every option are the
        // generator's to check: it throws with the message to show.
        var options = new BoxTrussOptions
        {
            Sides = new FlatTrussOptions
            {
                Type = (TrussType)type,
                Flip = flip,
                GenerateEndPosts = endPosts,
                Divisions = divisions,
                Spacing = spacing,
                MeasureOnPlan = onPlan,
                AdditionalSnapPoints = snapPoints.Select(p => p.Value).ToArray(),
                Strictness = (SnapStrictness)strictness,
                SnapTolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01,
            },
            LacingType = (TrussType)lacing,
            FlipLacing = flipLacing,
        };

        try
        {
            BoxTruss truss = BoxTrussGenerator.Generate(tops, bottoms, options);

            foreach (FormNote note in truss.Notes)
                AddRuntimeMessage(
                    note.Level == FormNoteLevel.Warning
                        ? GH_RuntimeMessageLevel.Warning
                        : GH_RuntimeMessageLevel.Remark,
                    note.Message);

            da.SetDataList(0, truss.TopChord);
            da.SetDataList(1, truss.BottomChord);
            da.SetDataList(2, truss.Verticals);
            da.SetDataList(3, truss.Diagonals);
            da.SetDataList(4, truss.EndPosts);
            da.SetDataList(5, truss.Struts);
            da.SetDataList(6, truss.Lacing);
            da.SetDataTree(7, PerChord(truss.TopNodes));
            da.SetDataTree(8, PerChord(truss.BottomNodes));

            Message = $"{(truss.ChordCount == 3 ? "Triangular" : "Box")}, {Naming.Humanise((TrussType)type)}\n"
                      + $"{truss.PanelCount} panels";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    private static DataTree<Point3d> PerChord(IReadOnlyList<IReadOnlyList<Point3d>> chords)
    {
        var tree = new DataTree<Point3d>();

        for (int k = 0; k < chords.Count; k++)
            tree.AddRange(chords[k], new GH_Path(k));

        return tree;
    }
}
