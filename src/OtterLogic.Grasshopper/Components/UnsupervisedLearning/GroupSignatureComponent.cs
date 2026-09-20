using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// What sets each group of a labelling apart from the rest, feature by feature.
/// <para>
/// Adapter only. The measures and the ranking belong to <see cref="GroupSignature"/>;
/// the summary wording to <see cref="GroupSignatureResult.Summary"/>.
/// </para>
/// </summary>
public sealed class GroupSignatureComponent : GH_Component
{
    public GroupSignatureComponent()
        : base("Group Signature", "Signature",
               "Explain a clustering: for every group, which features set it apart from the rest of "
               + "the samples, in which direction, and how strongly — ranked, so the first feature "
               + "listed is the one that most defines the group.\n\n"
               + "A method hands back cluster numbers; this says what they mean, so a group can be "
               + "named, checked and built on. Works on any labelling — a method's Result, a "
               + "consensus, a grouping made by hand. Wire the samples in the units you want to read "
               + "(usually the raw values, not the prepared ones): the separation and effect size are "
               + "unchanged by scaling, and the means only make sense in real units.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("7d65ca22-55b3-4cbd-b66d-60ced22da7bc");

    // Reads a method's output rather than producing one, beside Cluster Quality.
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("groupsignature", 24);

    private const int DefaultTop = 3;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T",
            "The samples to describe, in the units you want to read the answer in. "
            + TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Labels", "R",
            "A group per sample, in the same order — any method's Result. -1 samples get no "
            + "signature of their own but count as part of the rest; give them a label to describe "
            + "them.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Feature Names", "N",
            "Optional. A name per column of Training Inputs, used in Summary and Top Features. "
            + "Unwired, columns are called Feature 0, Feature 1, ...",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Top", "K",
            "How many features Summary and Top Features list per group. The other outputs always "
            + "hold every feature.",
            GH_ParamAccess.item, DefaultTop);

        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Summary", "S",
            "Every group's strongest features, with direction, separation, effect size and the "
            + "means compared. Read it in a panel.",
            GH_ParamAccess.item);

        pManager.AddTextParameter("Top Features", "F",
            "One branch per group: its strongest features as short tags, such as \"Length ↑\" — "
            + "ready to label geometry with.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Ranking", "O",
            "One branch per group: every feature's column index, most separating first.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Separation", "D",
            "One branch per group, a value per feature, -1 to 1 (Cliff's delta): the chance a "
            + "member is above a non-member, less the chance it is below. 1 means every member is "
            + "above every other sample; 0 means no difference. Unaffected by units or outliers.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Effect Size", "E",
            "One branch per group, a value per feature (Cohen's d): the gap between the group's "
            + "mean and the rest's, in standard deviations. Says how far apart, where Separation "
            + "says how cleanly.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Group Means", "M",
            "One branch per group: the mean of each feature within it, in the units wired in.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Rest Means", "A",
            "One branch per group: the mean of each feature over every sample outside it.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Sizes", "Z",
            "Samples in each group.",
            GH_ParamAccess.list);
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

        var labels = new List<int>();
        if (!da.GetDataList(1, labels)) return;

        var names = new List<string>();
        if (Params.Input[2].VolatileDataCount > 0 && !da.GetDataList(2, names)) return;

        int top = DefaultTop;
        if (!da.GetData(3, ref top)) return;

        int features = data.GetLength(1);
        if (names.Count > 0 && names.Count != features)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{names.Count} name(s) for {features} column(s). Columns without a name are called "
                + "by their index.");

        if (top < 1)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Top must be at least 1.");
            return;
        }

        try
        {
            var result = GroupSignature.Describe(data, labels.ToArray());
            int shown = Math.Min(top, features);

            var tags = new DataTree<string>();
            for (int g = 0; g < result.Groups; g++)
            {
                var path = new GH_Path(g);
                tags.EnsurePath(path);
                if (result.Sizes[g] == 0)
                    continue;

                foreach (int j in result.Ranking[g].Take(shown))
                {
                    double separation = result.Separation[g, j];
                    string arrow = separation > 0.0 ? "↑" : separation < 0.0 ? "↓" : "=";
                    tags.Add($"{GroupSignatureResult.FeatureName(names, j)} {arrow}", path);
                }
            }

            // The group whose best feature still barely separates it is the one a
            // user would otherwise trust without reason: it differs from the rest
            // in no single feature, only perhaps in a combination.
            var weak = Enumerable.Range(0, result.Groups)
                .Where(g => result.Sizes[g] > 0
                    && GroupSignatureResult.Magnitude(result.Separation[g, result.Ranking[g][0]])
                        is "negligible" or "small")
                .ToArray();
            if (weak.Length > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Group(s) {string.Join(", ", weak)} differ from the rest by no more than a small "
                    + "separation in any single feature. They may be real but defined by a combination "
                    + "of features — or they may not be real groups at all.");

            da.SetData(0, result.Summary(names, top));
            da.SetDataTree(1, tags);
            da.SetDataTree(2, Trees.FromBuckets(result.Ranking));
            da.SetDataTree(3, Trees.FromRows(result.Separation));
            da.SetDataTree(4, Trees.FromRows(result.Effect));
            da.SetDataTree(5, Trees.FromRows(result.Means));
            da.SetDataTree(6, Trees.FromRows(result.RestMeans));
            da.SetDataList(7, result.Sizes);

            Message = $"{result.Sizes.Count(s => s > 0)} groups\n{features} features";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
