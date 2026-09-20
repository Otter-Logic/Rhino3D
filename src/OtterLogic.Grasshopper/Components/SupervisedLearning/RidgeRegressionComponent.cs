using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Supervised.Linear;

namespace OtterLogic.Grasshopper.Components.SupervisedLearning;

/// <summary>
/// Linear regression with a penalty on coefficient size.
/// <para>
/// Adapter only. The algorithm belongs to <see cref="RidgeRegression"/>.
/// </para>
/// </summary>
public sealed class RidgeRegressionComponent : GH_Component
{
    public RidgeRegressionComponent()
        : base("Ridge Regression", "Ridge",
               "Predict a quantity as a weighted sum of the inputs.\n\n"
               + "The baseline for predicting a number, and the one that explains itself: Coefficients says "
               + "which inputs matter, in which direction, and by how much relative to each other. It "
               + "extrapolates beyond the training samples, for better and worse. It can only draw a "
               + "straight line through each input — where the answer goes with the square of something, "
               + "either wire the squared column in as well, or use Nearest Neighbour Regressor, which does "
               + "not care what shape the relationship is.",
               Categories.Root, Categories.SupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("f43ce648-9f96-432e-9122-40a9cc60cab9");

    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("ridge", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        foreach (var parameter in SupervisedInputs.ForValues())
            pManager.AddParameter(parameter);

        pManager.AddNumberParameter("Regularisation", "A",
            "How strongly large coefficients are discouraged.\n\n"
            + "One is negligible against a few hundred samples and decisive against ten, which is the "
            + "right way round. Raise it when Coefficients come out huge and of opposite sign on inputs "
            + "that say nearly the same thing. Zero is plain least squares, which fails outright when one "
            + "input is a combination of others.",
            GH_ParamAccess.item, 1.0);

        pManager.AddBooleanParameter("Standardise", "S", SupervisedInputs.StandardiseDescription,
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Result", "R", "The predicted value per test sample.", GH_ParamAccess.list);

        pManager.AddNumberParameter("Coefficients", "C",
            "One per input, in input order. With Standardise on, each is the change in the prediction for "
            + "one standard deviation of that input, so they can be compared with each other: the largest "
            + "in size is the input the model leans on most. An input that never changes gets zero.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Intercept", "I",
            "With Standardise on, the prediction for a sample that is average in every input.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!SupervisedInputs.TryReadSamples(this, da, out double[,] train, out double[,] test)) return;
        if (!SupervisedInputs.TryReadValues(da, out double[] values)) return;

        double regularisation = 1.0;
        bool standardise = true;
        if (!da.GetData(3, ref regularisation)) return;
        if (!da.GetData(4, ref standardise)) return;

        try
        {
            var model = RidgeRegression.Fit(train, values,
                new RidgeRegressionOptions { Regularisation = regularisation, Standardise = standardise });

            da.SetDataList(0, model.Predict(test));
            da.SetDataList(1, model.Coefficients);
            da.SetData(2, model.Intercept);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
