using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Methods;

/// <summary>
/// A Gaussian mixture as a method on a wire: how many clusters, and what shape
/// each may take. No data input; see <see cref="KMeansMethodComponent"/>.
/// </summary>
public sealed class GaussianMixtureMethodComponent : GH_Component
{
    public GaussianMixtureMethodComponent()
        : base("Gaussian Mixture", "GMM",
               "The Gaussian Mixture method for OtterCluster: fit one bell-shaped cloud per cluster and "
               + "give every sample a probability of belonging to each.\n\n"
               + "Soft assignment is what this has that K-Means does not: a sample can be mostly one "
               + "cluster and partly another, and Confidence shows which. Use it when clusters overlap "
               + "or are elongated rather than round. It still places every sample and still needs the "
               + "count — for outliers or an unknown count use HDBSCAN; for clusters shaped by what "
               + "connects to what, Spectral Clustering.\n\n"
               + "This component takes no samples: wire its Method output into OtterCluster.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("741bf358-31ea-4967-9c13-4322959e19eb");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords => new[] { "gmm", "mixture", "gaussian", "clustering", "soft" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("gaussianmixture", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Clusters", "K",
            "How many clusters — the number of Gaussians fitted.",
            GH_ParamAccess.item, 4);

        pManager.AddIntegerParameter("Covariance", "C",
            "The shape each cluster is allowed to take: spherical is a round ball, diagonal an "
            + "axis-aligned ellipsoid, full an ellipsoid at any orientation.\n\n"
            + "Right-click for the list, or wire a Covariance Type dropdown in. Full costs many more "
            + "parameters and wants a lot more samples to justify them.",
            GH_ParamAccess.item, (int)CovarianceType.Diagonal);

        var covariance = (Param_Integer)pManager[1];
        foreach (var (label, value) in EnumChoices.Of<CovarianceType>())
            covariance.AddNamedValue(label, value);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new ClusterMethodParameter(), "Method", "M", MethodWire.ClusterMethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        int clusters = 4;
        int covariance = (int)CovarianceType.Diagonal;
        if (!da.GetData(0, ref clusters)) return;
        if (!da.GetData(1, ref covariance)) return;

        if (!Enum.IsDefined(typeof(CovarianceType), covariance))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Covariance must be one of "
                + string.Join(", ", EnumChoices.Of<CovarianceType>().Select(c => $"{c.Value} ({c.Label})")) + ".");
            return;
        }

        var method = new GaussianMixtureMethod { Clusters = clusters, Covariance = (CovarianceType)covariance };
        da.SetData(0, new GH_ClusterMethod(method));
        Message = method.Describe();
    }
}
