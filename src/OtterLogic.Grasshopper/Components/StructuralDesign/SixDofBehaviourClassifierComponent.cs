using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using OtterLogic.Clustering;

namespace OtterLogic.Grasshopper.Components.StructuralDesign;

/// <summary>
/// Six-degree-of-freedom results in, behaviour groups out, no settings in
/// between.
/// <para>
/// Adapter only. Every decision belongs to <see cref="Clusterer"/>; this unpacks
/// a tree, calls it once, and packs the answer back out.
/// </para>
/// <para>
/// Deliberately the opposite of the three raw methods under Machine Learning,
/// which it is built on. Those expose everything so an advanced user can drive
/// them; this exposes one input, because a structural engineer with analysis
/// results should not have to hold an opinion about covariance shapes to find
/// out which members behave alike.
/// </para>
/// <para>
/// It sits under Structural Design rather than beside those methods because what
/// it knows is structural, not statistical: that these columns are forces and
/// moments, that demand data should not be logged, and how many directions such
/// data really varies along. Anyone wanting to reproduce or vary it can wire the
/// three raw components up themselves.
/// </para>
/// </summary>
public sealed class SixDofBehaviourClassifierComponent : GH_Component
{
    public SixDofBehaviourClassifierComponent()
        : base("6DOF Behaviour Classifier", "6DOF Classify",
               "Group structural members by how they behave, from the six-degree-of-freedom demand "
               + "on each one. Feed it analysis results and read the groups off — nothing to set "
               + "up.\n\n"
               + "It combines the three clustering methods under Machine Learning, tailored "
               + "to 6DOF data: it "
               + "standardises the six degrees of freedom, reduces them to the few directions the "
               + "demand really varies along, fits all three models, and picks the one the data "
               + "supports. Clean, well-separated behaviours go to K-Means; behaviours that overlap "
               + "go to the Gaussian Mixture, which can say a member sits between two; data with "
               + "genuine one-off members goes to HDBSCAN, which can leave them unassigned rather "
               + "than forcing them into the nearest group. Report says which it chose and why.",
               Categories.Root, Categories.StructuralDesign)
    {
    }

    public override Guid ComponentGuid => new("d5a6b019-3c47-4e2a-8b91-7f0e42c6a3d8");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("sixdofclassifier", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Demands", "D",
            "One branch per member, holding that member's six degree-of-freedom demands in a "
            + "consistent order — Fx, Fy, Fz, Mx, My, Mz.\n\n"
            + "Straight out of an analysis. Every branch must be the same length: branch position is "
            + "what ties a result back to the member it came from. Other column counts work, as long "
            + "as they are consistent.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Model", "M",
            "Leave unset to let the classifier choose, which is the point of the component.\n\n"
            + "Set it only to overrule the choice and see what a particular model would have said.",
            GH_ParamAccess.item);
        pManager[1].Optional = true;

        var model = (Param_Integer)pManager[1];
        foreach (var (label, value) in EnumChoices.Of<ClusteringModel>())
            model.AddNamedValue(label, value);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Result", "R",
            "Behaviour group per member, in the order the branches arrived. -1 means the member was "
            + "left unassigned, which only happens when HDBSCAN is chosen.\n\n"
            + "Groups are numbered largest first, so the numbering does not shuffle when the input "
            + "changes slightly and downstream colours stay put.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Groups", "G",
            "Member indices bucketed by behaviour group, one branch each — ready to drive geometry "
            + "or colour without sorting on the canvas.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Confidence", "C",
            "How firmly each member belongs where it was put, 0 to 1.\n\n"
            + "Low values are worth looking at by hand: they are the members the model is least sure "
            + "about, which is usually where the interesting engineering is.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Centres", "M",
            "One branch per behaviour group, holding that group's centre back in the original "
            + "degrees of freedom.\n\n"
            + "The output that makes the rest actionable: it is what lets you name a group — \"the "
            + "high-torsion family\" — rather than just number it.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Unassigned", "X",
            "Indices of members placed in no group. Empty unless HDBSCAN was chosen.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Projection", "P",
            "Every member in the reduced space the clustering ran in, one branch each.\n\n"
            + "Three numbers per member by default, so it plots straight as points to see the "
            + "behaviour groups laid out.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Model", "?",
            "Which of the three models was chosen.",
            GH_ParamAccess.item);

        pManager.AddTextParameter("Report", "!",
            "What was chosen, why, and how all three models scored. Wire it to a panel — it is the "
            + "difference between trusting the answer and checking it.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(0, out GH_Structure<GH_Number> tree))
            return;

        if (!TrainingData.TryRead(tree, out double[,] data, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        ClusteringModel? forced = null;
        int model = -1;
        if (da.GetData(1, ref model))
        {
            if (!Enum.IsDefined(typeof(ClusteringModel), model))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Model must be one of "
                    + string.Join(", ", EnumChoices.Of<ClusteringModel>().Select(c => $"{c.Value} ({c.Label})"))
                    + ".");
                return;
            }

            forced = (ClusteringModel)model;
        }

        try
        {
            var result = Clusterer.Classify(
                data, new ClusteringOptions { Model = forced });

            Warn(result, data.GetLength(1));

            da.SetDataList(0, result.Labels);
            da.SetDataTree(1, Trees.FromBuckets(result.Members()));
            da.SetDataList(2, result.Confidence);
            da.SetDataTree(3, Trees.FromRows(result.Centres));
            da.SetDataList(4, result.Unassigned());
            da.SetDataTree(5, Trees.FromRows(result.Projection));
            da.SetData(6, ModelName(result));
            da.SetData(7, result.Report());

            Message = $"{ModelName(result)}\n{result.Groups} behaviours";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    /// <summary>
    /// Says the things a user would otherwise have to read the report to
    /// notice — and would not, because the component looks like it worked.
    /// </summary>
    private void Warn(ClusteringResult result, int columns)
    {
        if (result.KeptColumns.Length < columns)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{columns - result.KeptColumns.Length} degree(s) of freedom had no variation across "
                + "the model and were dropped. Normal for a planar frame, where the out-of-plane "
                + "degrees of freedom are identically zero.");

        if (result.ExplainedVariance < 0.7)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"The retained components carry {result.ExplainedVariance:P0} of the variation, so "
                + "the grouping is working from a partial picture. Demand here is spread across more "
                + "directions than usual rather than lying on a simple surface.");

        int unassigned = result.Unassigned().Length;
        if (unassigned > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{unassigned} member(s) belong to no behaviour group. They are in Unassigned, and "
                + "they are the ones worth looking at first.");

        int weak = result.Confidence.Where((_, i) => result.Labels[i] >= 0).Count(c => c < 0.6);
        if (weak > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{weak} member(s) sit below 0.6 confidence, between two behaviours. Check Confidence.");
    }

    private static string ModelName(ClusteringResult result) => result.Chosen switch
    {
        ClusteringModel.KMeans => "K-Means",
        ClusteringModel.GaussianMixture => "Gaussian Mixture",
        ClusteringModel.Hdbscan => "HDBSCAN",
        _ => result.Chosen.ToString(),
    };
}
