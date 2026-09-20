using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using OtterLogic.StructuralForm;
using Rhino;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.StructuralForm;

/// <summary>
/// Fills the panels a floor's primary beams enclose with secondary members.
/// <para>
/// Adapter only. Finding the panels, deciding which way is long and placing the
/// members all belong to <see cref="BeamInfillGenerator"/>, which the
/// OtterBeamInfill Rhino command calls in exactly the same way.
/// </para>
/// </summary>
public sealed class BeamInfillComponent : GH_Component
{
    public BeamInfillComponent()
        : base("Beam Infill", "Infill",
               "Fill every panel a set of primary beams encloses with evenly spaced secondary "
               + "members, running the long way across each panel.\n\n"
               + "Feed it one floor of a stick model in any order: wherever the beams close a "
               + "four-sided loop, that is a panel. Panels with any other number of sides are "
               + "handed back on Skipped rather than guessed at — draw a beam across one to split "
               + "it. For bracing between two curves you choose yourself, use Flat Truss.",
               Categories.Root, Categories.StructuralForm)
    {
    }

    public override Guid ComponentGuid => new("18a11383-1a7f-4d58-ada9-5dba32309973");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap? Icon => EmbeddedIcons.Load("beaminfill", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Beams", "B",
            "The primary beams of one floor, in any order. They do not need splitting where "
            + "they cross, and columns caught up in the list are ignored.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Divisions", "D",
            "Number of bays each panel is divided into — one more than the members placed in "
            + "it. Overrides Spacing whenever both are set. Zero from both finds the panels and "
            + "leaves them empty, which is the way to check the beams read as intended.",
            GH_ParamAccess.item, 0);

        pManager.AddNumberParameter("Spacing", "S",
            "Target spacing between members, in model units, measured along the beams they "
            + "land on. Each panel divides its own length by this and rounds to whole bays, so "
            + "one value suits a floor of uneven panels.",
            GH_ParamAccess.item, 0.0);

        pManager.AddBooleanParameter("Flip", "F",
            "Run the members the short way across each panel instead of the long way.",
            GH_ParamAccess.item, false);
    }

    /// <summary>
    /// Members and their ends come out as trees with one branch per panel, in
    /// the same order as the Panel list, because the panel is the unit anyone
    /// works in afterwards — sizing the members of the big bays apart from the
    /// small ones is a branch selection rather than a geometric search.
    /// </summary>
    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddLineParameter("Secondary", "S",
            "The members placed, one branch per panel.", GH_ParamAccess.tree);
        pManager.AddPointParameter("Node", "N",
            "Where the members land on the beams, with the points two neighbouring panels "
            + "share merged into one. These are the points to split the primary beams at.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Panel", "P",
            "Outline of each panel filled. Item i is branch i of Secondary.",
            GH_ParamAccess.list);
        pManager.AddCurveParameter("Skipped", "X",
            "Outlines of the panels found but left empty, for not having four sides or for "
            + "having a loop of beams inside them.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var beams = new List<Curve>();
        int divisions = 0;
        double spacing = 0.0;
        bool flip = false;

        if (!da.GetDataList(0, beams)) return;
        if (!da.GetData(1, ref divisions)) return;
        if (!da.GetData(2, ref spacing)) return;
        if (!da.GetData(3, ref flip)) return;

        // Nulls are a wiring accident rather than a modelling one, so they are
        // dropped here; everything else is the generator's to validate.
        beams.RemoveAll(beam => beam is null);

        var options = new BeamInfillOptions
        {
            Divisions = divisions,
            Spacing = spacing,
            Flip = flip,
            Tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01,
        };

        try
        {
            BeamInfill infill = BeamInfillGenerator.Generate(beams, options);

            foreach (FormNote note in infill.Notes)
                AddRuntimeMessage(
                    note.Level == FormNoteLevel.Warning
                        ? GH_RuntimeMessageLevel.Warning
                        : GH_RuntimeMessageLevel.Remark,
                    note.Message);

            var members = new DataTree<Line>();

            for (int i = 0; i < infill.Panels.Count; i++)
            {
                // Ensured first, so a panel too small to take a member still
                // has its branch and the indices keep matching Panel.
                var path = new GH_Path(i);
                members.EnsurePath(path);
                members.AddRange(infill.Panels[i].Members, path);
            }

            da.SetDataTree(0, members);
            da.SetDataList(1, infill.Nodes);
            da.SetDataList(2, infill.Panels.Select(p => p.Outline));
            da.SetDataList(3, infill.SkippedPanels);

            Message = $"{infill.Panels.Count} panels\n{infill.Members.Count()} members";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
