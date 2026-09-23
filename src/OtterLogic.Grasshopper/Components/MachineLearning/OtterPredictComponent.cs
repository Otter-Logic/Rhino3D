using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Inference;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// Runs a trained model file on rows of inputs.
/// <para>
/// Adapter only; opening the file and running it belong to <see cref="OnnxModel"/>.
/// What this adds is the cache: one model per component, rebuilt only when the file
/// changes, because opening one is expensive and Grasshopper re-solves on every
/// slider move.
/// </para>
/// </summary>
public sealed class OtterPredictComponent : GH_Component
{
    private OnnxModel? _model;

    public OtterPredictComponent()
        : base("OtterPredict", "Predict",
               "Answer for each sample with a trained model — a class with its confidence, or a number.\n\n"
               + "Wire the model file first, with nothing else. Feature Names and Report say what it "
               + "predicts, which inputs it expects and in what order, and how it scored when it was "
               + "trained. Then wire Inputs with those columns in that order. The model runs on your "
               + "machine; nothing to install beyond OtterLogic.\n\n"
               + "A model is one .onnx file, whoever trained it: OtterTrain writes one, and one from a "
               + "colleague works the same way.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("7f6a2c0e-3d5b-4b1a-9c2e-5e8d1f0a6b42");

    // The cores tier, beside OtterCluster and OtterTrain: the file is the thing a
    // person has, and this is what they do with it.
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords
        => new[] { "predict", "prediction", "inference", "onnx", "model", "surrogate" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("otterpredict", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Model", "M",
            "Path to a model file (.onnx). Reopened only when the file changes.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Inputs", "X",
            "The samples to answer for, one branch each, holding the model's features in the order "
            + "Feature Names gives.\n\n"
            + "Optional: with nothing wired the component still opens the model and fills Feature Names "
            + "and Report, which is how to find out what to wire.",
            GH_ParamAccess.tree);
        pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter("Prediction", "P",
            "One per sample: the class name for a classifier, the value for a regressor. The score on "
            + "samples the model never saw is in Report; there is no separate scoring step.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Confidence", "C",
            "For a classifier, the probability of the class it chose, per sample. Sort by it to find the "
            + "answers worth checking by hand. Empty for a regressor, which has no honest number to give.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Feature Names", "FN",
            "The columns the model expects, in the order it expects them. Empty for a model from "
            + "elsewhere that carries no names.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Report", "Rp",
            "What the model predicts, what it was trained on, and how it scored on groups it never saw.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        string path = string.Empty;
        if (!da.GetData(0, ref path)) return;

        if (!TryOpen(path)) return;
        var model = _model!;

        da.SetDataList(2, model.FeatureNames);
        da.SetDataList(3, model.Describe());

        string kind = model.Task == ModelTask.Classification ? "class" : "number";
        Message = $"{kind}\n{model.FeatureCount} features";

        if (!model.HasMetadata)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "This model carries no OtterLogic metadata, so its features are unnamed and a class comes "
                + "back as an index. It runs, but nothing can check the columns are the right ones.");

        if (!da.GetDataTree(1, out GH_Structure<GH_Number> tree) || tree.DataCount == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Nothing wired to Inputs. Feature Names says what to wire, in what order.");
            return;
        }

        if (!TrainingData.TryRead(tree, out double[,] rows, out string? problem, minimumSamples: 1))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Inputs: " + problem);
            return;
        }

        ModelPrediction prediction;
        try
        {
            prediction = model.Predict(rows);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        if (prediction.Task == ModelTask.Classification)
        {
            da.SetDataList(0, prediction.Labels.Select(label => new GH_String(label)));
            da.SetDataList(1, prediction.Confidence);
        }
        else
        {
            da.SetDataList(0, prediction.Values.Select(value => new GH_Number(value)));
        }

        Message = $"{kind}\n{prediction.SampleCount} samples";
    }

    /// <summary>Opens the file, or keeps the open one if the file is unchanged. Reports its own errors.</summary>
    private bool TryOpen(string path)
    {
        if (_model is not null
            && string.Equals(_model.Path, path, StringComparison.OrdinalIgnoreCase)
            && File.Exists(path)
            && File.GetLastWriteTimeUtc(path) == _model.LastWriteTimeUtc)
            return true;

        Close();

        try
        {
            _model = OnnxModel.Load(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException
                                       or UnauthorizedAccessException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return false;
        }
    }

    private void Close()
    {
        _model?.Dispose();
        _model = null;
    }

    public override void RemovedFromDocument(GH_Document document)
    {
        Close();
        base.RemovedFromDocument(document);
    }
}
