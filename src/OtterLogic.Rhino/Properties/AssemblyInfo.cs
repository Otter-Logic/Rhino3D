using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("OtterLogic")]
[assembly: AssemblyDescription("Form finding, fabrication and machine learning playground for Rhino 8.")]
[assembly: AssemblyCompany("OtterLogic")]
[assembly: AssemblyProduct("OtterLogic")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

[assembly: ComVisible(false)]

// Rhino takes the plug-in identity from the ASSEMBLY Guid, not from the [Guid]
// on the PlugIn class. Without this, PlugIn.Id is Guid.Empty: commands still
// register, but the plug-in never appears in the plug-in manager and anything
// keyed on its id - RegisterPanel above all - fails outright. Must stay equal to
// the attribute on OtterLogicPlugIn.
[assembly: Guid("a8c6eee6-f04f-4af6-9613-a7d425fd73b7")]

// Shown in Rhino's plug-in manager.
[assembly: Rhino.PlugIns.PlugInDescription(Rhino.PlugIns.DescriptionType.Organization, "OtterLogic")]
[assembly: Rhino.PlugIns.PlugInDescription(Rhino.PlugIns.DescriptionType.WebSite, "https://github.com/Otter-Logic/OtterLogic.Rhino")]
