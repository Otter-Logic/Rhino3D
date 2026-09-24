using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;

// Fully qualified: inside Components.MachineLearning a bare "MachineLearning"
// binds here rather than to the toolkit, which is the namespace trap this repo
// has been caught by before.
using global::OtterLogic.MachineLearning.Shapes;

namespace OtterLogic.Grasshopper.Components.Dataset;

/// <summary>
/// Outlines in; the same row of numbers describing each of them out, learned from
/// the outlines themselves.
/// <para>
/// Adaptor only. The reading, aligning and decomposing belong to
/// <see cref="ShapeSignature"/>; here curves become points in a plane and the
/// shapes it reports come back as curves.
/// </para>
/// </summary>
public sealed class ShapeSignatureComponent : GH_Component
{
    private const int OutlinesInput = 0;
    private const int PlaneInput = 1;
    private const int PointsInput = 2;
    private const int TurnsInput = 3;
    private const int MirrorInput = 4;
    private const int SizeInput = 5;
    private const int SquareInput = 6;
    private const int ComponentsInput = 7;
    private const int VarianceInput = 8;
    private const int LookInput = 9;

    /// <summary>
    /// How finely a curved outline is read before the signature resamples it. Four
    /// readings per point it will keep is enough that resampling sees the curve
    /// rather than the chords; a polyline is read at its own corners and needs none
    /// of this.
    /// </summary>
    private const int OverRead = 4;

    public ShapeSignatureComponent()
        : base("Shape Signature", "Shape",
               "Describe every outline by the same row of numbers, so that shapes can be grouped, compared or "
               + "searched — and learn what to measure from the outlines themselves rather than being told.\n\n"
               + "The usual way to compare shapes is to pick a few measurements: length, width, area, how far "
               + "off-centre. It works until it does not. Two outlines can agree on every one of those and still be "
               + "a rectangle notched at its corners and a rectangle notched in its middle. This reads each outline "
               + "at the same number of points, finds the average shape they vary around, and takes the directions "
               + "they actually vary in — which catches a notch, a curve or a raked corner without any of them "
               + "having been anticipated.\n\n"
               + "Signature is what you wire onward: into OtterCluster's Data to find the shape families, with its Map "
               + "on to see them and its Report saying what sets each family apart. Two outlines that are the "
               + "same shape get the same row however they were drawn — from a different corner, the other way "
               + "round, with extra points along an edge.\n\n"
               + "How far apart two rows are is how far apart the two outlines are, averaged point for "
               + "corresponding point, in model units. So a tolerance on this is a tolerance you can justify: cut a "
               + "clustering of it at 5 and no two members are more than 5 apart.",
               Categories.Root, Categories.Dataset)
    {
    }

    public override Guid ComponentGuid => new("3a5f2d18-7c64-4a1b-9e0d-2b6f8c41d537");

    // Features from geometry: the last tier of the Dataset panel, a thing to do
    // before a method rather than a method.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("shapesignature", 24);

    private static readonly ShapeSignatureOptions Defaults = new();

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Outlines", "O",
            "The outlines to describe. All closed or all open — a closed one has no first point, so one is "
            + "searched for, and an open one is read end to end. A polyline is read at its own corners; anything "
            + "curved is read finely enough to follow it.",
            GH_ParamAccess.list);

        pManager.AddPlaneParameter("Plane", "P",
            "The plane the outlines are read in. They are compared as seen from here, so outlines lying in "
            + "different planes should be oriented onto one before being wired in. World XY by default.",
            GH_ParamAccess.item, Plane.WorldXY);

        pManager.AddIntegerParameter("Points", "N",
            "How many points each outline is read at. Every outline is read at the same count whatever it was "
            + "drawn with, which is what makes a four-corner shape and a forty-corner one comparable. Thirty-two "
            + "carries a notch or a curve.",
            GH_ParamAccess.item, Defaults.Points);

        pManager.AddIntegerParameter("Turns", "T",
            "How many equally spaced turns of an outline count as the same shape. 1 means a turned shape is a "
            + "different shape. 2 means a shape and the same shape turned half way round are the same — what a "
            + "flat part with no up and no down is. 4 adds the quarter turns.",
            GH_ParamAccess.item, Defaults.Turns);

        pManager.AddBooleanParameter("Mirror", "M",
            "Whether an outline and its mirror image are the same shape. Off by default: a thing and its mirror "
            + "are usually two things to make.",
            GH_ParamAccess.item, Defaults.AllowReflection);

        pManager.AddBooleanParameter("Set Aside Size", "S",
            "Divide each outline by its own size first, so two shapes of the same proportions are the same shape. "
            + "Off by default, since size is usually part of what tells things apart. The sizes come out on Size "
            + "either way.",
            GH_ParamAccess.item, Defaults.NormaliseScale);

        pManager.AddBooleanParameter("Turn Square", "Q",
            "Turn every outline to sit as squarely as it can on the others, so a shape at any angle is the same "
            + "shape. Off by default: outlines usually arrive in a frame that means something. Unlike Turns this "
            + "allows any angle at all.",
            GH_ParamAccess.item, Defaults.NormaliseRotation);

        pManager.AddIntegerParameter("Components", "K",
            "How many numbers to describe a shape with. Zero lets Variance decide, which is usually what you "
            + "want — how many a population needs is itself a finding.",
            GH_ParamAccess.item, 0);

        pManager.AddNumberParameter("Variance", "V",
            "When Components is zero, keep the fewest directions carrying this much of the variation, 0 to 1. Keep "
            + "all of it at 1, which is what makes the distance between two rows exactly the distance between the "
            + "two outlines.",
            GH_ParamAccess.item, Defaults.Variance);

        pManager.AddNumberParameter("Look", "L",
            "How far along each component to draw it on Variation, in spreads either side.",
            GH_ParamAccess.item, 2.0);

        for (int input = PlaneInput; input <= LookInput; input++)
            pManager[input].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Signature", "S",
            "One branch per outline, holding the numbers that describe it. This is what to wire into "
            + "OtterCluster's Data.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Count", "K",
            "How many numbers describe one shape.", GH_ParamAccess.item);

        pManager.AddNumberParameter("Spread", "V",
            "How much the outlines vary along each component, largest first, as a distance in model units. The "
            + "first is what this population mostly differs by.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Carried", "C",
            "The share of the variation the kept components carry, 0 to 1. What was dropped is gone from "
            + "everything downstream.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Size", "Z",
            "Each outline's size — how far its points sit from its own centre — matching Outlines. Measured as "
            + "drawn, whether or not size was set aside.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Residual", "R",
            "Per outline, how far it sits from its own description, in model units. Zero when every component was "
            + "kept; large where an outline is unlike anything else here.",
            GH_ParamAccess.list);

        pManager.AddCurveParameter("Mean Shape", "M",
            "The average outline the population varies around, drawn in the plane.",
            GH_ParamAccess.item);

        pManager.AddCurveParameter("Variation", "X",
            "One branch per component, holding the mean drawn back, still, and forward along it. This is how a "
            + "component gets named: one population's first component is how raked, another's is how deep the "
            + "notch, and no number will tell you that — seeing it will.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var curves = new List<Curve>();
        if (!da.GetDataList(OutlinesInput, curves))
            return;

        if (curves.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire the outlines to describe into Outlines.");
            return;
        }

        var plane = Plane.WorldXY;
        int points = Defaults.Points, turns = Defaults.Turns, components = 0;
        bool mirror = Defaults.AllowReflection, size = Defaults.NormaliseScale, square = Defaults.NormaliseRotation;
        double variance = Defaults.Variance, look = 2.0;

        if (!da.GetData(PlaneInput, ref plane)) return;
        if (!da.GetData(PointsInput, ref points)) return;
        if (!da.GetData(TurnsInput, ref turns)) return;
        if (!da.GetData(MirrorInput, ref mirror)) return;
        if (!da.GetData(SizeInput, ref size)) return;
        if (!da.GetData(SquareInput, ref square)) return;
        if (!da.GetData(ComponentsInput, ref components)) return;
        if (!da.GetData(VarianceInput, ref variance)) return;
        if (!da.GetData(LookInput, ref look)) return;

        if (!TryRead(curves, plane, points, out var outlines, out bool closed))
            return;

        ShapeSignatureResult result;
        try
        {
            result = ShapeSignature.Fit(outlines, new ShapeSignatureOptions
            {
                Points = points,
                Closed = closed,
                Turns = turns,
                AllowReflection = mirror,
                NormaliseScale = size,
                NormaliseRotation = square,
                Components = components,
                Variance = variance,
            });
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        foreach (string note in result.Notes)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);

        var signature = new DataTree<double>();
        for (int i = 0; i < result.Scores.GetLength(0); i++)
            for (int c = 0; c < result.Count; c++)
                signature.Add(result.Scores[i, c], new GH_Path(i));

        var variation = new DataTree<Curve>();
        for (int c = 0; c < result.Count; c++)
        {
            var path = new GH_Path(c);
            foreach (double along in new[] { -look, 0.0, look })
                variation.Add(Draw(result.Variation(c, along), plane, closed), path);
        }

        da.SetDataTree(0, signature);
        da.SetData(1, result.Count);
        da.SetDataList(2, result.Spread);
        da.SetData(3, result.Carried);
        da.SetDataList(4, result.Size);
        da.SetDataList(5, result.Residual);
        da.SetData(6, Draw(result.MeanShape, plane, closed));
        da.SetDataTree(7, variation);

        Message = $"{outlines.Count} outlines\n{result.Count} numbers each";
    }

    /// <summary>
    /// Curves as points in the plane. A polyline is taken at its own corners, which
    /// is exact; anything else is divided finely enough that resampling sees the
    /// curve rather than its chords.
    /// </summary>
    private bool TryRead(
        List<Curve> curves, Plane plane, int points, out List<double[,]> outlines, out bool closed)
    {
        outlines = new List<double[,]>(curves.Count);
        closed = false;

        for (int i = 0; i < curves.Count; i++)
        {
            if (curves[i] is null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Outline {i} is missing.");
                return false;
            }

            if (i == 0)
                closed = curves[i].IsClosed;
            else if (curves[i].IsClosed != closed)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Outline {i} is {(curves[i].IsClosed ? "closed" : "open")} where outline 0 is "
                    + $"{(closed ? "closed" : "open")}. Read them apart, or close them all.");
                return false;
            }

            var read = new List<Point3d>();
            if (curves[i].TryGetPolyline(out var polyline))
            {
                read.AddRange(polyline);
            }
            else
            {
                var parameters = curves[i].DivideByCount(points * OverRead, true) ?? Array.Empty<double>();
                read.AddRange(parameters.Select(curves[i].PointAt));
            }

            var flat = new double[read.Count, 2];
            for (int p = 0; p < read.Count; p++)
            {
                if (!plane.RemapToPlaneSpace(read[p], out var local))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Outline {i} could not be read in that plane.");
                    return false;
                }

                (flat[p, 0], flat[p, 1]) = (local.X, local.Y);
            }

            outlines.Add(flat);
        }

        return true;
    }

    /// <summary>A shape the signature reports, drawn back in the plane it was read in.</summary>
    private static Curve Draw(double[,] shape, Plane plane, bool closed)
    {
        var points = new List<Point3d>(shape.GetLength(0) + 1);
        for (int p = 0; p < shape.GetLength(0); p++)
            points.Add(plane.PointAt(shape[p, 0], shape[p, 1]));

        if (closed)
            points.Add(points[0]);

        return new PolylineCurve(points);
    }
}
