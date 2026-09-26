using System.Globalization;
using Grasshopper.Kernel.Types;
using OtterLogic.StructuralDesign;

namespace OtterLogic.Grasshopper.Types;

/// <summary>
/// The Settings wire: the Structural Insight Engine's tuning, chosen once on an
/// Insight Settings component and carried to the engine on one wire.
/// <para>
/// The engine took every setting as an input of its own until 2026-09-26, eleven in
/// all, and seven of them were knobs a user has to understand before they can
/// ignore. Now the engine takes geometry, a group count and this wire, and the
/// knobs live on a component nobody needs to place — the same cut the Machine
/// Learning panel made with its Method wire. The record never changes once made,
/// so a duplicate shares it, and two wires carrying equal settings compare equal.
/// </para>
/// </summary>
public sealed class GH_InsightSettings : GH_Goo<StructuralInsightOptions>
{
    public GH_InsightSettings() { }
    public GH_InsightSettings(StructuralInsightOptions settings) : base(settings) { }

    public override bool IsValid => Value is not null;
    public override string TypeName => "Insight Settings";
    public override string TypeDescription => "The Structural Insight Engine's tuning, for its Settings input";

    public override IGH_Goo Duplicate() => new GH_InsightSettings(Value);

    public override string ToString() => Value is null ? "<null insight settings>" : Describe(Value);

    /// <summary>The settings in one line, the way the component's message and the wire's tooltip both say them.</summary>
    public static string Describe(StructuralInsightOptions s)
    {
        string weights = string.Join("/", new[] { s.ConnectivityWeight, s.GeometryWeight, s.DensityWeight, s.RoleWeight }
            .Select(w => w.ToString("0.##", CultureInfo.InvariantCulture)));
        string text = $"up to {s.MaximumGroups} groups, weights {weights}";
        if (s.MinimumGroupSize > 1) text += $", groups of {s.MinimumGroupSize}+";
        if (!s.WeightViewsByAgreement) text += ", views weighted as given";
        if (s.ElementsAsMembers) text += ", lines not chained";
        return text;
    }
}
