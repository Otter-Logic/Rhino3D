using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Parameters.StructuralDesign;

/// <summary>
/// The parameter the Settings input and output are made of.
/// <para>
/// Hidden from the ribbon: settings are always made by Insight Settings, so there
/// is nothing to park on the canvas and no "set one" menu to offer. Not persistent
/// for the same reason.
/// </para>
/// </summary>
public sealed class InsightSettingsParameter : GH_Param<GH_InsightSettings>
{
    public InsightSettingsParameter()
        : base("Insight Settings", "Settings",
               "The Structural Insight Engine's tuning. Made by Insight Settings; read by the engine's Settings input.",
               Categories.Root, Categories.StructuralDesign, GH_ParamAccess.item)
    {
    }

    public override Guid ComponentGuid => new("7c2e9f41-3b8d-4a65-9e17-d05f6b8a2c93");

    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override Bitmap? Icon => EmbeddedIcons.Load("insightsettings", 24);
}
