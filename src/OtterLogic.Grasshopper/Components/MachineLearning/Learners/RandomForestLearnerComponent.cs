using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.Supervised.Learners;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Learners;

/// <summary>
/// A random forest as a learner on a wire: how many trees, and how deep. No data
/// input; see <see cref="BoostedTreesLearnerComponent"/>.
/// </summary>
public sealed class RandomForestLearnerComponent : GH_Component
{
    private static readonly RandomForestLearner Defaults = new();

    public RandomForestLearnerComponent()
        : base("Random Forest", "Forest",
               "The Random Forest learner for OtterTrain: many deep decision trees, each grown on its own "
               + "resample of the rows, averaged.\n\n"
               + "The safe choice. It almost never overfits badly, it is hard to tune wrongly, and it "
               + "cares about neither the scale of the inputs nor a useless feature. Boosted Trees is "
               + "usually a little more accurate; reach for the forest when boosting looks too good on "
               + "the rows it trained on, or when a steady answer matters more than the last few percent.\n\n"
               + "This component takes no samples: wire its Learner output into OtterTrain.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("71a8adce-afb9-4f70-8fd5-8ed64c32b5e9");

    // The learners tier, below the cluster methods.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords => new[] { "random forest", "bagging", "decision tree", "ensemble", "learner" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("randomforest", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Trees", "T",
            "How many trees are grown and averaged. A hundred by default; more steadies the answer and "
            + "costs time in proportion.",
            GH_ParamAccess.item, Defaults.Trees);

        pManager.AddIntegerParameter("Depth", "D",
            "How deep each tree may grow, or 0 for no limit — the default. A forest averages away the "
            + "variance that deep trees carry, so limiting them mostly costs accuracy.",
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

        var learner = new RandomForestLearner { Trees = trees, Depth = depth };
        if (!LearnerCheck.Passes(this, learner)) return;

        da.SetData(0, new GH_Learner(learner));
        Message = learner.Describe();
    }
}
