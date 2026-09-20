using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Data;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// Holds back whole groups for testing.
/// <para>
/// Adapter only. Which groups are held back, and why never rows, belongs to
/// <see cref="GroupSplit"/>; this picks the matching branches out of the trees.
/// </para>
/// </summary>
public sealed class SplitByGroupComponent : GH_Component
{
    public SplitByGroupComponent()
        : base("Split By Group", "GroupSplit",
               "Divide samples into a set to fit on and a set to test on, keeping every group whole.\n\n"
               + "Samples from one model resemble each other far more than they resemble the next model's. "
               + "Split them at random and the test set is full of near-copies of the training set, so the "
               + "score says how well the method recognises a project it has seen, which is reliably higher "
               + "than it manages on one it has not. Holding back whole models measures what you actually "
               + "want to know. There is no split by sample here, on purpose.\n\n"
               + "Change the Seed to see how much the score depends on which models were held back. With "
               + "only a few models, it will be a lot, and that is worth knowing.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("ffc58c7e-401d-4eba-b915-316ccadbfe47");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("splitbygroup", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddTextParameter("Groups", "G",
            "The group of every sample — from Read Dataset, the model it came from.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Features", "X",
            "Optional. One branch per sample, in the same order as Groups.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Targets", "Y",
            "Optional. One branch per sample, in the same order as Groups.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Test Fraction", "T",
            "Share of the groups to hold back, rounded to a whole number of groups and never all or "
            + "none of them.",
            GH_ParamAccess.item, 0.25);

        pManager.AddIntegerParameter("Random Seed", "S",
            "Decides which groups are held back. Fixed by default so the split, and the score, stay put "
            + "while Grasshopper re-solves.",
            GH_ParamAccess.item, 1);

        pManager[1].Optional = true;
        pManager[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Train Features", "Xa", "Features of the samples to fit on.", GH_ParamAccess.tree);
        pManager.AddTextParameter("Train Targets", "Ya", "Targets of the samples to fit on.", GH_ParamAccess.tree);
        pManager.AddNumberParameter("Test Features", "Xb", "Features of the samples held back.", GH_ParamAccess.tree);
        pManager.AddTextParameter("Test Targets", "Yb", "Targets of the samples held back.", GH_ParamAccess.tree);
        pManager.AddIntegerParameter("Train Indices", "Ia", "Positions of the samples to fit on.", GH_ParamAccess.list);
        pManager.AddIntegerParameter("Test Indices", "Ib", "Positions of the samples held back.", GH_ParamAccess.list);
        pManager.AddTextParameter("Test Groups", "Gb",
            "The groups held back. Quote them with any score: it is a score on these.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var groups = new List<string>();
        double fraction = 0.25;
        int seed = 1;

        if (!da.GetDataList(0, groups)) return;
        da.GetDataTree(1, out GH_Structure<GH_Number> features);
        da.GetDataTree(2, out GH_Structure<GH_String> targets);
        if (!da.GetData(3, ref fraction)) return;
        if (!da.GetData(4, ref seed)) return;

        if (!TextData.TryReadList(groups, "Groups", out string[] groupOf, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        foreach (var (name, count) in new[] { ("Features", features.PathCount), ("Targets", targets.PathCount) })
        {
            if (count != 0 && count != groupOf.Length)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"{name} has {count} branches and there are {groupOf.Length} groups. One branch per sample, "
                    + "in the same order.");
                return;
            }
        }

        try
        {
            var split = GroupSplit.Holdout(groupOf, fraction, seed);

            da.SetDataTree(0, Pick(features, split.TrainRows));
            da.SetDataTree(1, Pick(targets, split.TrainRows));
            da.SetDataTree(2, Pick(features, split.TestRows));
            da.SetDataTree(3, Pick(targets, split.TestRows));
            da.SetDataList(4, split.TrainRows);
            da.SetDataList(5, split.TestRows);
            da.SetDataList(6, split.TestGroups);

            Message = $"{split.TrainGroups.Length} + {split.TestGroups.Length} groups\n"
                + $"{split.TestRowFraction:P0} of samples held back";

            if (split.TestGroups.Length == 1)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Only '{split.TestGroups[0]}' is held back, so the score will be that one model's. "
                    + "Try other seeds before believing it.");
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    /// <summary>
    /// The branches at <paramref name="rows"/>, renumbered from zero — so that branch i
    /// of every output is sample i of that side, which is the contract every method
    /// downstream reads a tree by.
    /// </summary>
    private static GH_Structure<T> Pick<T>(GH_Structure<T> tree, int[] rows) where T : IGH_Goo
    {
        var picked = new GH_Structure<T>();
        if (tree.PathCount == 0)
            return picked;

        for (int i = 0; i < rows.Length; i++)
            picked.AppendRange(tree.Branches[rows[i]], new GH_Path(i));

        return picked;
    }
}
