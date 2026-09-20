using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Supervised.Neighbours;

namespace OtterLogic.Grasshopper.Components.SupervisedLearning;

/// <summary>
/// Regression by the average of the most similar training samples.
/// <para>
/// Adapter only. The algorithm belongs to <see cref="KNearestNeighbours"/>.
/// </para>
/// </summary>
public sealed class NearestNeighbourRegressorComponent : GH_Component
{
    public NearestNeighbourRegressorComponent()
        : base("Nearest Neighbour Regressor", "KNN Value",
               "Predict a quantity for each sample as the average over the training samples most like it.\n\n"
               + "Makes no assumption about the shape of the relationship, so it copes with curves and "
               + "thresholds that Ridge Regression cannot draw, and Neighbours shows where each answer "
               + "came from. It cannot extrapolate: a prediction is an average of values already seen, so "
               + "it never exceeds the largest of them, however large the new sample is. When the new "
               + "samples may lie outside what was trained on, or you want to read off which inputs matter, "
               + "use Ridge Regression. To predict a class rather than a quantity, use Nearest Neighbour "
               + "Classifier.",
               Categories.Root, Categories.SupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("c7094603-ec2b-467e-9dfe-165ca2ca5c61");

    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("knnregressor", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        foreach (var parameter in SupervisedInputs.ForValues())
            pManager.AddParameter(parameter);

        pManager.AddIntegerParameter("Neighbours", "K",
            "How many training samples each prediction averages over.\n\n"
            + "One repeats the nearest sample's value, noise included. A large count smooths towards the "
            + "overall average and flattens the extremes.",
            GH_ParamAccess.item, 5);

        pManager.AddParameter(SupervisedInputs.Weighting());

        pManager.AddBooleanParameter("Standardise", "S", SupervisedInputs.StandardiseDescription,
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Result", "R", "The predicted value per test sample.", GH_ParamAccess.list);

        pManager.AddIntegerParameter("Neighbours", "N",
            "One branch per test sample, holding the positions of the training samples it was averaged "
            + "from, nearest first.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Distances", "D",
            "How far each of those neighbours is, matching Neighbours item for item. A first distance "
            + "far larger than is typical means nothing like this sample was in training.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!SupervisedInputs.TryReadSamples(this, da, out double[,] train, out double[,] test)) return;
        if (!SupervisedInputs.TryReadValues(da, out double[] values)) return;

        int neighbours = 5;
        int weightingValue = 0;
        bool standardise = true;
        if (!da.GetData(3, ref neighbours)) return;
        if (!da.GetData(4, ref weightingValue)) return;
        if (!da.GetData(5, ref standardise)) return;
        if (!SupervisedInputs.TryWeighting(this, weightingValue, out NeighbourWeighting weighting)) return;

        try
        {
            var model = KNearestNeighbours.FitRegressor(train, values,
                new KNearestNeighboursOptions { Neighbours = neighbours, Weighting = weighting, Standardise = standardise });

            var (index, distance) = model.Neighbours(test);

            da.SetDataList(0, model.Predict(test));
            da.SetDataTree(1, Trees.FromRows(index));
            da.SetDataTree(2, Trees.FromRows(distance));

            Message = $"k = {neighbours}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
