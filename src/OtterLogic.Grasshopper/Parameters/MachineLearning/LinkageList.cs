using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Parameters.MachineLearning;

/// <summary>
/// The linkages as a dropdown, for the Linkage input of the Hierarchical Clustering
/// method component.
/// <para>
/// Everything specific to this enum is in this file: the name, the icon and the
/// GUID. The behaviour is <see cref="EnumValueList{TEnum}"/>'s.
/// </para>
/// </summary>
public sealed class LinkageList : EnumValueList<Linkage>
{
    public LinkageList()
        : base("Linkage", "Linkage",
               "How the distance between two clusters is measured: Ward merges whatever adds least "
               + "spread, complete keeps every cluster's diameter small, average is the compromise, "
               + "and single follows chains of close samples however long.\n\n"
               + "Plug it into the Linkage input of Hierarchical Clustering.",
               Categories.MachineLearning)
    {
    }

    // Dropdowns sit at the bottom of the panel: they are wired into a method
    // component's input, so nobody goes looking for one first.
    public override GH_Exposure Exposure => GH_Exposure.quinary;

    public override Guid ComponentGuid => new("50cf56cd-f146-4b0a-ac7c-a49b248e1519");

    protected override Bitmap? Icon => EmbeddedIcons.Load("linkage", 24);
}
