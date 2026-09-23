using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.MachineLearning.Training;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Learners;

/// <summary>
/// A small fully-connected network as a learner on a wire. No data input; see
/// <see cref="BoostedTreesLearnerComponent"/>.
/// </summary>
public sealed class NeuralNetworkLearnerComponent : GH_Component
{
    private static readonly NeuralNetworkLearner Defaults = new();

    public NeuralNetworkLearnerComponent()
        : base("Neural Network", "NeuralNet",
               "The Neural Network learner for OtterTrain: a small fully-connected network, one hidden "
               + "layer of 64 by default.\n\n"
               + "The comparison a person will ask for, and the shape deep-learning models grow from. "
               + "Slower to fit than Boosted Trees and rarely more accurate on a table of a few thousand "
               + "rows, so try trees first. Pick it when the relationship is smooth and the rows are "
               + "many, or when you mean to grow it later.\n\n"
               + "This component takes no samples: wire its Learner output into OtterTrain.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("9f6acff1-ecff-442f-a55e-76398f3054db");

    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords => new[] { "mlp", "perceptron", "neural", "deep learning", "learner" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("neuralnetwork", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Hidden Layers", "H",
            "Neurons in each hidden layer, input to output — one number per layer. One layer of 64 by "
            + "default; two of 64 is a deeper network.",
            GH_ParamAccess.list, Defaults.HiddenLayers[0]);

        pManager.AddIntegerParameter("Iterations", "I",
            "Passes over the training rows, at most. Five hundred by default; the fit stops early once "
            + "it settles.",
            GH_ParamAccess.item, Defaults.Iterations);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new LearnerParameter(), "Learner", "L", MethodWire.LearnerOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var layers = new List<int>();
        int iterations = Defaults.Iterations;
        if (!da.GetDataList(0, layers)) return;
        if (!da.GetData(1, ref iterations)) return;

        var learner = new NeuralNetworkLearner { HiddenLayers = layers.ToArray(), Iterations = iterations };
        if (!LearnerCheck.Passes(this, learner)) return;

        da.SetData(0, new GH_Learner(learner));
        Message = learner.Describe();
    }
}
