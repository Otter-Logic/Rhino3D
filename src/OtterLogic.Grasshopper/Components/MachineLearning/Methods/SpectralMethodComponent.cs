using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Methods;

/// <summary>
/// Spectral clustering as a method on a wire: how many clusters, and how many
/// neighbours each sample is linked to. The graph is built from the samples
/// inside the method, so nobody wires one. No data input; see
/// <see cref="KMeansMethodComponent"/>.
/// </summary>
public sealed class SpectralMethodComponent : GH_Component
{
    public SpectralMethodComponent()
        : base("Spectral Clustering", "Spectral",
               "The Spectral method for OtterCluster: cluster by connection rather than by closeness — "
               + "samples joined by a path of near neighbours end up together, whatever shape they make.\n\n"
               + "Each sample is linked to its nearest few, and the resulting graph is cut where the links "
               + "are weakest. Use it for long, curved or chained clusters that K-Means and Gaussian "
               + "Mixture slice across. It needs the count and places every sample; for outliers or an "
               + "unknown count use HDBSCAN. Too few neighbours and a real cluster breaks into pieces; "
               + "too many and links start bridging clusters that are merely close.\n\n"
               + "This component takes no samples: wire its Method output into OtterCluster.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("76963e21-9048-402c-aa38-da8e36dbdc57");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords => new[] { "spectral", "graph", "clustering", "manifold", "connected" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("spectralclustering", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Clusters", "K",
            "How many clusters to cut the graph into. At least two.",
            GH_ParamAccess.item, 4);

        pManager.AddIntegerParameter("Neighbours", "N",
            "How many of its nearest samples each sample is linked to. Ten by default; fewer follows "
            + "thinner shapes, more smooths them.",
            GH_ParamAccess.item, 10);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new ClusterMethodParameter(), "Method", "M", MethodWire.ClusterMethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        int clusters = 4;
        int neighbours = 10;
        if (!da.GetData(0, ref clusters)) return;
        if (!da.GetData(1, ref neighbours)) return;

        var method = new SpectralMethod { Clusters = clusters, Neighbours = neighbours };
        da.SetData(0, new GH_ClusterMethod(method));
        Message = method.Describe();
    }
}
