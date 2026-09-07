using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using OtterLogic.MachineLearning.Clustering;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// Groups members by their demand, using a Gaussian mixture over whitened
/// principal components.
/// <para>
/// Adapter only. Every decision about preprocessing, decomposition and fitting
/// belongs to <see cref="DesignGrouping"/>; this unpacks a data tree, calls it,
/// and packs the answer back out.
/// </para>
/// <para>
/// Nothing is pre-trained. The mixture computes its parameters from whatever is
/// on the wire, on every solve — so there is no model file, no Python, and
/// nothing for a user to install beyond the plug-in itself.
/// </para>
/// </summary>
public sealed class ClusterComponent : ClusterComponentBase
{
    public ClusterComponent()
        : base("Cluster Design Groups", "Cluster",
               "Group members that want the same detail, from their degree-of-freedom demands. "
               + "Reports how confident it is about each one, and what each group looks like in "
               + "the units the data came in as.")
    {
    }

    public override Guid ComponentGuid => new("820c786a-42a0-42f3-8956-746369248c83");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("cluster", 24);

    protected override int GroupParameterCount => 1;

    protected override void RegisterGroupParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Groups", "K",
            "How many groups to look for. Choose Group Count sweeps a range and plots the criteria "
            + "if you are not sure.",
            GH_ParamAccess.item, 4);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Labels", "L",
            "Group index per member, in the order the branches arrived. Groups are numbered largest "
            + "first, so the numbering does not shuffle when the input changes slightly.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Groups", "G",
            "Member indices bucketed by group, one branch each — ready to drive geometry without "
            + "sorting on the canvas.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Responsibilities", "R",
            "One branch per member, holding its probability of belonging to each group. Sums to one. "
            + "This is what a mixture gives you that k-means cannot.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Confidence", "C",
            "The largest responsibility for each member. Anything much below 0.6 is sitting between "
            + "two groups and is worth looking at by hand rather than taking on trust.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Centres", "M",
            "One branch per group, holding that group's centre back in the original units. This is "
            + "what lets you name a group — \"the high-moment family\" — rather than just number it.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Report", "?",
            "Convergence, parameter count, variance retained, BIC and how confident the assignments "
            + "are overall. Wire it to a panel when a result looks wrong.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!TryRead(da, out double[,] data, out DesignGroupingOptions options))
            return;

        int groups = 4;
        if (!da.GetData(1, ref groups)) return;

        try
        {
            // Group validates its own settings and throws carrying the message
            // to show, so a second copy of those rules here would only be a
            // second thing to keep in step with them.
            DesignGroupingResult result = DesignGrouping.Group(data, options with { Groups = groups });

            if (!result.Mixture.Converged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The fit hit its iteration cap after {result.Mixture.Iterations} rounds without "
                    + "settling. The groups are usable but not final — fewer groups, or a different "
                    + "seed, usually settles it.");

            if (result.KeptColumns.Length < result.InputColumnCount)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{result.InputColumnCount - result.KeptColumns.Length} column(s) had no variation "
                    + "and were dropped. Normal for a planar frame, where the out-of-plane degrees of "
                    + "freedom are identically zero.");

            int weak = result.Confidence.Count(c => c < 0.6);
            if (weak > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{weak} member(s) sit below 0.6 confidence, between two groups. Check Confidence.");

            da.SetDataList(0, result.Labels);
            da.SetDataTree(1, ToTree(result.Groups()));
            da.SetDataTree(2, ToTree(result.Responsibilities));
            da.SetDataList(3, result.Confidence);
            da.SetDataTree(4, ToTree(result.Centres));
            da.SetData(5, result.Report());

            Message = $"{groups} groups\n{result.Confidence.Average():0.00} confidence";
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

    private static DataTree<int> ToTree(int[][] buckets)
    {
        var tree = new DataTree<int>();
        for (int c = 0; c < buckets.Length; c++)
            tree.AddRange(buckets[c], new GH_Path(c));

        return tree;
    }

    private static DataTree<double> ToTree(double[,] rows)
    {
        var tree = new DataTree<double>();
        int width = rows.GetLength(1);

        for (int i = 0; i < rows.GetLength(0); i++)
        {
            var path = new GH_Path(i);
            for (int j = 0; j < width; j++)
                tree.Add(rows[i, j], path);
        }

        return tree;
    }
}
