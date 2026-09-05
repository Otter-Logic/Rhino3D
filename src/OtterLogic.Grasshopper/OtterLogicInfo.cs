using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

// Same namespace gotcha as the Rhino project: inside OtterLogic.Grasshopper a
// bare "Grasshopper.X" binds here, not to McNeel's assembly. Import above the
// namespace and reference types unqualified.
namespace OtterLogic.Grasshopper;

/// <summary>
/// Identifies the add-on to Grasshopper. Note this does *not* create the tab —
/// the ribbon tab comes from each component's Category string.
/// </summary>
public sealed class OtterLogicInfo : GH_AssemblyInfo
{
    public override string Name => "OtterLogic";
    public override string Description => "Form finding, fabrication and machine learning playground.";
    public override Bitmap? Icon => null;
    public override Guid Id => new("d974722a-bd69-487c-815e-5b776d3705ad");
    public override string AuthorName => "OtterLogic";
    public override string AuthorContact => "";
    public override string AssemblyVersion => GetType().Assembly.GetName().Version!.ToString();
}

/// <summary>
/// Runs once when Grasshopper loads. This is where the tab gets its short name,
/// symbol and (later) icon.
/// </summary>
public sealed class OtterLogicPriority : GH_AssemblyPriority
{
    public override GH_LoadingInstruction PriorityLoad()
    {
        Instances.ComponentServer.AddCategoryShortName(Categories.Root, "Otter");
        Instances.ComponentServer.AddCategorySymbolName(Categories.Root, 'O');

        // When you have one, drop a 24x24 into Resources and add:
        // Instances.ComponentServer.AddCategoryIcon(Categories.Root, Resources.TabIcon);

        return GH_LoadingInstruction.Proceed;
    }
}

/// <summary>
/// Every component Category/Subcategory in one place. The Category string is
/// literally what names the ribbon tab, so a typo silently creates a second one.
/// <para>
/// The values come from <see cref="OtterLogic.Core.Sections"/>, which the Rhino
/// panel also reads, so the tab and the panel cannot drift apart.
/// </para>
/// </summary>
public static class Categories
{
    public const string Root = OtterLogic.Core.Sections.Root;

    public const string StructuralForm = OtterLogic.Core.Sections.StructuralForm;
    public const string FormFinding = OtterLogic.Core.Sections.FormFinding;
    public const string Fabrication = OtterLogic.Core.Sections.Fabrication;
    public const string Learning = OtterLogic.Core.Sections.Learning;
}
