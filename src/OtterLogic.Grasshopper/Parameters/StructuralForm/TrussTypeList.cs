using System.Drawing;
using OtterLogic.StructuralForm;

namespace OtterLogic.Grasshopper.Parameters.StructuralForm;

/// <summary>
/// The truss bracing patterns as a dropdown, for the Type input of Flat Truss.
/// <para>
/// Everything specific to trusses is in this file: the enum it reads, the name,
/// the icon and the GUID. The behaviour is
/// <see cref="EnumValueList{TEnum}"/>'s, so the next enum that wants a dropdown
/// is a file this size and no more.
/// </para>
/// </summary>
public sealed class TrussTypeList : EnumValueList<TrussType>
{
    public TrussTypeList()
        : base("Truss Type", "TrussType",
               "Web bracing patterns for a flat truss. Plug it into the Type input of Flat Truss.",
               Categories.StructuralForm)
    {
    }

    public override Guid ComponentGuid => new("5e221c57-87cc-4f4c-9313-93ce93fc6710");

    protected override Bitmap? Icon => EmbeddedIcons.Load("trusstype", 24);
}
