using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Parameters.UnsupervisedLearning;

/// <summary>
/// The linkages as a dropdown, for the Linkage input of Hierarchical Clustering.
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
               Categories.UnsupervisedLearning)
    {
    }

    // Dropdowns sit below every method in the panel: they are wired into a
    // component's input, so nobody goes looking for one first.
    public override GH_Exposure Exposure => GH_Exposure.quinary;

    public override Guid ComponentGuid => new("50cf56cd-f146-4b0a-ac7c-a49b248e1519");

    protected override Bitmap? Icon => EmbeddedIcons.Load("linkage", 24);
}
