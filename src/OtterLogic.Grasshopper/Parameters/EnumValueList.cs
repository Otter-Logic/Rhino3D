using System.Globalization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace OtterLogic.Grasshopper.Parameters;

/// <summary>
/// A dropdown of an enum's values, sitting in the ribbon under its own icon and
/// wiring straight into the integer input that expects them.
/// <para>
/// A component's input carries the same names on its right-click menu, which is
/// fine once you know to look. This is for when you do not: the option list is a
/// thing on the canvas, visible in the definition, and readable by whoever opens
/// it next without clicking anything.
/// </para>
/// <para>
/// Generic on purpose. Every domain grows option enums, and each one wants
/// exactly this; a subclass is a constructor call, a GUID and an icon. The
/// labels come from <see cref="EnumChoices"/>, so a dropdown and the input it
/// feeds cannot end up spelling an option two different ways.
/// </para>
/// </summary>
/// <typeparam name="TEnum">The enum to offer. Its integer values are what travel down the wire.</typeparam>
public abstract class EnumValueList<TEnum> : GH_ValueList where TEnum : struct, Enum
{
    protected EnumValueList(string name, string nickName, string description, string subCategory)
    {
        Name = name;
        NickName = nickName;
        Description = description;
        Category = Categories.Root;
        SubCategory = subCategory;

        ListMode = GH_ValueListMode.DropDown;

        // The base constructor seeds three placeholder items of its own.
        ListItems.Clear();

        foreach (var (label, value) in EnumChoices.Of<TEnum>())
            ListItems.Add(new GH_ValueListItem(label, value.ToString(CultureInfo.InvariantCulture)));

        // Something has to be selected, or dropping one on the canvas produces a
        // dead wire and an error on the component it feeds.
        if (ListItems.Count > 0)
            ListItems[0].Selected = true;
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;
}
