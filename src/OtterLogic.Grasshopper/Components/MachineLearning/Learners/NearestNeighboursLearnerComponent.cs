using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.MachineLearning.Training;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Learners;

/// <summary>
/// Nearest neighbours as a learner on a wire: how many have a say. No data
/// input; see <see cref="BoostedTreesLearnerComponent"/>.
/// </summary>
public sealed class NearestNeighboursLearnerComponent : GH_Component
{
    private static readonly NearestNeighboursLearner Defaults = new();

    public NearestNeighboursLearnerComponent()
        : base("Nearest Neighbours", "kNN",
               "The Nearest Neighbours learner for OtterTrain: answer with the known answers of the most "
               + "similar training samples — their commonest class, or the average of their values.\n\n"
               + "No fitting to speak of and no assumption about the shape of the relationship, which is "
               + "its strength and its weakness: it follows the data exactly where there is data, and "
               + "has nothing to say far from it. Pick it when the question really is 'what did the "
               + "samples most like this one do', and Boosted Trees when you want a rule that carries "
               + "beyond the samples.\n\n"
               + "This component takes no samples: wire its Learner output into OtterTrain.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("104e4f01-dee6-4013-bf67-fb4a9d7fc63c");

    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords => new[] { "knn", "k nearest", "neighbours", "neighbors", "learner" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("nearestneighbours", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Neighbours", "K",
            "How many of the nearest training samples have a say. Five by default; more smooths, fewer "
            + "follows local detail.",
            GH_ParamAccess.item, Defaults.Neighbours);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new LearnerParameter(), "Learner", "L", MethodWire.LearnerOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        int neighbours = Defaults.Neighbours;
        if (!da.GetData(0, ref neighbours)) return;

        var learner = new NearestNeighboursLearner { Neighbours = neighbours };
        if (!LearnerCheck.Passes(this, learner)) return;

        da.SetData(0, new GH_Learner(learner));
        Message = learner.Describe();
    }
}
