using System.Runtime.InteropServices;
using Rhino.PlugIns;

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

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;
}
