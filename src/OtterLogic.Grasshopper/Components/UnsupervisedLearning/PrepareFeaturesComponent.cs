using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Preprocessing;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// Samples put on a common scale before a method sees them: optional log and
/// row normalisation, then standardisation and per-column weights.
/// <para>
/// Adapter only. The transform belongs to <see cref="FeaturePipeline"/>.
/// </para>
/// </summary>
public sealed class PrepareFeaturesComponent : GH_Component
{
    public PrepareFeaturesComponent()
        : base("Prepare Features", "Prepare",
               "Put every column on a common scale before clustering: each is centred and divided by "
               + "its spread, so a column in millimetres cannot outvote one in degrees. Columns with "
               + "no variation are dropped, since they carry nothing and break the methods that "
               + "estimate a covariance.\n\n"
               + "Most of the quality of a grouping is decided here, not in the method. Log Transform "
               + "is for magnitudes with a few huge values and a long tail of small ones; Normalise "
               + "Rows groups samples by the proportion between their values rather than their size; "
               + "Weights says which columns should count for more.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("888e9e10-2a9f-4641-bba6-ed7cde620d31");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("preparefeatures", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T", TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddBooleanParameter("Log Transform", "G",
            "Take log(1 + x) of every value first. For right-skewed magnitudes — a handful of very "
            + "large values and many small ones. Needs every value to be zero or more.",
            GH_ParamAccess.item, false);

        pManager.AddBooleanParameter("Normalise Rows", "N",
            "Scale each sample to unit length first, discarding its overall size and keeping only "
            + "the proportion between its values. Groups samples alike in shape whatever their size, "
            + "and changes the answer more than any other switch here.",
            GH_ParamAccess.item, false);

        pManager.AddNumberParameter("Weights", "W",
            "Optional. One multiplier per column, applied after scaling: 1 counts a column the same "
            + "as the rest, 2 double, 0 leaves it out. Takes effect through Principal Components "
            + "downstream, which a heavier column pulls toward itself.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Map Back", "B",
            "Optional. Rows in prepared space to map back into the original units — wire a method's "
            + "centroids here to read them as the data arrived. One branch per row, one value per "
            + "kept column.",
            GH_ParamAccess.tree);

        pManager[3].Optional = true;
        pManager[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Prepared", "P",
            "One branch per sample, holding its prepared values for the kept columns only.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Kept Columns", "K",
            "Which input columns Prepared holds, in order.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Dropped Columns", "D",
            "Input columns left out, for having no variation or a weight of zero.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Mapped Back", "B",
            "Map Back's rows in the original units, every input column restored — dropped ones at "
            + "their constant value. With Normalise Rows on, the size divided out cannot return, so "
            + "a row comes back as the proportion between columns.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(0, out GH_Structure<GH_Number> tree))
            return;

        if (!TrainingData.TryRead(tree, out double[,] data, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        bool log = false;
        bool normalise = false;
        if (!da.GetData(1, ref log)) return;
        if (!da.GetData(2, ref normalise)) return;

        var weights = new List<double>();
        if (Params.Input[3].VolatileDataCount > 0 && !da.GetDataList(3, weights)) return;

        double[,]? mapBack = null;
        if (Params.Input[4].VolatileDataCount > 0)
        {
            if (!da.GetDataTree(4, out GH_Structure<GH_Number> rows)) return;
            if (!TrainingData.TryRead(rows, out double[,] read, out problem, minimumSamples: 1))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Map Back: " + problem);
                return;
            }

            mapBack = read;
        }

        try
        {
            var pipeline = FeaturePipeline.Fit(data, log, normalise, weights.Count > 0 ? weights.ToArray() : null);

            int columns = data.GetLength(1);
            var dropped = Enumerable.Range(0, columns).Except(pipeline.KeptColumns).ToArray();

            if (dropped.Length > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Dropped column(s) {string.Join(", ", dropped)}: no variation, or a weight of zero. "
                    + "Prepared holds the kept columns only — see Kept Columns for which.");

            da.SetDataTree(0, Trees.FromRows(pipeline.Transform(data)));
            da.SetDataList(1, pipeline.KeptColumns);
            da.SetDataList(2, dropped);

            if (mapBack is not null)
                da.SetDataTree(3, Trees.FromRows(pipeline.InverseTransform(mapBack)));

            Message = $"{pipeline.KeptColumns.Length} of {columns} columns";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
