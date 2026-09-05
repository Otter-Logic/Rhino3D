using System.Runtime.InteropServices;
using OtterLogic.Rhino.UI;
using Rhino;
using Rhino.PlugIns;
using Rhino.UI;

// Gotcha worth knowing early: inside namespace OtterLogic.Rhino, a bare
// "Rhino.Something" binds to *this* namespace, not McNeel's. Always import the
// types with a using directive above the namespace and reference them
// unqualified, or write global::Rhino.Something.
namespace OtterLogic.Rhino;

/// <summary>
/// Rhino entry point. One instance per session, constructed by Rhino itself.
/// <para>
/// Plumbing only — no algorithms. Anything worth testing lives in
/// OtterLogic.Core so the Grasshopper side reuses it verbatim.
/// </para>
/// </summary>
[Guid("a8c6eee6-f04f-4af6-9613-a7d425fd73b7")]
public sealed class OtterLogicPlugIn : PlugIn
{
    public OtterLogicPlugIn() => Instance = this;

    /// <summary>The one and only instance, for commands and panels to reach.</summary>
    public static OtterLogicPlugIn? Instance { get; private set; }

    // AtStartup, so the panel is registered before the user types anything.
    // A panel registered lazily is a panel Rhino has already decided does not
    // exist by the time it restores the last session layout.
    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    /// <summary>
    /// Set when panel registration failed, so the reason is visible instead of
    /// being swallowed.
    /// </summary>
    public static string? PanelRegistrationError { get; private set; }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        try
        {
            Panels.RegisterPanel(this, typeof(OtterLogicPanel), Core.Sections.Root, PanelIcon.Create());
        }
        catch (Exception ex)
        {
            // A panel that will not register is a nuisance. A plug-in that will
            // not load because of it takes every command down with it, which is
            // a great deal worse. Report and carry on.
            PanelRegistrationError = ex.ToString();
            RhinoApp.WriteLine($"OtterLogic: the panel could not be registered. {ex.Message}");
        }

        return LoadReturnCode.Success;
    }
}
