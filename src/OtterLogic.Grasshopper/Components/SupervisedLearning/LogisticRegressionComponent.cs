using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Supervised;
using OtterLogic.Supervised.Linear;

namespace OtterLogic.Grasshopper.Components.SupervisedLearning;

/// <summary>
/// Classification by a weighted sum of the inputs per class.
/// <para>
/// Adapter only. The algorithm belongs to <see cref="LogisticRegression"/>.
/// </para>
/// </summary>
public sealed class LogisticRegressionComponent : GH_Component
{
    public LogisticRegressionComponent()
        : base("Logistic Regression", "Logistic",
               "Predict a class for each sample from a weighted sum of the inputs, with a probability for "
               + "every class.\n\n"
               + "The baseline for predicting a class, and the model to beat: if something far more "
               + "elaborate cannot outscore this on held-back groups, the elaboration has found nothing. "
               + "Coefficients says which inputs argue for which class, and the probabilities are smooth "
               + "where Nearest Neighbour Classifier's move in steps. Its boundaries between classes are "
               + "flat — classes that can only be told apart by inputs curving round each other need Nearest "
               + "Neighbour Classifier, or that combination wired in as an input of its own.",
               Categories.Root, Categories.SupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("19d7f7eb-48bd-4586-9cf3-2deb03b59ef5");

    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("logistic", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        foreach (var parameter in SupervisedInputs.ForClasses())
            pManager.AddParameter(parameter);

        pManager.AddNumberParameter("Regularisation", "A",
            "How strongly large coefficients are discouraged. Must be above zero.\n\n"
            + "That is not fussiness. Where the classes separate perfectly — usual on small, clean data — "
            + "there is no best fit without it: the coefficients grow without limit chasing probabilities "
            + "of exactly one. Raise it when Confidence is near one on almost everything and the score on "
            + "held-back groups is poor.",
            GH_ParamAccess.item, 1.0);

        pManager.AddBooleanParameter("Standardise", "S", SupervisedInputs.StandardiseDescription,
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Result", "R", "The predicted class per test sample.", GH_ParamAccess.list);

        pManager.AddNumberParameter("Confidence", "C",
            "The probability of the predicted class. Sort by it to find the predictions worth checking "
            + "by hand.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Probabilities", "P",
            "One branch per test sample, holding the probability of each class, in the order of Classes.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Classes", "Cl",
            "The classes, in the order Probabilities and Coefficients list them.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Coefficients", "W",
            "One branch per class, holding one value per input. A large positive value means a high "
            + "reading on that input argues for that class. Each input's values sum to zero across the "
            + "classes, so read a branch against the others rather than alone.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Intercepts", "I",
            "One per class. They carry how common each class is.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!SupervisedInputs.TryReadSamples(this, da, out double[,] train, out double[,] test)) return;
        if (!SupervisedInputs.TryReadLabels(this, da, out string[] labels)) return;

        double regularisation = 1.0;
        bool standardise = true;
        if (!da.GetData(3, ref regularisation)) return;
        if (!da.GetData(4, ref standardise)) return;

        try
        {
            var classes = ClassLabels.From(labels);
            if (!SupervisedInputs.RequireTwoClasses(this, classes)) return;

            var model = LogisticRegression.Fit(train, classes.Encode(labels), classes.Count,
                new LogisticRegressionOptions { Regularisation = regularisation, Standardise = standardise });

            if (!model.Converged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Stopped at the iteration cap after {model.Iterations} steps rather than settling. The "
                    + "predictions are usable but not final — this usually means inputs on wildly different "
                    + "scales with Standardise off.");

            var prediction = model.Predict(test);

            da.SetDataList(0, classes.Decode(prediction.Labels));
            da.SetDataList(1, prediction.Confidence);
            da.SetDataTree(2, Trees.FromRows(prediction.Probabilities));
            da.SetDataList(3, classes.Classes);
            da.SetDataTree(4, Trees.FromRows(model.Coefficients));
            da.SetDataList(5, model.Intercepts);

            Message = $"{classes.Count} classes";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
