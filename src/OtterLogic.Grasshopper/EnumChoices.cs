using OtterLogic.Core;

namespace OtterLogic.Grasshopper;

/// <summary>
/// The choices an enum offers a user, as a label and the integer behind it.
/// <para>
/// One source for both of the ways Grasshopper puts an enum in front of
/// someone: the dropdown a value list drops on the canvas, and the named values
/// on the input that dropdown plugs into. Built separately, those two lists
/// drift into two spellings of the same option and nothing tells the user they
/// mean the same thing.
/// </para>
/// </summary>
internal static class EnumChoices
{
    /// <summary>
    /// Every value of <typeparamref name="TEnum"/>, in declaration order, named
    /// the way <see cref="Naming.Humanise(Enum)"/> names it.
    /// </summary>
    internal static IEnumerable<(string Label, int Value)> Of<TEnum>() where TEnum : struct, Enum
        => Enum.GetValues<TEnum>().Select(value => (Naming.Humanise(value), Convert.ToInt32(value)));
}
