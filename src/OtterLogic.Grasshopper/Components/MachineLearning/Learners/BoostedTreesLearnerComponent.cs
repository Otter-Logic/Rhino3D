using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.Supervised.Learners;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Learners;

/// <summary>
/// Boosted trees as a learner on a wire: how many trees, and how deep. No data
/// input; the samples go to OtterTrain, and this only makes the
/// <see cref="BoostedTreesLearner"/> record for it.
/// </summary>
public sealed class BoostedTreesLearnerComponent : GH_Component
{
    private static readonly BoostedTreesLearner Defaults = new();

    public BoostedTreesLearnerComponent()
        : base("Boosted Trees", "Trees",
               "The Boosted Trees learner for OtterTrain: many small decision trees, each correcting the "
               + "ones before it.\n\n"
               + "The default, and what OtterTrain fits with nothing wired. On a table of a few thousand "
               + "rows it is usually the most accurate thing available and fits in seconds; the scale of "
               + "the inputs does not matter to it, and a useless feature does not throw it. Reach for "
               + "Linear Model to check whether the relationship is really a straight line, Random "
               + "Forest when boosting looks too good on the rows it trained on, and Neural Network "
               + "as the comparison.\n\n"
               + "This component takes no samples: wire its Learner output into OtterTrain.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("1dd83fc6-2b75-40ab-8d51-024bfea4fb75");

    // The learners tier, below the cluster methods.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords => new[] { "gradient boosting", "xgboost", "decision tree", "ensemble", "learner" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("boostedtrees", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Trees", "T",
            "How many trees are added, one after another. More fits closer, and past a few hundred "
            + "mostly slower.",
            GH_ParamAccess.item, Defaults.Trees);

        pManager.AddIntegerParameter("Depth", "D",
            "How deep each tree may grow. Three by default: boosting corrects a shallow tree many "
            + "times over, and a deep one fitted to residuals memorises rows.",
            GH_ParamAccess.item, Defaults.Depth);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new LearnerParameter(), "Learner", "L", MethodWire.LearnerOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        int trees = Defaults.Trees;
        int depth = Defaults.Depth;
        if (!da.GetData(0, ref trees)) return;
        if (!da.GetData(1, ref depth)) return;

        var learner = new BoostedTreesLearner { Trees = trees, Depth = depth };
        if (!LearnerCheck.Passes(this, learner)) return;

        da.SetData(0, new GH_Learner(learner));
        Message = learner.Describe();
    }
}

/// <summary>
/// Validates a learner where it is made, so a bad setting is an error on the
/// component that holds it rather than on OtterTrain minutes later.
/// </summary>
internal static class LearnerCheck
{
    public static bool Passes(GH_Component owner, Learner learner)
    {
        try
        {
            learner.Validate();
            return true;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return false;
        }
    }
}
