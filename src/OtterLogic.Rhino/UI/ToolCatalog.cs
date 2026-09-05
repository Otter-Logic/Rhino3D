using OtterLogic.Core;

namespace OtterLogic.Rhino.UI;

/// <summary>One entry in the panel: a command, and enough words to recognise it.</summary>
/// <param name="Section">Heading to file it under. Use a <see cref="Sections"/> constant.</param>
/// <param name="Name">Button label.</param>
/// <param name="Command">Rhino command name, without the leading underscore.</param>
/// <param name="Summary">One line saying what it does, in the panel under the button.</param>
public sealed record OtterTool(string Section, string Name, string Command, string Summary);

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
            "Bracing between two chords you have already drawn. Adds web and end posts only."),

        new OtterTool(
            Sections.FormFinding,
            "Relax Mesh",
            "OtterRelax",
            "Relax a mesh toward equilibrium, drawn live. Esc stops it and keeps what it reached."),
    };
}
