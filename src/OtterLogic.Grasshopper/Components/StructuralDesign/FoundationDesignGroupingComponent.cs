using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using OtterLogic.StructuralDesign;

namespace OtterLogic.Grasshopper.Components.StructuralDesign;

/// <summary>
/// Column base nodes and their reactions under any number of load combinations
/// in; the nodes grouped for design, each with its own envelope, out.
/// </summary>
public sealed class FoundationDesignGroupingComponent : DesignGroupingComponent
{
    /// <summary>What ties every force output to the geometry — the same sentence on all seven.</summary>
    private const string LinedUp =
        " Sorted into the design groups: branch g, item i belongs to item i of branch g of Grouped "
        + "Nodes, so it can be tagged at the node.";

    private static readonly ForceOutput[] Outputs =
    {
        new("Fx", "Fx", "Largest |Fx| of each node over every combination." + LinedUp),
        new("Fy", "Fy", "Largest |Fy| of each node over every combination." + LinedUp),
        new("Fz Max", "FzMax",
            "Largest Fz of each node over every combination, with its sign as it came in." + LinedUp
            + "\n\nOne end of the axial force: where positive is compression, the most compression the "
            + "foundation bears. Report says which sign was read as compression."),
        new("Fz Min", "FzMin",
            "Smallest Fz of each node over every combination, with its sign as it came in." + LinedUp
            + "\n\nThe other end: the least compression, or — past zero — the most uplift."),
        new("Mx", "Mx", "Largest |Mx| of each node over every combination." + LinedUp),
        new("My", "My", "Largest |My| of each node over every combination." + LinedUp),
        new("Mz", "Mz", "Largest |Mz| of each node over every combination." + LinedUp),
    };

    public FoundationDesignGroupingComponent()
        : base("Foundation Design Grouping", "Foundations",
               "Group foundations for design by the forces at each column base, under any number of "
               + "load combinations. Plug in the base nodes and their six forces, and read back the nodes "
               + "grouped, each with its own envelope forces alongside.\n\n"
               + "Each force takes one branch per node, holding its value under every combination; a "
               + "flat list is one combination. Every node is reduced to its envelope first and grouped "
               + "on that, so one set of forces per node comes back however many combinations went in.\n\n"
               + "Only the axial force Fz is read with its sign, and kept as its largest and smallest "
               + "value. A node in uplift under any combination is never grouped with one in compression "
               + "throughout; which sign is compression is read from the forces. Shear, bending and "
               + "torsion are designed to act either way, so they are enveloped and grouped by size — "
               + "+80 kN and -80 kN of shear are the same design.\n\n"
               + "Nothing to set up: it finds how many groups the forces support. A node that fits no "
               + "group comes back as a group of its own, to be designed individually. Report shows "
               + "which model found the groups and why.")
    {
    }

    public override Guid ComponentGuid => new("7f4737ee-1b2c-450d-be1f-b31f037fe09d");

    protected override Bitmap? Icon => EmbeddedIcons.Load("foundationgrouping", 24);

    protected override string ElementName => "node";

    protected override IReadOnlyList<ForceOutput> ForceOutputs => Outputs;

    protected override string ForceInputDescription(string force)
        => $"{force} at each node, one branch per node in the same order as Nodes, holding its value under "
           + "every load combination. A flat list with one value per node is one combination.\n\n"
           + "Every node needs the same combinations in the same order, in all six forces. Straight out "
           + "of an analysis, with its sign; no scaling needed. Feed combinations rather than load cases — "
           + "each node is grouped on its envelope over them. For a planar model, give the out-of-plane "
           + "components as zeros.";

    protected override Outcome? Group(double[][][] forces, IGH_DataAccess da)
    {
        var result = FoundationGrouping.Group(forces[0], forces[1], forces[2], forces[3], forces[4], forces[5]);
        var grouping = result.Grouping;

        string groups = grouping.OneOffCount == 0
            ? $"{grouping.Groups.Length} groups"
            : $"{grouping.BehaviourGroupCount} groups + {grouping.OneOffCount} one-off";
        string combinations = result.CombinationCount == 1 ? "1 combination" : $"{result.CombinationCount} combinations";

        return new Outcome(grouping.Groups, result.Envelope.Columns, result.Report(ElementName),
            $"{groups}\n{combinations}", Remarks(grouping));
    }

    /// <summary>
    /// Says what a user would otherwise have to read the report to notice — and
    /// would not, because the component looks like it worked.
    /// </summary>
    private static List<string> Remarks(DesignGroupingResult result)
    {
        var remarks = new List<string>();

        if (result.OneOffCount > 0)
            remarks.Add($"{result.OneOffCount} node(s) fit no behaviour group. Each is a group of one at the end of "
                + "the output — design them individually rather than with the nearest group.");

        if (result.Parts.Count > 1)
            remarks.Add($"Grouped in {result.Parts.Count} parts that never share a group — "
                + $"{string.Join(", ", result.Parts.Select(p => $"{p.Elements.Length} {p.Name}"))}. Report says why.");

        var classifications = result.Parts
            .Select(part => part.Classification)
            .OfType<SixDofClassificationResult>()
            .ToList();

        int dropped = classifications.Max(c => (int?)(c.InputColumnCount - c.KeptColumns.Length)) ?? 0;
        if (dropped > 0)
            remarks.Add($"{dropped} force measure(s) were the same for every node and were ignored. Normal for a "
                + "planar model, where the out-of-plane components are zero.");

        double least = classifications.Min(c => (double?)c.ExplainedVariance) ?? 1.0;
        if (least < 0.7)
            remarks.Add($"The grouping works from as little as {least:P0} of the variation in the forces, so it is a "
                + "partial picture. Check the groups against their forces.");

        int weak = classifications.Sum(c => c.Confidence
            .Where((_, i) => c.Labels[i] >= 0)
            .Count(confidence => confidence < 0.6));
        if (weak > 0)
            remarks.Add($"{weak} node(s) sit between two groups. Worth checking which group's forces they are "
                + "designed for.");

        return remarks;
    }

    protected override IGH_Param GeometryInput() => new Param_Point
    {
        Name = "Nodes",
        NickName = "N",
        Description = "The column base nodes, one per foundation, in the same order as the forces.",
        Access = GH_ParamAccess.list,
    };

    protected override IGH_Param GeometryOutput() => new Param_Point
    {
        Name = "Grouped Nodes",
        NickName = "N",
        Description = "The nodes sorted into design groups, one branch per group, largest first.",
        Access = GH_ParamAccess.tree,
    };
}
