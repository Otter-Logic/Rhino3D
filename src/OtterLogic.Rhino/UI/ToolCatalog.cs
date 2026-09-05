using OtterLogic.Core;

namespace OtterLogic.Rhino.UI;

/// <summary>One entry in the panel: a command, and enough words to recognise it.</summary>
/// <param name="Section">Heading to file it under. Use a <see cref="Sections"/> constant.</param>
/// <param name="Name">Button label.</param>
/// <param name="Command">Rhino command name, without the leading underscore.</param>
/// <param name="Summary">
/// What it does, shown under the button. Keep it short — this renders in a
/// docked side panel a couple of hundred pixels wide.
/// </param>
/// <param name="Icon">
/// Base name of a PNG in <c>assets/icons</c>, without the extension. The same
/// name the toolbar and the Grasshopper component use, so one master file
/// serves every surface.
/// </param>
public sealed record OtterTool(string Section, string Name, string Command, string Summary, string Icon);

/// <summary>
/// Everything the Rhino panel lists.
/// <para>
/// Adding a command to the panel means adding one line here — no layout code to
/// touch. The panel groups by <see cref="OtterTool.Section"/> in the order the
/// sections first appear below, so keep related tools together.
/// </para>
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<OtterTool> Tools { get; } = new[]
    {
        new OtterTool(
            Sections.StructuralForm,
            "Truss 2D",
            "OtterTruss2D",
            "Bracing between two chords you drew. Adds web and end posts only.",
            "truss2d"),
    };
}
