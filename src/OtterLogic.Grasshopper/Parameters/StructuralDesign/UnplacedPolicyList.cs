using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Parameters.StructuralDesign;

/// <summary>
/// What to do with elements no group would take, as a dropdown for the Unassigned
/// input of the 6DOF Behaviour Classifier.
/// <para>
/// In Structural Design rather than Machine Learning, although the enum lives
/// in Unsupervised: the person wiring the classifier should find everything it
/// takes in the panel they are already in.
/// </para>
/// </summary>
public sealed class UnplacedPolicyList : EnumValueList<UnplacedPolicy>
{
    public UnplacedPolicyList()
        : base("Unassigned", "Unassigned",
               "What happens to an element that fits no group. Leave keeps it at -1, the honest reading. "
               + "Own group gives each one a group of its own after the others — the cautious choice when every "
               + "element must be designed and an unusual one should not share its neighbours' design. Nearest "
               + "files it with the closest group.\n\n"
               + "Plug it into the Unassigned input of 6DOF Behaviour Classifier.",
               Categories.StructuralDesign)
    {
    }

    // Below every tool, in the dropdown tier: it is wired into the 6DOF Behaviour
    // Classifier, so nobody goes looking for it first.
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    public override Guid ComponentGuid => new("3b8d5e0f-6a2c-4f71-9d3e-b5c8a1e47f26");

    protected override Bitmap? Icon => EmbeddedIcons.Load("unplacedpolicy", 24);
}
