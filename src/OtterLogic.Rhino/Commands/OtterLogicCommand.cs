using OtterLogic.Rhino.UI;
using Rhino;
using Rhino.Commands;
using Rhino.UI;

namespace OtterLogic.Rhino.Commands;

/// <summary>
/// Shows or hides the OtterLogic panel.
/// <para>
/// Toggling rather than always opening matches how Rhino behaves for
/// <c>Layer</c>, <c>Properties</c> and the rest, so the command does what a
/// Rhino user already expects it to.
/// </para>
/// </summary>
public sealed class OtterLogicCommand : Command
{
    public OtterLogicCommand() => Instance = this;

    public static OtterLogicCommand? Instance { get; private set; }

    public override string EnglishName => "OtterLogic";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        Guid panelId = OtterLogicPanel.PanelId;

        if (Panels.IsPanelVisible(panelId))
        {
            Panels.ClosePanel(panelId);
            return Result.Success;
        }

        Panels.OpenPanel(panelId);
        return Result.Success;
    }
}
