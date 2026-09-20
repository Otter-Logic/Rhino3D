using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Parameters.UnsupervisedLearning;

/// <summary>
/// The three models Cluster Selector compares, as a dropdown for its Model input.
/// <para>
/// Everything specific to this enum is in this file: the name, the icon and the
/// GUID. The behaviour is <see cref="EnumValueList{TEnum}"/>'s.
/// </para>
/// </summary>
public sealed class ClusteringModelList : EnumValueList<ClusteringModel>
{
    public ClusteringModelList()
        : base("Clustering Model", "Model",
               "K-Means assumes round clusters and places every sample; Gaussian Mixture allows "
               + "elongated, overlapping clusters and says how firmly each sample belongs; HDBSCAN "
               + "finds dense regions of any shape and leaves outliers unplaced.\n\n"
               + "Plug it into the Model input of Cluster Selector to force one instead of choosing.",
               Categories.UnsupervisedLearning)
    {
    }

    // Dropdowns sit below every method in the panel: they are wired into a
    // component's input, so nobody goes looking for one first.
    public override GH_Exposure Exposure => GH_Exposure.quinary;

    public override Guid ComponentGuid => new("d84e348e-ac7e-409f-959d-68dc4e5498db");

    protected override Bitmap? Icon => EmbeddedIcons.Load("clusteringmodel", 24);
}
