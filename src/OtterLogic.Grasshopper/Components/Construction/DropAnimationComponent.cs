using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using OtterLogic.Construction;

namespace OtterLogic.Grasshopper.Components.Construction;

/// <summary>
/// Geometry, its erection order and a moment in time in; the pieces that have left
/// the hook, each where it is on its way down, out.
/// <para>
/// Adapter only. When each piece moves, and how far along it is, comes from
/// <see cref="DropTimeline.At"/>; here that progress becomes a vertical translation
/// of a copy of the geometry, and pieces not yet lifted are left out so the scene
/// shows only what is on site.
/// </para>
/// </summary>
public sealed class DropAnimationComponent : GH_Component
{
    private const int GeometryInput = 0;
    private const int OrderInput = 1;
    private const int TimeInput = 2;
    private const int HeightInput = 3;
    private const int OverlapInput = 4;

    public DropAnimationComponent()
        : base("Drop Animation", "Drop",
               "Play an erection sequence: drag Time from 0 to 1 and the pieces drop into place one after another, "
               + "in order.\n\n"
               + "Wire the geometry and the Order from Erection Sequence — or its Stage, to drop each stage as one. "
               + "Any list of integers works as an order, so a sequence from anywhere else, or one you wrote by "
               + "hand, plays the same way. Each piece falls through its own window of the timeline; Overlap says "
               + "how many are in the air at once. Animate the Time slider from its right-click menu to record it.\n\n"
               + "For the order itself, use Erection Sequence.",
               Categories.Root, Categories.Construction)
    {
    }

    public override Guid ComponentGuid => new("c3a9d5f2-7e18-4c6b-b2f4-51e8a0d97c63");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("dropanimation", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGeometryParameter("Geometry", "G",
            "The pieces, one item each, in the order they came out of Erection Sequence's Lines input — or any "
            + "geometry in the same order as Order: the lines themselves, or the members modelled on them.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Order", "O",
            "Per piece, its place in the sequence, 0 first. Order from Erection Sequence, or Stage to drop each "
            + "stage as one. Pieces sharing a value fall together.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Time", "T",
            "Where the sequence is: 0 nothing has moved, 1 everything has landed. Wire a slider from 0 to 1.",
            GH_ParamAccess.item, 0.0);

        pManager.AddNumberParameter("Drop Height", "H",
            "How far above its place each piece starts, in model units. By default the height of the whole "
            + "model, so the first piece appears above the last thing built.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Overlap", "Ov",
            "How many pieces are in the air at once. One drops them strictly one at a time; more keeps a "
            + "large model moving.",
            GH_ParamAccess.item, 4);

        pManager[HeightInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGeometryParameter("Geometry", "G",
            "Every piece that has left the hook, where it is now: landed pieces in place, falling pieces on the way.",
            GH_ParamAccess.list);
        pManager.AddIntegerParameter("Index", "I",
            "Per output piece, which input it is — to carry colours or names across.",
            GH_ParamAccess.list);
        pManager.AddNumberParameter("Progress", "P",
            "Per input piece: 0 not yet lifted, 1 landed, between the two on its way down.",
            GH_ParamAccess.list);
        pManager.AddBooleanParameter("Landed", "L", "Per input piece: whether it is down.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var pieces = new List<IGH_GeometricGoo>();
        da.GetDataList(GeometryInput, pieces);
        var order = new List<int>();
        da.GetDataList(OrderInput, order);

        double time = 0.0;
        int overlap = 4;
        da.GetData(TimeInput, ref time);
        da.GetData(OverlapInput, ref overlap);

        if (pieces.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire in the pieces to drop.");
            return;
        }

        if (order.Count != pieces.Count)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"{pieces.Count} pieces but {order.Count} order values. Every piece needs its place in the sequence, in the same order.");
            return;
        }

        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i] is null || !pieces[i].IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Piece {i} is missing or invalid. Remove it, or every index after it shifts.");
                return;
            }
        }

        double height = double.NaN;
        da.GetData(HeightInput, ref height);
        if (double.IsNaN(height))
        {
            var box = BoundingBox.Empty;
            foreach (var piece in pieces)
                box.Union(piece.Boundingbox);
            height = box.IsValid ? box.Max.Z - box.Min.Z : 0.0;
            if (height <= 0.0)
                height = box.IsValid ? box.Diagonal.Length : 1.0;
            if (height <= 0.0)
                height = 1.0;
        }
        else if (!double.IsFinite(height))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Drop height must be a finite distance.");
            return;
        }

        DropTimelineResult timeline;
        try
        {
            timeline = DropTimeline.At(order.ToArray(), time, overlap);
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var moved = new List<IGH_GeometricGoo>();
        var index = new List<int>();
        var landed = new bool[pieces.Count];
        for (int i = 0; i < pieces.Count; i++)
        {
            landed[i] = timeline.IsLanded(i);
            if (!timeline.IsStarted(i))
                continue;

            var copy = pieces[i].DuplicateGeometry();
            double lift = height * (1.0 - timeline.Progress[i]);
            if (lift > 0.0)
                copy = copy.Transform(Transform.Translation(0.0, 0.0, lift));

            moved.Add(copy);
            index.Add(i);
        }

        da.SetDataList(0, moved);
        da.SetDataList(1, index);
        da.SetDataList(2, timeline.Progress);
        da.SetDataList(3, landed);

        Message = $"{timeline.Landed} of {pieces.Count} down";
    }
}
