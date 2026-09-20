using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Supervised;
using OtterLogic.Supervised.Evaluation;

namespace OtterLogic.Grasshopper.Components.SupervisedLearning;

/// <summary>
/// Scores predicted classes against the truth.
/// <para>
/// Adapter only. The scores belong to <see cref="ClassificationReport"/>.
/// </para>
/// </summary>
public sealed class EvaluateClassificationComponent : GH_Component
{
    public EvaluateClassificationComponent()
        : base("Evaluate Classification", "EvalClass",
               "Compare predicted classes with the known ones, for samples the method was not fitted on.\n\n"
               + "Never read Accuracy alone. Most real class targets are lopsided, and on one that is "
               + "nine-tenths a single class, ninety per cent is what you get by ignoring the inputs "
               + "entirely — No-Information Rate is that figure, and Accuracy has to clear it to mean "
               + "anything. Balanced Accuracy and Macro F1 cannot be inflated that way. Then read Confusion: "
               + "two classes that keep being swapped are two classes your inputs cannot tell apart.\n\n"
               + "It scores predictions from anywhere — a method in this panel, or a trained model. For "
               + "quantities, use Evaluate Regression.",
               Categories.Root, Categories.SupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("48735931-0915-42e5-9f98-b070cd671345");

    // The evaluation tier, below the methods.
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("evaluateclassification", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Actual", "A",
            "The known class of every test sample.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Predicted", "P",
            "The predicted class of every test sample, in the same order.",
            GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Accuracy", "Acc", "Share of predictions that were right.", GH_ParamAccess.item);

        pManager.AddNumberParameter("No-Information Rate", "NIR",
            "The accuracy of always answering the commonest class. What Accuracy has to beat.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Balanced Accuracy", "BA",
            "The share found of each class, averaged with every class counting the same however rare. "
            + "One over the number of classes is guessing.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Macro F1", "F1",
            "Precision and recall combined per class, then averaged with every class counting the same.",
            GH_ParamAccess.item);

        pManager.AddTextParameter("Classes", "Cl",
            "The classes, in the order every per-class output lists them.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Confusion", "M",
            "One branch per actual class, holding how many of its samples were predicted as each class. "
            + "The diagonal is what was right; everything else is a specific mistake.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Precision", "Pr",
            "Per class: of the samples predicted as it, the share that were.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Recall", "Re",
            "Per class: of the samples that were it, the share found.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Support", "N", "Per class: how many test samples truly were it.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var actualRaw = new List<string>();
        var predictedRaw = new List<string>();
        if (!da.GetDataList(0, actualRaw)) return;
        if (!da.GetDataList(1, predictedRaw)) return;

        if (!TextData.TryReadList(actualRaw, "Actual", out string[] actual, out string? problem)
            || !TextData.TryReadList(predictedRaw, "Predicted", out string[] predicted, out problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        try
        {
            // Both lists, so a class that was predicted but never occurred still
            // has a column to be wrong in.
            var classes = ClassLabels.From(actual.Concat(predicted));
            var report = ClassificationReport.From(
                classes.Encode(actual), classes.Encode(predicted), Math.Max(classes.Count, 2));

            if (report.Accuracy <= report.NoInformationRate)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Accuracy {report.Accuracy:P1} does not beat the {report.NoInformationRate:P1} that always "
                    + "answering the commonest class would score. On this evidence nothing has been learned.");

            da.SetData(0, report.Accuracy);
            da.SetData(1, report.NoInformationRate);
            da.SetData(2, report.BalancedAccuracy);
            da.SetData(3, report.MacroF1);
            da.SetDataList(4, classes.Classes);
            da.SetDataTree(5, Trees.FromRows(Square(report.Confusion, classes.Count)));
            da.SetDataList(6, report.Precision.Take(classes.Count));
            da.SetDataList(7, report.Recall.Take(classes.Count));
            da.SetDataList(8, report.Support.Take(classes.Count));

            Message = $"{report.Accuracy:P1}\nvs {report.NoInformationRate:P1}";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    /// <summary>
    /// The leading <paramref name="size"/> rows and columns. The report always has at
    /// least two classes; a test set where everything is one class has only one to show.
    /// </summary>
    private static int[,] Square(int[,] confusion, int size)
    {
        var trimmed = new int[size, size];
        for (int a = 0; a < size; a++)
            for (int p = 0; p < size; p++)
                trimmed[a, p] = confusion[a, p];

        return trimmed;
    }
}
