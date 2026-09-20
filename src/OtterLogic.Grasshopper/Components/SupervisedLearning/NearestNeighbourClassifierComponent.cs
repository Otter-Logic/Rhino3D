using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Supervised;
using OtterLogic.Supervised.Neighbours;

namespace OtterLogic.Grasshopper.Components.SupervisedLearning;

/// <summary>
/// Classification by the vote of the most similar training samples.
/// <para>
/// Adapter only. The algorithm belongs to <see cref="KNearestNeighbours"/>.
/// </para>
/// </summary>
public sealed class NearestNeighbourClassifierComponent : GH_Component
{
    public NearestNeighbourClassifierComponent()
        : base("Nearest Neighbour Classifier", "KNN Class",
               "Predict a class for each sample from the classes of the training samples most like it.\n\n"
               + "The first thing to try on a new table. It assumes nothing about how the inputs relate to "
               + "the answer, and every prediction can be checked: Neighbours lists the training samples it "
               + "came from. It treats every column as equally important, so it weakens as irrelevant "
               + "columns are added, and a sample unlike anything in training still gets an answer — watch "
               + "Distances for that. When a few columns matter much more than the rest, use Logistic "
               + "Regression, which works that out. To predict a quantity rather than a class, use Nearest "
               + "Neighbour Regressor.",
               Categories.Root, Categories.SupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("16a9b5c3-fc07-42de-b6e0-11b1f4932cb3");

    // The methods tier of the Supervised Learning panel.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("knnclassifier", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        foreach (var parameter in SupervisedInputs.ForClasses())
            pManager.AddParameter(parameter);

        pManager.AddIntegerParameter("Neighbours", "K",
            "How many training samples each prediction consults.\n\n"
            + "One memorises the training set, mislabelled samples included. A large count smooths "
            + "towards the commonest class and lets it outvote a rare one everywhere. An odd count "
            + "avoids tied votes between two classes.",
            GH_ParamAccess.item, 5);

        pManager.AddParameter(SupervisedInputs.Weighting());

        pManager.AddBooleanParameter("Standardise", "S", SupervisedInputs.StandardiseDescription,
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Result", "R", "The predicted class per test sample.", GH_ParamAccess.list);

        pManager.AddNumberParameter("Confidence", "C",
            "The share of the vote the predicted class received. Sort by it to find the predictions "
            + "worth checking by hand — the ones near an even split.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Probabilities", "P",
            "One branch per test sample, holding the share of the vote for each class, in the order of "
            + "Classes.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Classes", "Cl", "The classes, in the order Probabilities lists them.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Neighbours", "N",
            "One branch per test sample, holding the positions of the training samples it was predicted "
            + "from, nearest first. This is the explanation for the prediction.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Distances", "D",
            "How far each of those neighbours is, matching Neighbours item for item. A first distance "
            + "far larger than is typical means nothing like this sample was in training, and the "
            + "prediction is a guess.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!SupervisedInputs.TryReadSamples(this, da, out double[,] train, out double[,] test)) return;
        if (!SupervisedInputs.TryReadLabels(this, da, out string[] labels)) return;

        int neighbours = 5;
        int weightingValue = 0;
        bool standardise = true;
        if (!da.GetData(3, ref neighbours)) return;
        if (!da.GetData(4, ref weightingValue)) return;
        if (!da.GetData(5, ref standardise)) return;
        if (!SupervisedInputs.TryWeighting(this, weightingValue, out NeighbourWeighting weighting)) return;

        try
        {
            var classes = ClassLabels.From(labels);
            if (!SupervisedInputs.RequireTwoClasses(this, classes)) return;

            var model = KNearestNeighbours.FitClassifier(train, classes.Encode(labels), classes.Count,
                new KNearestNeighboursOptions { Neighbours = neighbours, Weighting = weighting, Standardise = standardise });

            var prediction = model.Predict(test);
            var (index, distance) = model.Neighbours(test);

            da.SetDataList(0, classes.Decode(prediction.Labels));
            da.SetDataList(1, prediction.Confidence);
            da.SetDataTree(2, Trees.FromRows(prediction.Probabilities));
            da.SetDataList(3, classes.Classes);
            da.SetDataTree(4, Trees.FromRows(index));
            da.SetDataTree(5, Trees.FromRows(distance));

            Message = $"{classes.Count} classes\nk = {neighbours}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
