using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using OtterLogic.StructuralDesign;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.StructuralDesign;

/// <summary>
/// Six lists of six-degree-of-freedom data in, one value per element; the elements
/// grouped by behaviour, with each group's range, out.
/// <para>
/// Adapter only. The grouping is <see cref="SixDofBehaviourClassifier.Classify(IReadOnlyList{double}, IReadOnlyList{double}, IReadOnlyList{double}, IReadOnlyList{double}, IReadOnlyList{double}, IReadOnlyList{double}, SixDofClassificationOptions?)"/>;
/// here the lists are unpacked and the answer packed back, with any geometry
/// wired alongside carried through the same grouping.
/// </para>
/// </summary>
public sealed class SixDofBehaviourClassifierComponent : GH_Component
{
    private static readonly string[] Dofs = { "Fx", "Fy", "Fz", "Mx", "My", "Mz" };

    private const int GeometryInput = 6;
    private const int UnassignedInput = 7;
    private const int MinimumGroupsInput = 8;
    private const int MaximumGroupsInput = 9;
    private const int MinimumClusterSizeInput = 10;
    private const int ModelInput = 11;

    /// <summary>Model input value meaning "compare all three and choose".</summary>
    private const int ChooseModel = -1;

    public SixDofBehaviourClassifierComponent()
        : base("6DOF Behaviour Classifier", "6DOF",
               "Group elements by how they behave, from six degrees of freedom of data on each — member end "
               + "forces, support reactions, connection demands, displacements, whatever the six values are.\n\n"
               + "Multipurpose: nothing here knows what the elements are or what the grouping is for. Prepare the "
               + "six lists for your own purpose first — take the envelope over load combinations, the size of a "
               + "force designed either way, a split into parts that may never share a group — and wire in one "
               + "value per element.\n\n"
               + "It fits K-Means, a Gaussian Mixture and HDBSCAN, and picks the one the data supports: clean "
               + "families, overlapping ones, or families with genuine one-offs. Report says which and why, and how "
               + "far the three agreed. Each group's Minimum and Maximum are its envelope in the units that came "
               + "in.\n\n"
               + "To drive the clustering yourself, wire the features into OtterCluster under Machine Learning.",
               Categories.Root, Categories.StructuralDesign)
    {
    }

    public override Guid ComponentGuid => new("8e2f4a61-c3d7-4b95-a0e8-5f1b7d29c64e");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("sixdofclassifier", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // One input per degree of freedom rather than a branch of six per element.
        // A branch holds its values by position, so a branch of five or one in the
        // wrong order looks fine and puts values under the wrong name; a wire into
        // Fz can only be Fz.
        foreach (string dof in Dofs)
            pManager.AddNumberParameter(dof, dof,
                $"{dof} of each element, one value per element, in the same order as the other five. Zero where "
                + "there is none — a planar model's out-of-plane values, say; a column that never varies is ignored.",
                GH_ParamAccess.list);

        pManager.AddGenericParameter("Geometry", "G",
            "Optional. One item per element, in the same order as the values — points, curves, anything — to "
            + "come back sorted into the groups.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Unassigned", "U",
            "What happens to an element HDBSCAN finds fits no group. Leave by default. Plug in Unassigned for a dropdown.",
            GH_ParamAccess.item, (int)UnplacedPolicy.Leave);

        pManager.AddIntegerParameter("Minimum Groups", "MinG", "Fewest groups K-Means and the mixture consider.",
            GH_ParamAccess.item, 2);

        pManager.AddIntegerParameter("Maximum Groups", "MaxG", "Most groups K-Means and the mixture consider.",
            GH_ParamAccess.item, 10);

        pManager.AddIntegerParameter("Minimum Cluster Size", "MinC",
            "Smallest group HDBSCAN accepts. Zero derives it from the element count.",
            GH_ParamAccess.item, 0);

        pManager.AddIntegerParameter("Model", "M",
            "Choose (the default) compares all three models and picks one. Set a model to use it regardless.",
            GH_ParamAccess.item, ChooseModel);

        for (int i = GeometryInput; i <= ModelInput; i++)
            pManager[i].Optional = true;

        foreach (var (label, value) in EnumChoices.Of<UnplacedPolicy>())
            ((Param_Integer)pManager[UnassignedInput]).AddNamedValue(label, value);

        var model = (Param_Integer)pManager[ModelInput];
        model.AddNamedValue("Choose", ChooseModel);
        foreach (var value in Enum.GetValues<ClusteringModel>())
            model.AddNamedValue(ClusterSelection.Name(value), (int)value);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Group", "G",
            "The group of each element, in the order they came in; -1 for an element left unassigned.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Indices", "I",
            "One branch per group, largest first, holding the index of each element in it. Groups made for "
            + "unassigned elements follow the rest.",
            GH_ParamAccess.tree);

        pManager.AddGenericParameter("Grouped Geometry", "GG",
            "The Geometry input sorted like Indices. Empty when nothing was wired there.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Confidence", "C",
            "How firmly each element belongs to its group, in the chosen model's own terms; zero for an element "
            + "the model left unassigned.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Centres", "Ce",
            "One branch per group: the mean of each of the six values across its elements, Fx to Mz.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Minimum", "Min",
            "One branch per group: the smallest of each of the six values across its elements, Fx to Mz.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Maximum", "Max",
            "One branch per group: the largest of each of the six values across its elements, Fx to Mz — with "
            + "Minimum, the envelope the group would be designed or checked for.",
            GH_ParamAccess.tree);

        pManager.AddPointParameter("Projection", "P",
            "Every element as a point in the three directions the six values vary along most — colour them by "
            + "Group to see the families.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Model", "Mo", "Which model was used.", GH_ParamAccess.item);

        pManager.AddTextParameter("Report", "!",
            "What was chosen and why, how all three models scored, and each group's range. Wire it to a panel to "
            + "check the grouping rather than take it on trust.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var values = new List<double>[Dofs.Length];
        for (int j = 0; j < Dofs.Length; j++)
        {
            values[j] = new List<double>();
            if (!da.GetDataList(j, values[j]))
                return;
        }

        var geometry = new List<IGH_Goo>();
        bool hasGeometry = da.GetDataList(GeometryInput, geometry) && geometry.Count > 0;
        if (hasGeometry && geometry.Count != values[0].Count)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Geometry has {geometry.Count} item(s) but there are {values[0].Count} values per degree of freedom. "
                + "It needs one item per element, in the same order.");
            return;
        }

        int unassigned = (int)UnplacedPolicy.Leave;
        int minimumGroups = 2, maximumGroups = 10, minimumClusterSize = 0, model = ChooseModel;
        da.GetData(UnassignedInput, ref unassigned);
        da.GetData(MinimumGroupsInput, ref minimumGroups);
        da.GetData(MaximumGroupsInput, ref maximumGroups);
        da.GetData(MinimumClusterSizeInput, ref minimumClusterSize);
        da.GetData(ModelInput, ref model);

        if (!Enum.IsDefined(typeof(UnplacedPolicy), unassigned))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unassigned must be one of "
                + string.Join(", ", EnumChoices.Of<UnplacedPolicy>().Select(c => $"{c.Value} ({c.Label})")) + ".");
            return;
        }

        if (model != ChooseModel && !Enum.IsDefined(typeof(ClusteringModel), model))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Model must be {ChooseModel} (Choose) or one of "
                + string.Join(", ", Enum.GetValues<ClusteringModel>().Select(m => $"{(int)m} ({ClusterSelection.Name(m)})")) + ".");
            return;
        }

        var options = new SixDofClassificationOptions
        {
            Unplaced = (UnplacedPolicy)unassigned,
            Selection = new ClusterSelectorOptions
            {
                MinimumGroups = minimumGroups,
                MaximumGroups = maximumGroups,
                MinimumClusterSize = minimumClusterSize > 0 ? minimumClusterSize : null,
                Model = model == ChooseModel ? null : (ClusteringModel)model,
            },
        };

        SixDofClassificationResult result;
        try
        {
            result = SixDofBehaviourClassifier.Classify(values[0], values[1], values[2], values[3], values[4], values[5], options);
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        var members = result.Members();
        var grouped = new DataTree<IGH_Goo>();
        if (hasGeometry)
            for (int g = 0; g < members.Length; g++)
                grouped.AddRange(members[g].Select(i => geometry[i]), new GH_Path(g));

        var projection = result.Projection;
        var points = Enumerable.Range(0, projection.GetLength(0)).Select(i => new Point3d(
            projection[i, 0],
            projection.GetLength(1) > 1 ? projection[i, 1] : 0.0,
            projection.GetLength(1) > 2 ? projection[i, 2] : 0.0));

        Remarks(result);

        da.SetDataList(0, result.Labels);
        da.SetDataTree(1, Trees.FromBuckets(members));
        da.SetDataTree(2, grouped);
        da.SetDataList(3, result.Confidence);
        da.SetDataTree(4, Trees.FromRows(result.Centres));
        da.SetDataTree(5, Trees.FromRows(result.Minimum));
        da.SetDataTree(6, Trees.FromRows(result.Maximum));
        da.SetDataList(7, points);
        da.SetData(8, ClusterSelection.Name(result.Chosen));
        da.SetData(9, result.Report());

        Message = $"{result.Groups} groups\n{ClusterSelection.Name(result.Chosen)}";
    }

    /// <summary>Says what a user would otherwise have to read the report to notice.</summary>
    private void Remarks(SixDofClassificationResult result)
    {
        int unassigned = result.Unassigned().Length;
        if (unassigned > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, result.Unplaced switch
            {
                UnplacedPolicy.OwnGroup => $"{unassigned} element(s) fit no group; each is a group of its own at the end.",
                UnplacedPolicy.Nearest => $"{unassigned} element(s) fit no group and were filed with the nearest.",
                _ => $"{unassigned} element(s) fit no group and are labelled -1. Set Unassigned if every element needs a group.",
            });

        int dropped = result.InputColumnCount - result.KeptColumns.Length;
        if (dropped > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{dropped} degree(s) of freedom were the same for every element and were ignored — normal for a planar model.");

        if (result.ExplainedVariance < 0.7)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"The grouping works from {result.ExplainedVariance:P0} of the variation in the values, so it is a partial "
                + "picture. Check the groups against their ranges.");

        if (result.ModelAgreement < 0.5)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"The three models drew quite different groups (agreement {result.ModelAgreement:0.00}), so the answer "
                + "depends on which was chosen. Report says why it was.");
    }
}
