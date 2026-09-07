using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.MachineLearning.Clustering;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// Fits the same pipeline across a range of group counts and reports the
/// criteria for each, so the count can be chosen by looking rather than guessing.
/// <para>
/// Separate from Cluster Design Groups on purpose. A sweep of seven counts at ten
/// restarts is seventy fits, and that should not re-run every time an unrelated
/// slider moves.
/// </para>
/// <para>
/// It deliberately reports rather than decides. The lowest BIC is a suggestion:
/// the useful count is usually the one that is both near the elbow and means
/// something to whoever has to detail the result, and no criterion knows about
/// the second half of that.
/// </para>
/// </summary>
public sealed class ClusterCountComponent : ClusterComponentBase
{
    public ClusterCountComponent()
        : base("Choose Group Count", "GroupCount",
               "Sweep the number of groups and report how well each count fits. Plot BIC against "
               + "Groups and look for the elbow.")
    {
    }

    public override Guid ComponentGuid => new("7b1196ba-e923-41a6-9ae9-cdde82c87cc2");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("clustercount", 24);

    protected override int GroupParameterCount => 2;

    protected override void RegisterGroupParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Minimum", "Lo", "Lowest group count to try.",
            GH_ParamAccess.item, 2);

        pManager.AddIntegerParameter("Maximum", "Hi",
            "Highest group count to try. Every extra count is another full set of restarts, so keep "
            + "this near the range you would actually detail.",
            GH_ParamAccess.item, 10);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Groups", "K", "The group counts tried, in order.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("BIC", "B",
            "Bayesian information criterion, lower is better. Penalises extra groups by ln(n), so it "
            + "leans toward fewer — usually what you want when each group becomes a detail somebody "
            + "has to draw.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("AIC", "A",
            "Akaike information criterion, lower is better. A lighter penalty than BIC, so it tends "
            + "to suggest more groups.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Confidence", "C",
            "Mean confidence at each count. A model whose assignments all sit near 1/k has not found "
            + "structure, whatever its BIC says — worth reading alongside the criteria rather than "
            + "after them.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Lowest BIC At", "L",
            "The count with the lowest BIC. A starting point for the conversation, not the answer.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!TryRead(da, out double[,] data, out DesignGroupingOptions options))
            return;

        int minimum = 2;
        int maximum = 10;
        if (!da.GetData(1, ref minimum)) return;
        if (!da.GetData(2, ref maximum)) return;

        if (maximum < minimum)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Maximum ({maximum}) is below Minimum ({minimum}).");
            return;
        }

        try
        {
            var candidates = DesignGrouping.ChooseGroupCount(data, options, minimum, maximum);

            if (candidates.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Nothing to sweep over.");
                return;
            }

            int best = candidates.OrderBy(c => c.Bic).First().Groups;

            if (best == candidates[^1].Groups && candidates.Count > 1)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"BIC is still falling at {best}, the top of the range. Either raise Maximum or "
                    + "take the elbow rather than the minimum — with enough groups the criterion will "
                    + "keep rewarding more of them long after they stop meaning anything.");

            da.SetDataList(0, candidates.Select(c => c.Groups));
            da.SetDataList(1, candidates.Select(c => c.Bic));
            da.SetDataList(2, candidates.Select(c => c.Aic));
            da.SetDataList(3, candidates.Select(c => c.MeanConfidence));
            da.SetData(4, best);

            Message = $"{minimum}–{maximum}\nlowest at {best}";
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
