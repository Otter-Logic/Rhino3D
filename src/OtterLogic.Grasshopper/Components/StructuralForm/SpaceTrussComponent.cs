using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using OtterLogic.Core;
using OtterLogic.Grasshopper.Parameters.StructuralForm;
using OtterLogic.Grasshopper.Types;
using OtterLogic.StructuralForm;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.StructuralForm;

/// <summary>
/// Builds a double-layer space truss on a surface grid.
/// <para>
/// Adapter only. Where the second layer goes and what runs between the two
/// belongs to <see cref="SpaceTrussGenerator"/>, which the OtterSpaceTruss
/// Rhino command calls in exactly the same way.
/// </para>
/// </summary>
public sealed class SpaceTrussComponent : GH_Component
{
    public SpaceTrussComponent()
        : base("Space Truss", "SpaceTruss",
               "Build a double-layer space truss on a Surface Grid: the grid is the top layer, a "
               + "second layer sits a given depth under it, and a web joins the two.\n\n"
               + "Offset puts a node under the centre of every cell and a pyramid on it — the usual "
               + "space frame. Aligned puts a node under every node and runs a flat truss along every "
               + "grid line, with the bracing patterns Flat Truss has. Openings clipped out of the "
               + "grid are left out of the truss. For a single truss between two curves, use Flat "
               + "Truss; for three or four chords, Box Truss.",
               Categories.Root, Categories.StructuralForm)
    {
    }

    public override Guid ComponentGuid => new("466fddee-11e0-4a3d-a430-85824b4a1299");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap? Icon => EmbeddedIcons.Load("spacetruss", 24);

    /// <summary>
    /// Inputs in the order the truss is decided: what it stands on, how deep,
    /// how the layers sit, then the web between them, then which side.
    /// </summary>
    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new SurfaceGridParameter(), "Grid", "G",
            "The Grid output of Surface Grid: its nodes are the top layer, its members the top "
            + "chords, its pattern and divisions the truss's. Tick Clip To Trim there to leave "
            + "openings out of the truss.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Depth", "D",
            "Distance between the two layers, in model units, measured along the surface normal "
            + "or vertically as Depth Along says. Has to be greater than zero.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Type", "Ty",
            "How the second layer sits under the first. Offset: a node under the centre of every "
            + "cell, joined to its four corners — a pyramid per cell, and under a diagrid a node "
            + "under every diamond. Aligned: a node under every node, the grid's own pattern "
            + "between them, and a flat truss along every grid line. Right-click for the list.",
            GH_ParamAccess.item, (int)SpaceTrussType.Offset);

        pManager.AddIntegerParameter("Web", "W",
            "Bracing pattern of an Aligned truss, along every grid line: the same patterns Flat "
            + "Truss has, read the same way. Wire a Truss Type dropdown in, or right-click for the "
            + "list. Not read by Offset, whose web is the pyramids.",
            GH_ParamAccess.item, (int)TrussType.Warren);

        pManager.AddBooleanParameter("Flip Web", "FW",
            "Mirror every web diagonal within its own panel, as Flat Truss's Flip does. Aligned only.",
            GH_ParamAccess.item, false);

        pManager.AddBooleanParameter("End Posts", "E",
            "Close an Aligned truss with a post wherever a grid line ends: round the outside, and "
            + "at the rim of an opening. Off, the lines are open-ended there. Offset has no posts.",
            GH_ParamAccess.item, true);

        pManager.AddIntegerParameter("Depth Along", "DA",
            "Which way the second layer is offset. Surface normal: the truss is the same depth "
            + "everywhere and follows the surface, on its underside where it has one. Vertical: "
            + "straight down, so every web member under a node is plumb. Right-click for the list.",
            GH_ParamAccess.item, (int)DepthDirection.SurfaceNormal);

        pManager.AddBooleanParameter("Flip Depth", "FD",
            "Put the second layer on the other side of the surface: above a roof, inside a tower.",
            GH_ParamAccess.item, false);

        Named<SpaceTrussType>(pManager[2]);
        Named<TrussType>(pManager[3]);
        Named<DepthDirection>(pManager[6]);
    }

    private static void Named<T>(IGH_Param input) where T : struct, Enum
    {
        foreach (var (label, value) in EnumChoices.Of<T>())
            ((Param_Integer)input).AddNamedValue(label, value);
    }

    /// <summary>
    /// One port per section group, under the names the OtterSpaceTruss command
    /// gives its layers and Flat Truss gives its ports. The two chord ports
    /// are trees with a branch per part of the grid — U, V, diagonal, edge —
    /// so a definition that sized the grid's edge beams apart can size the
    /// truss's the same way. Nodes come out a branch per row, as Surface
    /// Grid's do, so a node's place in the tree is its place in the grid.
    /// </summary>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddLineParameter("Top Chord", "T",
            "Top chord members — the grid's own — in four branches: {0} along U, {1} along V, "
            + "{2} diagonals across cells, {3} edges. Flatten for one list.",
            GH_ParamAccess.tree);
        pManager.AddLineParameter("Bottom Chord", "B",
            "Bottom chord members, in the same four branches as Top Chord. An Offset truss under a "
            + "quad or triangulated grid has U, V and edge members; under a diagrid, diagonals.",
            GH_ParamAccess.tree);
        pManager.AddLineParameter("Vertical", "V",
            "Web members from a top node straight to the bottom node under it. Aligned only.",
            GH_ParamAccess.list);
        pManager.AddLineParameter("Diagonal", "D",
            "Web members running across between the layers: the pyramids of an Offset truss, the "
            + "bracing of an Aligned one.",
            GH_ParamAccess.list);
        pManager.AddLineParameter("End Post", "E", "Posts where a grid line ends. Aligned only.", GH_ParamAccess.list);
        pManager.AddPointParameter("Top Node", "TN",
            "The top layer's nodes, one branch per row: item i of branch j is the node at column "
            + "i, row j, as Surface Grid gives them.",
            GH_ParamAccess.tree);
        pManager.AddPointParameter("Bottom Node", "BN",
            "The bottom layer's nodes, one branch per row. Aligned: the rows of the top layer, one "
            + "node under each. Offset: a row per row of cells, one node per cell — or under a "
            + "diagrid, the top's rows with a node only at each diamond's centre.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        GH_SurfaceGrid? grid = null;
        double depth = 0.0;
        int type = (int)SpaceTrussType.Offset;
        int web = (int)TrussType.Warren;
        bool flipWeb = false;
        bool endPosts = true;
        int depthAlong = (int)DepthDirection.SurfaceNormal;
        bool flipDepth = false;

        if (!da.GetData(0, ref grid)) return;
        if (!da.GetData(1, ref depth)) return;
        if (!da.GetData(2, ref type)) return;
        if (!da.GetData(3, ref web)) return;
        if (!da.GetData(4, ref flipWeb)) return;
        if (!da.GetData(5, ref endPosts)) return;
        if (!da.GetData(6, ref depthAlong)) return;
        if (!da.GetData(7, ref flipDepth)) return;

        if (grid?.Value is null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Wire the Grid output of Surface Grid in.");
            return;
        }

        // Depth, Type, Web and Depth Along are not checked here. The
        // generator validates its own options and throws ArgumentException
        // carrying the message to show.
        var options = new SpaceTrussOptions
        {
            Depth = depth,
            Type = (SpaceTrussType)type,
            Web = (TrussType)web,
            FlipWeb = flipWeb,
            GenerateEndPosts = endPosts,
            DepthAlong = (DepthDirection)depthAlong,
            FlipDepth = flipDepth,
        };

        try
        {
            SpaceTruss truss = SpaceTrussGenerator.Generate(grid.Value, options);

            foreach (FormNote note in truss.Notes)
                AddRuntimeMessage(
                    note.Level == FormNoteLevel.Warning
                        ? GH_RuntimeMessageLevel.Warning
                        : GH_RuntimeMessageLevel.Remark,
                    note.Message);

            da.SetDataTree(0, ByGridRole(truss, TrussMemberRole.TopChord));
            da.SetDataTree(1, ByGridRole(truss, TrussMemberRole.BottomChord));
            da.SetDataList(2, truss.Verticals);
            da.SetDataList(3, truss.Diagonals);
            da.SetDataList(4, truss.EndPosts);
            da.SetDataTree(5, Trees.ByRow(truss.TopLattice));
            da.SetDataTree(6, Trees.ByRow(truss.BottomLattice));

            Message = $"{Naming.Humanise(truss.Type)}\n{truss.Grid.PanelsU} by {truss.Grid.PanelsV}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    /// <summary>
    /// A layer's chords with a branch per part of the grid. Every branch is
    /// there even when empty, so branch {3} is the edges whether or not this
    /// truss has any.
    /// </summary>
    private static DataTree<Line> ByGridRole(SpaceTruss truss, TrussMemberRole layer)
    {
        var tree = new DataTree<Line>();

        foreach (GridMemberRole part in GridMemberRoles.All)
        {
            var path = new GH_Path((int)part);
            tree.EnsurePath(path);
            tree.AddRange(truss.ChordsOf(layer, part), path);
        }

        return tree;
    }
}
