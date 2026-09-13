using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.StructuralAnalysis;

namespace OtterLogic.Grasshopper.Parameters.StructuralAnalysis;

/// <summary>
/// The connection styles as a dropdown, for the Connections input of Load Path
/// Hierarchy.
/// <para>
/// Everything specific to this enum is in this file: the name, the icon and the
/// GUID. The behaviour is <see cref="EnumValueList{TEnum}"/>'s.
/// </para>
/// </summary>
public sealed class ConnectionStyleList : EnumValueList<ConnectionStyle>
{
    public ConnectionStyleList()
        : base("Connection Style", "Connections",
               "How the members are connected, which decides the releases Load Path Hierarchy suggests: "
               + "simple construction pins beams where they bear, a moment frame fixes primary beams to "
               + "columns, monolithic releases nothing.\n\n"
               + "Plug it into the Connections input of Load Path Hierarchy.",
               Categories.StructuralAnalysis)
    {
    }

    // Dropdowns sit below every tool in the panel: they are wired into a
    // component's input, so nobody goes looking for one first.
    public override GH_Exposure Exposure => GH_Exposure.quinary;

    public override Guid ComponentGuid => new("a46b0e26-5256-4bb0-b6ac-a70eb2698119");

    protected override Bitmap? Icon => EmbeddedIcons.Load("connectionstyle", 24);
}
