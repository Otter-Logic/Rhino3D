using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Data;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// Writes this definition's samples into a dataset folder, as one model among many.
/// <para>
/// Adapter only. The folder, the schema check and the file format belong to
/// <see cref="DatasetFolder"/>; this unpacks the wires and reports what happened.
/// </para>
/// </summary>
public sealed class WriteDatasetComponent : GH_Component
{
    public WriteDatasetComponent()
        : base("Write Dataset", "WriteData",
               "Save this model's samples into a dataset folder, to train on later together with the "
               + "samples from other models.\n\n"
               + "A dataset is gathered one model at a time: open a model, wire its features and the "
               + "answers you already know, give it a Model ID, and write. Writing the same Model ID again "
               + "replaces that model's rows, so it is safe to leave switched on while you work. The folder "
               + "remembers its columns, and refuses rows whose columns differ — which is what stops the "
               + "twentieth model going in with two inputs swapped.\n\n"
               + "Read it back with Read Dataset. To fit a model on the samples in this definition alone, "
               + "skip the folder and wire them straight into a method in Supervised Learning.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("0ce82461-a4e9-4ce9-a197-1f8777ee81ee");

    // The data tier of the panel: what happens before any feature preparation.
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("writedataset", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Folder", "F",
            "The dataset folder. Created if it is not there. One folder per question — samples that "
            + "answer different questions do not share one.",
            GH_ParamAccess.item);

        pManager.AddTextParameter("Model ID", "ID",
            "Names this model's file, and becomes the group its rows belong to.\n\n"
            + "Use something that identifies the project for good, such as a job number. Testing is done "
            + "by holding back whole models, so the same model written under two names would end up on "
            + "both sides and flatter every score.",
            GH_ParamAccess.item);

        pManager.AddTextParameter("Feature Names", "FN",
            "One name per feature, in the order the values sit in each branch.\n\n"
            + "Not optional, and not decoration: order is all a model knows about its inputs, so the "
            + "names are the only thing that can catch two of them being swapped six months from now.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Features", "X",
            TrainingData.Description + "\n\nMake them independent of the model's size and of the software "
            + "it came from — ratios, angles, counts — or what is learned will not carry to the next model.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Target Names", "TN",
            "One name per target — the things to be predicted.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Targets", "Y",
            "One branch per sample, in the same order as Features, holding that sample's known answer "
            + "for each target.",
            GH_ParamAccess.tree);

        pManager.AddBooleanParameter("Number Targets", "N",
            "True for a target that is a quantity, false for one that is a class. One value for all "
            + "targets, or one per target.\n\n"
            + "False by default, and deliberately not guessed: classes are very often written 0 and 1, "
            + "and treated as a quantity those would train a model that answers 0.37.",
            GH_ParamAccess.list, false);

        pManager.AddTextParameter("Ids", "I",
            "Optional. One name per sample, saved beside it so a prediction or a mistake can be traced "
            + "back to the thing it was about. Never learned from.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Extractor Version", "V",
            "Change this whenever you change how the features are computed.\n\n"
            + "Rows from two versions are not the same measurement even when the names match, so the "
            + "folder refuses to mix them. Start a new folder, or re-export the earlier models.",
            GH_ParamAccess.item, "1");

        pManager.AddBooleanParameter("Write", "W",
            "Set true to write. While true, the file is rewritten whenever the inputs change, and left "
            + "untouched when they have not.",
            GH_ParamAccess.item, false);

        pManager[4].Optional = true;
        pManager[5].Optional = true;
        pManager[7].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("File", "F", "The file written for this model.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Rows", "R", "Samples written.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "Rp",
            "What this model's table holds: class balance, and any feature that never changes.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        string folder = string.Empty;
        string modelId = string.Empty;
        string version = "1";
        bool write = false;
        var featureNames = new List<string>();
        var targetNames = new List<string>();
        var numberFlags = new List<bool>();
        var ids = new List<string>();

        if (!da.GetData(0, ref folder)) return;
        if (!da.GetData(1, ref modelId)) return;
        if (!da.GetDataList(2, featureNames)) return;
        if (!da.GetDataTree(3, out GH_Structure<GH_Number> featureTree)) return;
        da.GetDataList(4, targetNames);
        da.GetDataTree(5, out GH_Structure<GH_String> targetTree);
        da.GetDataList(6, numberFlags);
        da.GetDataList(7, ids);
        da.GetData(8, ref version);
        da.GetData(9, ref write);

        if (!TrainingData.TryRead(featureTree, out double[,] features, out string? problem, minimumSamples: 1))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        string[,]? targets = null;
        if (targetNames.Count > 0 && !TextData.TryReadRows(targetTree, "Targets", out targets, out problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        if (numberFlags.Count != 1 && numberFlags.Count != targetNames.Count && targetNames.Count > 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Number Targets takes one value for all targets or one per target. There are {targetNames.Count} "
                + $"targets and {numberFlags.Count} values.");
            return;
        }

        var isNumber = targetNames.Select((_, t) => numberFlags.Count == 1 ? numberFlags[0] : numberFlags[t]).ToArray();

        string[]? idColumn = null;
        if (ids.Count > 0 && !TextData.TryReadList(ids, "Ids", out idColumn, out problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        try
        {
            var dataset = Dataset.FromColumns(
                featureNames.Select(n => (n ?? string.Empty).Trim()).ToArray(), features,
                targetNames.Select(n => (n ?? string.Empty).Trim()).ToArray(), targets, isNumber,
                idColumn, version);

            da.SetDataList(2, dataset.Describe());

            if (targetNames.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "No targets are wired, so these rows carry nothing to learn from. That is fine for "
                    + "samples you mean to label later.");

            if (!write)
            {
                Message = "Not written";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"The table is valid — {dataset.RowCount} rows. Set Write to true to save it.");
                return;
            }

            var result = DatasetFolder.Write(folder, modelId, dataset);

            // A class the folder had not met is news either way: a real new class,
            // or an existing one spelt differently and about to become a category.
            foreach (var (column, name) in result.ClassesAdded)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"'{name}' is new to target '{column}' in this dataset. If it is a misspelling of an "
                    + "existing class, fix it and write again — the file is replaced, but schema.json keeps "
                    + "the class until it is edited out.");

            da.SetData(0, result.Path);
            da.SetData(1, result.Rows);
            Message = $"{result.Rows} rows";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException
                                       or InvalidDataException or UnauthorizedAccessException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
