using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// A Gaussian mixture in its raw form, fitted by expectation-maximisation.
/// <para>
/// Adapter only. The algorithm belongs to <see cref="GaussianMixture"/>.
/// </para>
/// </summary>
public sealed class GaussianMixtureComponent : GH_Component
{
    public GaussianMixtureComponent()
        : base("Gaussian Mixture", "GMM",
               "Fit a mixture of Gaussians and report, for every sample, the probability that it "
               + "belongs to each one.\n\n"
               + "Soft assignment is what this has that k-means does not: a sample can be mostly one "
               + "group and partly another, and you can see which. Use it when groups overlap or are "
               + "elongated rather than round.",
               Categories.Root, Categories.UnsupervisedLearning)
    {
    }

    public override Guid ComponentGuid => new("b41c0d67-5a92-4e83-8f16-2d7a90c3e5b4");

    // The methods tier of the Unsupervised Learning panel. Grasshopper draws a
    // divider between exposures, which is what groups this panel by pipeline
    // stage without needing a subcategory each.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("gaussianmixture", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Training Inputs", "T", TrainingData.Description,
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Components", "K",
            "How many Gaussians to fit — the number of groups.\n\n"
            + "Unlike k-means this has a likelihood, so BIC on the output can compare counts: fit a "
            + "range and take the lowest.",
            GH_ParamAccess.item, 4);

        pManager.AddIntegerParameter("Covariance", "C",
            "The shape each group is allowed to take: spherical is a round ball, diagonal an "
            + "axis-aligned ellipsoid, full an ellipsoid at any orientation.\n\n"
            + "Right-click for the list, or wire a Covariance Type dropdown in. Full costs many more "
            + "parameters and wants a lot more samples to justify them.",
            GH_ParamAccess.item, (int)CovarianceType.Diagonal);

        pManager.AddIntegerParameter("Restarts", "R",
            "How many times to refit from a different start, keeping the best likelihood.\n\n"
            + "EM climbs to a local optimum and stops, so this is the cheapest accuracy available.",
            GH_ParamAccess.item, 10);

        pManager.AddIntegerParameter("Random Seed", "S",
            "Seeds the initialisation. Leave it fixed so a re-solve returns the same groups.",
            GH_ParamAccess.item, 1);

        var covariance = (Param_Integer)pManager[2];
        foreach (var (label, value) in EnumChoices.Of<CovarianceType>())
            covariance.AddNamedValue(label, value);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Result", "R",
            "Group index per sample — the most probable group. Groups are numbered largest first, so "
            + "the numbering does not shuffle when the input changes slightly.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Groups", "G",
            "Sample indices bucketed by group, one branch each.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Probability", "P",
            "One branch per sample, holding its probability of belonging to each group. Each branch "
            + "sums to one.\n\n"
            + "This is the output worth having. A sample split 0.55 / 0.45 is a real finding, and a "
            + "hard label would hide it.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Confidence", "C",
            "The largest probability for each sample. Anything much below 0.75 is sitting between "
            + "two groups.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Centres", "M",
            "One branch per group, holding that group's mean in the units the data arrived in.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Likelihood", "L",
            "Log-likelihood of the data under the fitted model. Higher is a better fit, but it "
            + "always improves with more components, so use BIC to compare counts.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("BIC", "B",
            "Bayesian information criterion, lower is better.\n\n"
            + "Penalises extra components by ln(n), so it is the honest way to choose how many to "
            + "fit. Only comparable between fits over the same data.",
            GH_ParamAccess.item);
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

        int components = 4;
        int covariance = (int)CovarianceType.Diagonal;
        int restarts = 10;
        int seed = 1;
        if (!da.GetData(1, ref components)) return;
        if (!da.GetData(2, ref covariance)) return;
        if (!da.GetData(3, ref restarts)) return;
        if (!da.GetData(4, ref seed)) return;

        if (!Enum.IsDefined(typeof(CovarianceType), covariance))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Covariance must be one of "
                + string.Join(", ", EnumChoices.Of<CovarianceType>().Select(c => $"{c.Value} ({c.Label})"))
                + ".");
            return;
        }

        try
        {
            var result = GaussianMixture.Fit(data, new GaussianMixtureOptions
            {
                Components = components,
                Covariance = (CovarianceType)covariance,
                Restarts = restarts,
                Seed = seed,
            });

            if (!result.Converged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The fit hit its iteration cap after {result.Iterations} rounds without "
                    + "settling. Fewer components, or a different seed, usually settles it.");

            // Largest first, so a small upstream change does not permute the
            // groups and shuffle every colour downstream.
            var ordered = result.OrderedByWeight();

            int weak = result.Confidence().Count(c => c < 0.75);
            if (weak > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{weak} sample(s) sit below 0.75 confidence, between two groups. Check Confidence.");

            da.SetDataList(0, ordered.Labels());
            da.SetDataTree(1, Trees.FromBuckets(ordered.Clusters()));
            da.SetDataTree(2, Trees.FromRows(ordered.Responsibilities));
            da.SetDataList(3, ordered.Confidence());
            da.SetDataTree(4, Trees.FromRows(ordered.Means));
            da.SetData(5, result.LogLikelihood);
            da.SetData(6, result.Bic);

            Message = $"{components} components\n{(CovarianceType)covariance}";
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
