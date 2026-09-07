using System.Drawing;
using OtterLogic.MachineLearning.Clustering;

namespace OtterLogic.Grasshopper.Parameters.MachineLearning;

/// <summary>
/// The covariance shapes as a dropdown, for the Covariance input of the
/// clustering components.
/// <para>
/// Everything specific to this enum is in this file: the name, the icon and the
/// GUID. The behaviour is <see cref="EnumValueList{TEnum}"/>'s.
/// </para>
/// </summary>
public sealed class CovarianceTypeList : EnumValueList<CovarianceType>
{
    public CovarianceTypeList()
        : base("Covariance Type", "Covariance",
               "The shape each group's spread may take. Plug it into the Covariance input of "
               + "Cluster Design Groups or Choose Group Count.",
               Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("c0162144-ed66-4e93-b2d9-1161162b40ef");

    protected override Bitmap? Icon => EmbeddedIcons.Load("covariancetype", 24);
}
