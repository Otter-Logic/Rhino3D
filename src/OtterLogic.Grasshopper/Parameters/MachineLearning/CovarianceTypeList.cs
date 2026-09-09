using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.MachineLearning.Clustering;

namespace OtterLogic.Grasshopper.Parameters.MachineLearning;

/// <summary>
/// The covariance shapes as a dropdown, for the Covariance input of the
/// Gaussian Mixture component.
/// <para>
/// Everything specific to this enum is in this file: the name, the icon and the
/// GUID. The behaviour is <see cref="EnumValueList{TEnum}"/>'s.
/// </para>
/// </summary>
public sealed class CovarianceTypeList : EnumValueList<CovarianceType>
{
    public CovarianceTypeList()
        : base("Covariance Type", "Covariance",
               "The shape each group is allowed to take: spherical is a round ball, diagonal an "
               + "axis-aligned ellipsoid, full an ellipsoid at any orientation.\n\n"
               + "Plug it into the Covariance input of Gaussian Mixture.",
               Categories.MachineLearning)
    {
    }

    // Dropdowns sit below every method in the panel: they are wired into a
    // component's input, so nobody goes looking for one first.
    public override GH_Exposure Exposure => GH_Exposure.quinary;

    public override Guid ComponentGuid => new("c0162144-ed66-4e93-b2d9-1161162b40ef");

    protected override Bitmap? Icon => EmbeddedIcons.Load("covariancetype", 24);
}
