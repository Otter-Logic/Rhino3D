using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using OtterLogic.MachineLearning.Data;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// Reads every model in a dataset folder back as one table.
/// <para>
/// Adapter only; the reading and its checks belong to <see cref="DatasetFolder"/>.
/// </para>
/// </summary>
public sealed class ReadDatasetComponent : GH_Component
{
    public ReadDatasetComponent()
        : base("Read Dataset", "ReadData",
               "Read every model in a dataset folder as one table, each sample tagged with the model it "
               + "came from.\n\n"
               + "Read the Report before fitting anything. It says how the classes are balanced — which "
               + "decides what a good score even is — and lists any feature that never changes. Then wire "
               + "Groups into Split By Group, so that what you test on comes from models the fit never saw.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("6ddec67f-cc1f-4ae9-b92c-9231e7a75424");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("readdataset", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Folder", "F",
            "A dataset folder written by Write Dataset. It is read when this component solves; recompute "
            + "after writing another model into it.",
            GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Feature Names", "FN", "One per feature, in branch order.", GH_ParamAccess.list);
        pManager.AddNumberParameter("Features", "X", "One branch per sample.", GH_ParamAccess.tree);
        pManager.AddTextParameter("Target Names", "TN", "One per target, in branch order.", GH_ParamAccess.list);
        pManager.AddTextParameter("Targets", "Y",
            "One branch per sample, holding its answer for each target. List Item on this tree picks "
            + "one target out as the labels or values a method wants.",
            GH_ParamAccess.tree);
        pManager.AddTextParameter("Groups", "G", "The model each sample came from.", GH_ParamAccess.list);
        pManager.AddTextParameter("Ids", "I", "Each sample's identifier, blank if none were written.", GH_ParamAccess.list);
        pManager.AddTextParameter("Classes", "C",
            "One branch per target, holding that target's classes in the order the dataset numbers them. "
            + "Empty for a number target.",
            GH_ParamAccess.tree);
        pManager.AddTextParameter("Report", "Rp",
            "Rows per model, class balance, and any feature that never changes.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        string folder = string.Empty;
        if (!da.GetData(0, ref folder)) return;

        try
        {
            var dataset = DatasetFolder.Read(folder);
            var targets = dataset.Schema.Targets;

            var classes = new DataTree<string>();
            for (int t = 0; t < targets.Count; t++)
                classes.AddRange(targets[t].Classes, new GH_Path(t));

            da.SetDataList(0, dataset.Schema.Features.Select(c => c.Name));
            da.SetDataTree(1, Trees.FromRows(dataset.Features));
            da.SetDataList(2, targets.Select(c => c.Name));
            da.SetDataTree(3, Trees.FromRows(dataset.TargetsAsText()));
            da.SetDataList(4, dataset.Groups);
            da.SetDataList(5, dataset.Ids);
            da.SetDataTree(6, classes);
            da.SetDataList(7, dataset.Describe());

            int groups = dataset.RowsPerGroup().Count;
            Message = $"{dataset.RowCount} rows\n{groups} model{(groups == 1 ? string.Empty : "s")}";

            if (groups < 2)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "One model so far. A score needs at least two — one to fit on and one, never seen, to "
                    + "test on.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException
                                       or UnauthorizedAccessException)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
