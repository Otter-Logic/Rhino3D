using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using OtterLogic.StructuralDesign;

namespace OtterLogic.Grasshopper.Components.StructuralDesign;

/// <summary>
/// Bars and their end forces over any number of load combinations and either or
/// both ends in; the bars grouped for end plate design, each with its own envelope,
/// out.
/// </summary>
public sealed class BeamEndPlateDesignGroupingComponent : DesignGroupingComponent
{
    /// <summary>What ties every force output to the geometry — the same sentence on all seven.</summary>
    private const string LinedUp =
        " Sorted into the design groups: branch g, item i belongs to item i of branch g of Grouped "
        + "Bars, so it can be tagged at the bar.";

    private static readonly ForceOutput[] Outputs =
    {
        new("Fx Max", "FxMax",
            "Largest axial force of each bar over every value given, with its sign as it came in." + LinedUp
            + "\n\nWith tension positive, the most tension the end plate carries."),
        new("Fx Min", "FxMin",
            "Smallest axial force of each bar over every value given, with its sign as it came in." + LinedUp
            + "\n\nWith tension positive, the most compression — or, above zero, the least tension."),
        new("Fy", "Fy", "Largest |Fy| of each bar over every value given." + LinedUp),
        new("Fz", "Fz", "Largest |Fz| of each bar over every value given." + LinedUp),
        new("Mx", "Mx", "Largest |Mx| of each bar over every value given." + LinedUp),
        new("My", "My", "Largest |My| of each bar over every value given." + LinedUp),
        new("Mz", "Mz", "Largest |Mz| of each bar over every value given." + LinedUp),
    };

    public BeamEndPlateDesignGroupingComponent()
        : base("Beam End Plate Design Grouping", "End Plates",
               "Group beam end plates for design by the forces at the ends of each bar, under any number "
               + "of load combinations. Plug in the bars and their six end forces, and read back the bars "
               + "grouped, each with its own envelope forces alongside.\n\n"
               + "Each force takes one branch per bar, holding its values — every combination, and both "
               + "ends if you like; a flat list is one value per bar. Every bar is reduced to its envelope "
               + "first and grouped on that, so one set of forces per bar comes back however many values "
               + "went in.\n\n"
               + "Only the axial force Fx is read with its sign, and kept as its largest and smallest "
               + "value: tension and compression load an end plate differently. An end plate is "
               + "symmetrical, so shear, torsion and bending are enveloped and grouped by size — which also "
               + "makes a bar's two ends, equal and opposite, read the same. Give Fx as the internal force, "
               + "the same sign at both ends.\n\n"
               + "Types are cut so that every bar carries at least Efficiency of its type's peak tension, "
               + "major shear and major moment — the forces that govern an end plate. Compression, minor-axis "
               + "shear and moment, and torsion come back in the envelope but do not split types. Connections "
               + "under a fifth of the largest are all simply light, so they share light types. The number of "
               + "types follows from Efficiency.")
    {
    }

    public override Guid ComponentGuid => new("cb811915-d7bb-4747-88d6-5a160aa6062d");

    protected override Bitmap? Icon => EmbeddedIcons.Load("beamendplategrouping", 24);

    protected override string ElementName => "bar";

    protected override IReadOnlyList<ForceOutput> ForceOutputs => Outputs;

    protected override string ForceInputDescription(string force)
        => $"{force} at the ends of each bar, one branch per bar in the same order as Bars, holding its "
           + "values: every load combination, and both ends if you like. A flat list with one value per "
           + "bar is one value each.\n\n"
           + "Every bar needs the same number of values, in the same order, in all six forces. Straight "
           + "out of an analysis, with its sign; no scaling needed."
           + (force == "Fx"
               ? " Give the axial force as the internal force — tension one sign, compression the other, "
                 + "the same at both ends — not as member-end forces, where one tension reads opposite at "
                 + "the two ends."
               : " Taken by size, so the two ends of a bar, equal and opposite, give the same value.");

    protected override void RegisterSettings(GH_InputParamManager pManager)
    {
        var defaults = new BeamEndPlateGroupingOptions();
        pManager.AddNumberParameter("Efficiency", "E",
            "Share of its type's peak that every bar must carry, in tension, |Fz| and |My|, between 0 and 1.\n\n"
            + "Higher keeps every end plate closer to what its bar needs and gives more types; lower gives fewer "
            + $"types and designs more bars for forces they never see. {defaults.Efficiency:0.0} by default — on a "
            + "110-bar model, 0.5 gave 9 types, 0.6 gave 13 and 0.7 gave 24.",
            GH_ParamAccess.item, defaults.Efficiency);
    }

    protected override Outcome? Group(double[][][] forces, IGH_DataAccess da)
    {
        double efficiency = new BeamEndPlateGroupingOptions().Efficiency;
        if (!da.GetData(FirstSettingInput, ref efficiency))
            return null;

        var result = BeamEndPlateGrouping.Group(forces[0], forces[1], forces[2], forces[3], forces[4], forces[5],
            new BeamEndPlateGroupingOptions { Efficiency = efficiency });

        var remarks = new List<string>();
        if (result.MinorAxisDominant.Length > 0)
            remarks.Add($"{result.MinorAxisDominant.Length} bar(s) carry more minor-axis shear or moment than major, "
                + "and their types were chosen on Fz and My alone. Report lists them — check their local axes, or "
                + "whether they bend the weak way.");

        int singles = result.Groups.Count(members => members.Length == 1);
        if (singles > 0)
            remarks.Add($"{singles} type(s) hold a single bar: no other bar is within {efficiency:P0} of its forces. "
                + "Lower Efficiency to fold them in, if the waste is acceptable.");

        var warnings = new List<string>();
        if (result.AxialLooksMirrored)
            warnings.Add("Every bar with an axial force has it as +N and -N, which is how member-end forces "
                + "read: one tension, opposite at the two ends. Give Fx as the internal force, the same sign "
                + "at both ends, or tension cannot be read.");

        string values = result.ValuesPerBeam == 1 ? "1 value per bar" : $"{result.ValuesPerBeam} values per bar";
        return new Outcome(result.Groups, result.Envelope.Columns, result.Report(ElementName),
            $"{result.GroupCount} types at {efficiency:P0}\n{values}", remarks, warnings);
    }

    protected override IGH_Param GeometryInput() => new Param_Curve
    {
        Name = "Bars",
        NickName = "B",
        Description = "The bars, one per beam, in the same order as the forces.",
        Access = GH_ParamAccess.list,
    };

    protected override IGH_Param GeometryOutput() => new Param_Curve
    {
        Name = "Grouped Bars",
        NickName = "B",
        Description = "The bars sorted into design groups, one branch per group, largest first.",
        Access = GH_ParamAccess.tree,
    };
}
