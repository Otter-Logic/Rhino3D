using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using OtterLogic.Core;
using OtterLogic.Grasshopper.Parameters.StructuralForm;
using OtterLogic.Grasshopper.Types;
using OtterLogic.StructuralForm;
using Rhino;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.StructuralForm;

/// <summary>
/// Draws a quad, triangulated or diagrid layout of members over a surface.
/// <para>
/// Adapter only. Where the grid lines go and which members get drawn belong to
/// <see cref="SurfaceGridGenerator"/>, which the OtterSurfaceGrid Rhino command
/// calls in exactly the same way.
/// </para>
/// </summary>
public sealed class SurfaceGridComponent : GH_Component
{
    public SurfaceGridComponent()
        : base("Surface Grid", "SrfGrid",
               "Lay straight members over a surface as a quad grid, a triangulated grid or a "
               + "diagrid, with every node on the surface.\n\n"
               + "Give it a single surface, or instead the two to four curves round the outside of "
               + "an area. Divisions are measured by length along the edges, and a snap point on "
               + "an edge pulls a grid line through it. A structured grid, not a mesh: rows and "
               + "columns you can predict, for a gridshell, a façade, a tower or a grillage. A "
               + "trimmed surface is gridded whole, with a warning, unless Clip To Trim leaves out "
               + "what falls in its openings. The Grid output feeds Space Truss.",
               Categories.Root, Categories.StructuralForm)
    {
    }

    public override Guid ComponentGuid => new("7cd47fde-55aa-4216-a75c-aab81a3de841");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap? Icon => EmbeddedIcons.Load("surfacegrid", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddBrepParameter("Surface", "S",
            "A single surface to grid. Leave empty to use Edges instead.", GH_ParamAccess.item);
        pManager[0].Optional = true;

        pManager.AddCurveParameter("Edges", "E",
            "Instead of a surface: two, three or four curves round the outside of the area, in "
            + "any order. Three or four have to meet end to end; two may stand apart, as the "
            + "rails of a strip. Ignored when a Surface is given.",
            GH_ParamAccess.list);
        pManager[1].Optional = true;

        pManager.AddIntegerParameter("Pattern", "P",
            "Quad, triangulated or diagrid. Wire a Grid Pattern dropdown in, or right-click for "
            + "the same list as a menu.",
            GH_ParamAccess.item, (int)GridPattern.Quad);

        pManager.AddIntegerParameter("Divisions U", "DU",
            "Panels across the surface in its U direction. Overrides Spacing U. Zero from both "
            + "leaves it to the edges: a line through every kink and snap point. A diagrid needs "
            + "an even count and is given one more when it is not.",
            GH_ParamAccess.item, 0);

        pManager.AddIntegerParameter("Divisions V", "DV",
            "Panels across the surface in its V direction.", GH_ParamAccess.item, 0);

        pManager.AddNumberParameter("Spacing U", "SU",
            "Target panel width in U, in model units, measured along the longer edge that way.",
            GH_ParamAccess.item, 0.0);

        pManager.AddNumberParameter("Spacing V", "SV",
            "Target panel width in V, in model units.", GH_ParamAccess.item, 0.0);

        pManager.AddPointParameter("Snap Points", "Pt",
            "Points to run a grid line through — a column, a support. Each has to lie on an edge "
            + "of the surface; one out in the middle is discounted, with a message saying so.",
            GH_ParamAccess.list);
        pManager[7].Optional = true;

        pManager.AddIntegerParameter("Strictness", "SS",
            "What happens to a Snap Point the even layout cannot reach: Relaxed leaves it unused "
            + "and says so, Strict runs a line through it. Right-click for the list.",
            GH_ParamAccess.item, (int)SnapStrictness.Relaxed);

        pManager.AddIntegerParameter("Diagonals", "Dg",
            "Which diagonal each cell of a triangulated grid takes: one way, alternating, or "
            + "whichever is shorter. Right-click for the list.",
            GH_ParamAccess.item, (int)DiagonalRule.OneWay);

        pManager.AddBooleanParameter("Flip", "F",
            "Take the other diagonal in every cell. For a diagrid, this shifts it off the corners.",
            GH_ParamAccess.item, false);

        // Appended rather than placed beside Surface, where it reads best:
        // Grasshopper restores a saved component's inputs by position, so a
        // new one anywhere but the end rewires every definition using this.
        pManager.AddBooleanParameter("Clip To Trim", "Cl",
            "Clip the grid to a trimmed surface: leave out the nodes that fall in an opening or "
            + "outside the trimmed edge, the members that ran to them, and any member crossing an "
            + "opening. Off, the grid covers the whole surface underneath the trim, with a warning. "
            + "Rows and columns keep their numbering either way.",
            GH_ParamAccess.item, false);

        Named<GridPattern>(pManager[2]);
        Named<SnapStrictness>(pManager[8]);
        Named<DiagonalRule>(pManager[9]);
    }

    private static void Named<T>(IGH_Param input) where T : struct, Enum
    {
        foreach (var (label, value) in EnumChoices.Of<T>())
            ((Param_Integer)input).AddNamedValue(label, value);
    }

    /// <summary>
    /// One port per section group, under the names the OtterSurfaceGrid command
    /// gives its layers. U and V come out as trees with a branch per grid line,
    /// so a whole beam is a branch; nodes as a tree with a branch per row, so
    /// the position of a node in the tree is its position in the grid.
    /// </summary>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddLineParameter("U Member", "U",
            "Members along the U grid lines inside the surface, one branch per line.",
            GH_ParamAccess.tree);
        pManager.AddLineParameter("V Member", "V",
            "Members along the V grid lines inside the surface, one branch per line.",
            GH_ParamAccess.tree);
        pManager.AddLineParameter("Diagonal", "D", "Members across a cell.", GH_ParamAccess.list);
        pManager.AddLineParameter("Edge", "E", "Members along the boundary of the surface.", GH_ParamAccess.list);
        pManager.AddPointParameter("Node", "N",
            "Every node of the grid, one branch per row: item i of branch j is the node at "
            + "column i, row j. A diagrid stands on every other one. Under Clip To Trim, a node "
            + "that fell in an opening is left out of its row.",
            GH_ParamAccess.tree);

        pManager.AddParameter(new SurfaceGridParameter(), "Grid", "G",
            "The grid as one wire — nodes, members and the surface they sit on — for Space Truss "
            + "to build on.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Brep? surface = null;
        var edges = new List<Curve>();
        int pattern = (int)GridPattern.Quad;
        int divisionsU = 0, divisionsV = 0;
        double spacingU = 0.0, spacingV = 0.0;
        var snapPoints = new List<GH_Point>();
        int strictness = (int)SnapStrictness.Relaxed;
        int diagonals = (int)DiagonalRule.OneWay;
        bool flip = false;
        bool clip = false;

        da.GetData(0, ref surface);
        da.GetDataList(1, edges);
        if (!da.GetData(2, ref pattern)) return;
        if (!da.GetData(3, ref divisionsU)) return;
        if (!da.GetData(4, ref divisionsV)) return;
        if (!da.GetData(5, ref spacingU)) return;
        if (!da.GetData(6, ref spacingV)) return;
        da.GetDataList(7, snapPoints);
        if (!da.GetData(8, ref strictness)) return;
        if (!da.GetData(9, ref diagonals)) return;
        if (!da.GetData(10, ref flip)) return;
        if (!da.GetData(11, ref clip)) return;

        edges.RemoveAll(edge => edge is null);

        if (surface is null && edges.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Give a Surface, or the Edges round an area.");
            return;
        }

        var options = new SurfaceGridOptions
        {
            Pattern = (GridPattern)pattern,
            DivisionsU = divisionsU,
            DivisionsV = divisionsV,
            SpacingU = spacingU,
            SpacingV = spacingV,
            SnapPoints = snapPoints.Select(p => p.Value).ToArray(),
            Strictness = (SnapStrictness)strictness,
            Diagonals = (DiagonalRule)diagonals,
            Flip = flip,
            ClipToTrim = clip,
            Tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01,
        };

        try
        {
            SurfaceGrid grid = surface is not null
                ? SurfaceGridGenerator.Generate(surface, options)
                : SurfaceGridGenerator.Generate(edges, options);

            foreach (FormNote note in grid.Notes)
                AddRuntimeMessage(
                    note.Level == FormNoteLevel.Warning
                        ? GH_RuntimeMessageLevel.Warning
                        : GH_RuntimeMessageLevel.Remark,
                    note.Message);

            da.SetDataTree(0, ByGridLine(grid, GridMemberRole.U));
            da.SetDataTree(1, ByGridLine(grid, GridMemberRole.V));
            da.SetDataList(2, grid.Diagonals);
            da.SetDataList(3, grid.Edges);

            da.SetDataTree(4, Trees.ByRow(grid.Lattice));
            da.SetData(5, new GH_SurfaceGrid(grid));

            Message = $"{Naming.Humanise(options.Pattern)}\n{grid.PanelsU} by {grid.PanelsV}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    private static DataTree<Line> ByGridLine(SurfaceGrid grid, GridMemberRole role)
    {
        var tree = new DataTree<Line>();

        foreach (GridMember member in grid.Members.Where(m => m.Role == role))
            tree.Add(member.Line, new GH_Path(member.GridLine));

        return tree;
    }
}
