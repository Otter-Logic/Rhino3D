using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Decomposition;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// Principal component analysis: samples rotated onto the directions they vary
/// most in, keeping the few that matter.
/// <para>
/// Adapter only. The decomposition belongs to <see cref="PrincipalComponents"/>.
/// </para>
/// </summary>
public sealed class PrincipalComponentsComponent : GH_Component
{
    public PrincipalComponentsComponent()
        : base("Principal Components", "PCA",
               "Rotate samples onto the directions they vary most in, and keep only as many of those "
               + "directions as the data needs.\n\n"
               + "Three jobs in one: it drops columns that say the same thing twice, it drops "
               + "directions with no variance at all (a flat case of a 3D quantity), and with two or "
               + "three components kept it turns any number of columns into points you can draw. "
               + "Scale columns in different units first with Prepare Features, or the largest units "
               + "decide the directions.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("1fa6d630-e722-4eef-9c86-12d5a3cd4690");

    // The features tier: it prepares samples for a method rather than grouping them.
    public override GH_Exposure Exposure => GH_Exposure.secondary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("principalcomponents", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T", TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Components", "K",
            "Optional. Keep exactly this many components — the choice when something downstream "
            + "needs a fixed number of columns, such as 2 or 3 to draw. Leave unwired to keep "
            + "however many Variance asks for.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Variance", "V",
            "When Components is unwired: keep the fewest components that together carry this share "
            + "of the total variance, 0 to 1. Directions with essentially none are always dropped.",
            GH_ParamAccess.item, 0.99);

        pManager.AddBooleanParameter("Whiten", "W",
            "Scale every kept component to unit variance, so each counts the same downstream. On "
            + "suits clustering; turn off to keep the real spread along each direction — what you "
            + "want when drawing the result.",
            GH_ParamAccess.item, true);

        pManager.AddNumberParameter("Map Back", "B",
            "Optional. Rows in component space to map back into the original columns — wire a "
            + "method's centroids here to read them in the units the data arrived in. One branch "
            + "per row.",
            GH_ParamAccess.tree);

        pManager[1].Optional = true;
        pManager[4].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Projected", "P",
            "One branch per sample, holding its coordinates along the kept components.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Components", "C",
            "One branch per component, holding how much each input column contributes to it. The "
            + "largest values say what that direction is made of.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Explained Variance", "E",
            "Variance along each kept component, largest first.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Retained Ratio", "R",
            "Share of the total variance the kept components carry, 0 to 1. What was dropped is "
            + "gone from everything downstream.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Mean", "M",
            "Mean of each input column, subtracted before projecting.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Mapped Back", "B",
            "Map Back's rows in the original columns, one branch each. Exact when every component "
            + "was kept; otherwise the nearest point the kept components can express.",
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

        int? count = null;
        int requested = 0;
        if (da.GetData(1, ref requested))
            count = requested;

        double variance = 0.99;
        bool whiten = true;
        if (!da.GetData(2, ref variance)) return;
        if (!da.GetData(3, ref whiten)) return;

        if (count is null && (variance <= 0.0 || variance > 1.0))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Variance is {variance}; it is a share of the total, above 0 and at most 1.");
            return;
        }

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
            var pca = count is { } k
                ? PrincipalComponents.FitCount(data, k, whiten)
                : PrincipalComponents.Fit(data, variance, whiten);

            if (count is { } asked && pca.Count < asked)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Asked for {asked} components but the data has only {pca.Count} column(s) to give.");

            if (whiten && pca.ExplainedVariance.Any(v => v <= 0.0))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "A kept component has no variance, so whitening divides by zero along it. Lower "
                    + "Components, or leave it unwired so empty directions are dropped.");

            da.SetDataTree(0, Trees.FromRows(pca.Transform(data)));
            da.SetDataTree(1, Trees.FromRows(pca.Components));
            da.SetDataList(2, pca.ExplainedVariance);
            da.SetData(3, pca.ExplainedVarianceRatio);
            da.SetDataList(4, pca.Mean);

            if (mapBack is not null)
                da.SetDataTree(5, Trees.FromRows(pca.InverseTransform(mapBack)));

            Message = $"{pca.Count} of {data.GetLength(1)}\n{pca.ExplainedVarianceRatio:P0} kept";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
