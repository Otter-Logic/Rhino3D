using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.StructuralDesign;
using OtterLogic.Grasshopper.Types;
using OtterLogic.StructuralDesign;

namespace OtterLogic.Grasshopper.Components.StructuralDesign;

/// <summary>
/// The Structural Insight Engine's tuning on a wire: how many groups to consider,
/// how each clustering view votes, and how the lines are read as members.
/// <para>
/// No geometry input, on purpose. The model goes to the engine; this only makes the
/// <see cref="StructuralInsightOptions"/> record and hands it over, the way a
/// Kangaroo goal goes into the solver. Tolerance and the fixed group count are not
/// here because they stay on the engine: the one is the document's, the other is
/// the setting a user actually reaches for.
/// </para>
/// </summary>
public sealed class InsightSettingsComponent : GH_Component
{
    private const int MaximumGroupsInput = 0;
    private const int MinimumGroupSizeInput = 1;
    private const int ConnectivityWeightInput = 2;
    private const int GeometryWeightInput = 3;
    private const int DensityWeightInput = 4;
    private const int RoleWeightInput = 5;
    private const int WeightByAgreementInput = 6;
    private const int ChainInput = 7;

    public InsightSettingsComponent()
        : base("Insight Settings", "Settings",
               "Tuning for the Structural Insight Engine, for when its defaults do not fit the model.\n\n"
               + "The engine fuses four unsupervised views of the members — connectivity (spectral clustering "
               + "of the element graph: contiguous regions), geometry (hierarchical clustering of what each "
               + "member is like: repeated kinds wherever they are), density (HDBSCAN: dense groups and the "
               + "outliers outside them) and role (what each member is like and what it is attached to). Each "
               + "weight is that view's vote; zero switches the view off. Maximum Groups caps what every view "
               + "and the fusion consider, and Minimum Group Size merges stragglers into the connected group "
               + "they agree with most.\n\n"
               + "Leave the engine's Settings unwired and every value here is at its default. This component "
               + "takes no geometry: wire its Settings output into the engine.",
               Categories.Root, Categories.StructuralDesign)
    {
    }

    public override Guid ComponentGuid => new("5a9d3c7e-8f14-4b2a-b6e3-1c47d9e0f582");

    // The second step: reached for once the engine's first answer is in hand.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords => new[] { "insight", "settings", "weights", "views", "clustering" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("insightsettings", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        var defaults = new StructuralInsightOptions();

        pManager.AddIntegerParameter("Maximum Groups", "MaxG",
            "Most natural groups each view and the fusion consider.", GH_ParamAccess.item, defaults.MaximumGroups);

        pManager.AddIntegerParameter("Minimum Group Size", "MinS",
            "Groups smaller than this merge into the connected group their elements agree with most. One merges nothing.",
            GH_ParamAccess.item, defaults.MinimumGroupSize);

        pManager.AddNumberParameter("Connectivity Weight", "Wc",
            "Vote of the connectivity view — groups of connected elements that are alike. Zero skips it.",
            GH_ParamAccess.item, defaults.ConnectivityWeight);

        pManager.AddNumberParameter("Geometry Weight", "Wg",
            "Vote of the geometry view — elements alike wherever they are, so repeated elements group. Zero skips it.",
            GH_ParamAccess.item, defaults.GeometryWeight);

        pManager.AddNumberParameter("Density Weight", "Wd",
            "Vote of the density view — dense groups, and outliers outside them. Zero skips it and the outlier issues.",
            GH_ParamAccess.item, defaults.DensityWeight);

        pManager.AddNumberParameter("Role Weight", "Wr",
            "Vote of the role view — members alike in themselves and in what they are attached to, so members playing "
            + "the same part group wherever they are. Zero skips it.",
            GH_ParamAccess.item, defaults.RoleWeight);

        pManager.AddBooleanParameter("Weight By Agreement", "A",
            "Scale each view's vote by how far the other views agree with it. Switch off when the views answer "
            + "deliberately different questions — regions and kinds that cut across each other — and the vote "
            + "should be exactly the weights given.",
            GH_ParamAccess.item, defaults.WeightViewsByAgreement);

        pManager.AddBooleanParameter("Chain Members", "Ch",
            "Read lines that carry straight on through their joints as one member. Switch off when a model was "
            + "drawn with deliberate breaks that ought to stay breaks.",
            GH_ParamAccess.item, !defaults.ElementsAsMembers);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new InsightSettingsParameter(), "Settings", "S",
            "These settings, for the Structural Insight Engine's Settings input. The model is wired there, not here.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var defaults = new StructuralInsightOptions();
        int maximumGroups = defaults.MaximumGroups, minimumGroupSize = defaults.MinimumGroupSize;
        double connectivity = defaults.ConnectivityWeight, geometry = defaults.GeometryWeight;
        double density = defaults.DensityWeight, role = defaults.RoleWeight;
        bool byAgreement = defaults.WeightViewsByAgreement, chain = !defaults.ElementsAsMembers;

        da.GetData(MaximumGroupsInput, ref maximumGroups);
        da.GetData(MinimumGroupSizeInput, ref minimumGroupSize);
        da.GetData(ConnectivityWeightInput, ref connectivity);
        da.GetData(GeometryWeightInput, ref geometry);
        da.GetData(DensityWeightInput, ref density);
        da.GetData(RoleWeightInput, ref role);
        da.GetData(WeightByAgreementInput, ref byAgreement);
        da.GetData(ChainInput, ref chain);

        // Said here, where the wrong value was typed, rather than at the engine two
        // wires away. The engine still validates everything against the model.
        if (maximumGroups < 2)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Maximum Groups must be at least 2.");
            return;
        }

        if (minimumGroupSize < 1)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Minimum Group Size must be at least 1.");
            return;
        }

        foreach (var (name, weight) in new[] { ("Connectivity", connectivity), ("Geometry", geometry), ("Density", density), ("Role", role) })
        {
            if (!double.IsFinite(weight) || weight < 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{name} Weight must be zero or above.");
                return;
            }
        }

        if (connectivity + geometry + density + role <= 0.0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one view needs a weight above zero, or there is nothing to fuse.");
            return;
        }

        var settings = new StructuralInsightOptions
        {
            MaximumGroups = maximumGroups,
            MinimumGroupSize = minimumGroupSize,
            ConnectivityWeight = connectivity,
            GeometryWeight = geometry,
            DensityWeight = density,
            RoleWeight = role,
            WeightViewsByAgreement = byAgreement,
            ElementsAsMembers = !chain,
        };

        da.SetData(0, new GH_InsightSettings(settings));
        Message = GH_InsightSettings.Describe(settings).Replace(", ", "\n");
    }
}
