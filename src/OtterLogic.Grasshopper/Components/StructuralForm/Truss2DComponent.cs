using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using OtterLogic.StructuralForm;
using Rhino;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.StructuralForm;

/// <summary>
/// Builds a 2D truss between two chords.
/// <para>
/// Adapter only. Every decision about where nodes land and which diagonals get
/// drawn belongs to <see cref="Truss2DGenerator"/>, which the OtterTruss2D Rhino
/// command calls in exactly the same way.
/// </para>
/// </summary>
public sealed class Truss2DComponent : GH_Component
{
    public Truss2DComponent()
        : base("Truss 2D", "Truss2D",
               "Generate a 2D truss between a top and a bottom chord.",
               Categories.Root, Categories.StructuralForm)
    {
    }

    public override Guid ComponentGuid => new("6a430957-4853-4f58-bd90-71a07fcf248a");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap? Icon => EmbeddedIcons.Load("truss2d", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Top Chord", "T",
            "Top chord. Line, polyline or curve.", GH_ParamAccess.item);

        pManager.AddCurveParameter("Bottom Chord", "B",
            "Bottom chord. Reversed automatically if it runs against the top chord.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Type", "Ty",
            "Web bracing pattern.", GH_ParamAccess.item, (int)TrussType.Warren);

        pManager.AddBooleanParameter("End Posts", "E",
            "Close the truss with a post at each end.", GH_ParamAccess.item, true);

        pManager.AddIntegerParameter("Divisions", "D",
            "Number of panels to lay out evenly before snapping. This sets how many verticals "
            + "and diagonals there are; snap points then move those members rather than adding "
            + "to them. Zero hands control to the geometry, where every detected point becomes "
            + "a node in its own right.",
            GH_ParamAccess.item, 0);

        pManager.AddPointParameter("Snap Points", "P",
            "Extra points to snap to, on top of the polyline vertices and curve kinks already "
            + "detected. Each is pulled onto whichever chord is nearer.",
            GH_ParamAccess.list);
        pManager[5].Optional = true;

        pManager.AddNumberParameter("Snap Spacing", "S",
            "Target panel spacing in model units — another way to say Divisions when you care "
            + "about panel length rather than count. Divisions wins if both are set.",
            GH_ParamAccess.item, 0.0);

        pManager.AddBooleanParameter("Flip", "F",
            "Mirror every diagonal within its own panel. Pratt becomes Howe, and the Warren "
            + "zigzag starts the other way up. No effect on Vierendeel or cross-braced.",
            GH_ParamAccess.item, false);

        // Right-click the input for a readable menu instead of raw integers.
        var typeParam = (Param_Integer)pManager[2];
        foreach (TrussType value in Enum.GetValues<TrussType>())
            typeParam.AddNamedValue(Nicify(value), (int)value);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddLineParameter("Top Chord", "T", "Top chord members.", GH_ParamAccess.list);
        pManager.AddLineParameter("Bottom Chord", "B", "Bottom chord members.", GH_ParamAccess.list);
        pManager.AddLineParameter("Web", "W", "Web members.", GH_ParamAccess.list);
        pManager.AddLineParameter("End Posts", "E", "End posts.", GH_ParamAccess.list);
        pManager.AddPointParameter("Nodes", "N", "Panel points, top chord first.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Curve? top = null;
        Curve? bottom = null;
        int type = (int)TrussType.Warren;
        bool endPosts = true;
        int divisions = 0;
        var snapPoints = new List<GH_Point>();
        double spacing = 0.0;
        bool flip = false;

        if (!da.GetData(0, ref top)) return;
        if (!da.GetData(1, ref bottom)) return;
        if (!da.GetData(2, ref type)) return;
        if (!da.GetData(3, ref endPosts)) return;
        if (!da.GetData(4, ref divisions)) return;
        da.GetDataList(5, snapPoints);
        if (!da.GetData(6, ref spacing)) return;
        if (!da.GetData(7, ref flip)) return;

        if (top is null || !top.IsValid || bottom is null || !bottom.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Both chords must be valid curves.");
            return;
        }

        if (!Enum.IsDefined(typeof(TrussType), type))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Type {type} is not a truss type. Valid values are 0-{Enum.GetValues<TrussType>().Length - 1}.");
            return;
        }

        if (divisions < 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Divisions cannot be negative.");
            return;
        }

        if (spacing < 0.0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Snap Spacing cannot be negative.");
            return;
        }

        var options = new Truss2DOptions
        {
            Type = (TrussType)type,
            GenerateEndPosts = endPosts,
            Flip = flip,
            Divisions = divisions,
            AdditionalSnapPoints = snapPoints.Select(p => p.Value).ToArray(),
            SnapSpacing = spacing,
            SnapTolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01,
        };

        try
        {
            Truss2D truss = Truss2DGenerator.Generate(top, bottom, options);

            if (!truss.IsPlanar)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "The two chords are not coplanar, so this truss is warped.");

            if (endPosts && (truss.ChordsMeetAtStart || truss.ChordsMeetAtEnd))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    truss.ChordsMeetAtStart && truss.ChordsMeetAtEnd
                        ? "The chords meet at both ends, so no end posts were generated."
                        : "The chords meet at one end, so only one end post was generated.");

            da.SetDataList(0, truss.TopChord);
            da.SetDataList(1, truss.BottomChord);
            da.SetDataList(2, truss.Web);
            da.SetDataList(3, truss.EndPosts);
            da.SetDataList(4, truss.Nodes);

            Message = $"{Nicify(truss.Type)}\n{truss.PanelCount} panels";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    /// <summary>"WarrenWithVerticals" reads better as "Warren with verticals".</summary>
    private static string Nicify(TrussType type)
    {
        string name = type.ToString();
        var text = new System.Text.StringBuilder(name.Length + 4);

        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                text.Append(' ');
                text.Append(char.ToLowerInvariant(name[i]));
                continue;
            }

            text.Append(name[i]);
        }

        return text.ToString();
    }
}
