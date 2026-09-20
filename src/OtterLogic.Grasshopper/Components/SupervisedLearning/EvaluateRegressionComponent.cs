using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Supervised.Evaluation;

namespace OtterLogic.Grasshopper.Components.SupervisedLearning;

/// <summary>
/// Scores predicted quantities against the truth.
/// <para>
/// Adapter only. The scores belong to <see cref="RegressionReport"/>.
/// </para>
/// </summary>
public sealed class EvaluateRegressionComponent : GH_Component
{
    public EvaluateRegressionComponent()
        : base("Evaluate Regression", "EvalValue",
               "Compare predicted quantities with the known ones, for samples the method was not fitted on.\n\n"
               + "R² says how much better the predictions are than answering with the average every time: "
               + "one is perfect, zero is no better, and below zero — which does happen on held-back groups "
               + "— is worse, meaning what was learned did not carry over. Mean Absolute Error is the "
               + "typical miss in the target's own units, and the number to quote to whoever will use the "
               + "predictions. Max Error is the one an average hides.\n\n"
               + "It scores predictions from anywhere. For classes, use Evaluate Classification.",
               Categories.Root, Categories.SupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("ae7c76be-93ab-4ad7-b71d-21d3de2dab06");

    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("evaluateregression", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Actual", "A", "The known value of every test sample.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Predicted", "P",
            "The predicted value of every test sample, in the same order.", GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("R Squared", "R2",
            "Share of the spread in the known values that the predictions account for. Zero is the bar: "
            + "no better than the average.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Mean Absolute Error", "MAE",
            "The typical miss, in the target's own units.", GH_ParamAccess.item);

        pManager.AddNumberParameter("Root Mean Squared Error", "RMSE",
            "Like the mean absolute error but punishing large misses more. Well above it means a few "
            + "predictions are badly wrong rather than all being slightly so.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Max Error", "Max", "The single largest miss.", GH_ParamAccess.item);

        pManager.AddIntegerParameter("Worst Sample", "W",
            "Position of the sample behind Max Error, so it can be looked at.", GH_ParamAccess.item);

        pManager.AddNumberParameter("Residuals", "E",
            "Predicted minus actual, per sample. Positive is an over-prediction. Plot them against "
            + "Actual: a slope means the method is squeezing everything towards the middle.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var actual = new List<double>();
        var predicted = new List<double>();
        if (!da.GetDataList(0, actual)) return;
        if (!da.GetDataList(1, predicted)) return;

        try
        {
            var report = RegressionReport.From(actual, predicted);

            if (report.RSquared <= 0.0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"R² is {report.RSquared:0.###}: answering with the average every time would have done "
                    + "as well or better.");

            da.SetData(0, report.RSquared);
            da.SetData(1, report.MeanAbsoluteError);
            da.SetData(2, report.RootMeanSquaredError);
            da.SetData(3, report.MaxError);
            da.SetData(4, report.WorstSample);
            da.SetDataList(5, report.Residuals);

            Message = $"R² {report.RSquared:0.00}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
