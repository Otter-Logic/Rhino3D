using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.Supervised.Learners;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Learners;

/// <summary>
/// A linear model as a learner on a wire: ridge for a number, logistic for a
/// class. No data input; see <see cref="BoostedTreesLearnerComponent"/>.
/// </summary>
public sealed class LinearLearnerComponent : GH_Component
{
    private static readonly LinearLearner Defaults = new();

    public LinearLearnerComponent()
        : base("Linear Model", "Linear",
               "The Linear learner for OtterTrain: a straight-line fit — ridge regression for a number, "
               + "logistic regression for a class.\n\n"
               + "The simplest honest model, and the one to compare the others against: when it does as "
               + "well as Boosted Trees, the relationship is a straight line and the trees are not "
               + "earning their keep. Pick it when the rows are few, when you want a model you can read, "
               + "or as the baseline before anything cleverer.\n\n"
               + "This component takes no samples: wire its Learner output into OtterTrain.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("e4bccf95-6c7c-4788-81c3-8f51ab94dcd4");

    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords => new[] { "ridge", "logistic", "regression", "linear", "baseline", "learner" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("linearmodel", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Regularisation", "R",
            "How strongly the coefficients are pulled toward zero. One by default. More makes the fit "
            + "smoother and more cautious; less lets it follow the rows more closely.",
            GH_ParamAccess.item, Defaults.Regularisation);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new LearnerParameter(), "Learner", "L", MethodWire.LearnerOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        double regularisation = Defaults.Regularisation;
        if (!da.GetData(0, ref regularisation)) return;

        var learner = new LinearLearner { Regularisation = regularisation };
        if (!LearnerCheck.Passes(this, learner)) return;

        da.SetData(0, new GH_Learner(learner));
        Message = learner.Describe();
    }
}
