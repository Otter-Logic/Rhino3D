using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Parameters.MachineLearning;

/// <summary>
/// The parameter every Learner input and output is made of. Hidden and not
/// persistent, for the same reasons as <see cref="ClusterMethodParameter"/>.
/// </summary>
public sealed class LearnerParameter : GH_Param<GH_Learner>
{
    public LearnerParameter()
        : base("Learner", "Learner",
               "A method OtterTrain can fit, with its settings chosen. Made by Boosted Trees, Random "
               + "Forest, Neural Network or Linear Model; read by OtterTrain.",
               Categories.Root, Categories.MachineLearning, GH_ParamAccess.item)
    {
    }

    public override Guid ComponentGuid => new("19f8c189-6801-401b-8bf4-b9db2dfe982b");

    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override Bitmap? Icon => EmbeddedIcons.Load("learner", 24);
}
