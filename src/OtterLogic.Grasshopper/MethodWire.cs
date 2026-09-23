using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Types;
using OtterLogic.MachineLearning.Training;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper;

/// <summary>
/// The two "method on a wire" outputs, read and described in one place so that
/// nine small components cannot describe the same wire nine ways.
/// <para>
/// Registering stays in each component — Grasshopper's parameter managers are
/// protected types nested in <c>GH_Component</c> — but the wording and the
/// reading are shared, as <see cref="GraphWire"/> does for the Graph wire.
/// </para>
/// </summary>
internal static class MethodWire
{
    /// <summary>What every cluster method component says about its one output.</summary>
    public const string ClusterMethodOutput =
        "This method with these settings. Wire it into OtterCluster's Method input; the samples are "
        + "wired there, not here.";

    /// <summary>What every learner component says about its one output.</summary>
    public const string LearnerOutput =
        "This learner with these settings. Wire it into OtterTrain's Learner input; the samples are "
        + "wired there, not here.";

    /// <summary>The method at <paramref name="index"/>, or null when nothing is wired — which is a valid choice, not an error.</summary>
    public static ClusteringMethod? ReadClusterMethod(IGH_DataAccess da, int index)
    {
        GH_ClusterMethod? goo = null;
        return da.GetData(index, ref goo) ? goo?.Value : null;
    }

    /// <summary>The learner at <paramref name="index"/>, or null when nothing is wired.</summary>
    public static Learner? ReadLearner(IGH_DataAccess da, int index)
    {
        GH_Learner? goo = null;
        return da.GetData(index, ref goo) ? goo?.Value : null;
    }
}
