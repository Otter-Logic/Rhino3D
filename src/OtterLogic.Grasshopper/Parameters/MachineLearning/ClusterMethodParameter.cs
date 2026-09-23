using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Parameters.MachineLearning;

/// <summary>
/// The parameter every Method input and output is made of.
/// <para>
/// Hidden from the ribbon: a method is always made by a method component, so
/// there is nothing to park on the canvas and no "set one" menu to offer. Not
/// persistent for the same reason.
/// </para>
/// </summary>
public sealed class ClusterMethodParameter : GH_Param<GH_ClusterMethod>
{
    public ClusterMethodParameter()
        : base("Cluster Method", "Method",
               "A clustering algorithm with its settings chosen. Made by K-Means, Gaussian Mixture, "
               + "HDBSCAN, Spectral Clustering or Hierarchical Clustering; read by OtterCluster.",
               Categories.Root, Categories.MachineLearning, GH_ParamAccess.item)
    {
    }

    public override Guid ComponentGuid => new("2fe5ca97-5ea1-4be2-885e-30b058352de6");

    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override Bitmap? Icon => EmbeddedIcons.Load("clustermethod", 24);
}
