using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Supervised.Neighbours;

namespace OtterLogic.Grasshopper.Parameters.SupervisedLearning;

/// <summary>
/// The neighbour weightings as a dropdown, for the Weighting input of the two
/// nearest-neighbour components.
/// <para>
/// Everything specific to this enum is in this file: the name, the icon and the
/// GUID. The behaviour is <see cref="EnumValueList{TEnum}"/>'s.
/// </para>
/// </summary>
public sealed class NeighbourWeightingList : EnumValueList<NeighbourWeighting>
{
    public NeighbourWeightingList()
        : base("Neighbour Weighting", "Weighting",
               "How a sample's neighbours share the say in its prediction: uniform gives each an equal "
               + "vote, distance weights each by one over how far away it is.\n\n"
               + "Plug it into the Weighting input of Nearest Neighbour Classifier or Nearest Neighbour "
               + "Regressor.",
               Categories.SupervisedLearning)
    {
    }

    // Dropdowns sit below every method in the panel: they are wired into a
    // component's input, so nobody goes looking for one first.
    public override GH_Exposure Exposure => GH_Exposure.quinary;

    public override Guid ComponentGuid => new("b441851d-84e8-4a5c-a5b3-4a88d7c1d3d7");

    protected override Bitmap? Icon => EmbeddedIcons.Load("neighbourweighting", 24);
}
