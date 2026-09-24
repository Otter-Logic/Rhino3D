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
    public override Bitmap? Icon => EmbeddedIcons.Load("otterlogic", 24);
    public override Guid Id => new("d974722a-bd69-487c-815e-5b776d3705ad");
    public override string AuthorName => "Marvin Suen";
    public override string AuthorContact => "https://github.com/Otter-Logic/Rhino3D";
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

        // The ribbon tab icon. Grasshopper falls back to the symbol letter above
        // if this is null, so a missing icon degrades rather than breaks.
        Bitmap? tabIcon = EmbeddedIcons.Load("otterlogic", 24);
        if (tabIcon is not null)
            Instances.ComponentServer.AddCategoryIcon(Categories.Root, tabIcon);

        return GH_LoadingInstruction.Proceed;
    }
}

/// <summary>
/// Every component Category/Subcategory in one place. The Category string is
/// literally what names the ribbon tab, so a typo silently creates a second one.
/// <para>
/// The values come from <see cref="OtterLogic.Core.Sections"/>, which the Rhino
/// toolbar also reads, so the two cannot drift apart. The panels are the section
/// names with a hidden prefix that puts them in <see cref="Sections.Order"/>:
/// Grasshopper sorts a tab's panels by name and offers a plug-in no other say in
/// the matter, but its sort (GH_Layout.StringSort, for RH-83156) puts a name
/// with more leading whitespace first and counts the zero-width space U+200B as
/// whitespace. That character draws at no width, so a run of them orders the
/// panels and the labels still centre like Grasshopper's own. Ordinary spaces
/// sort the same way but are drawn, and pushed every label off centre. Graphs
/// gets the longest run and sits leftmost. Category and SubCategory are
/// display-only — a saved definition keys on ComponentGuid — so the prefix
/// breaks nothing and can change freely.
/// </para>
/// </summary>
public static class Categories
{
    public const string Root = OtterLogic.Core.Sections.Root;

    public static readonly string Document = Ranked(OtterLogic.Core.Sections.Document);
    public static readonly string StructuralForm = Ranked(OtterLogic.Core.Sections.StructuralForm);
    public static readonly string StructuralDesign = Ranked(OtterLogic.Core.Sections.StructuralDesign);
    public static readonly string FormFinding = Ranked(OtterLogic.Core.Sections.FormFinding);
    public static readonly string Fabrication = Ranked(OtterLogic.Core.Sections.Fabrication);
    public static readonly string Construction = Ranked(OtterLogic.Core.Sections.Construction);
    public static readonly string Graphs = Ranked(OtterLogic.Core.Sections.Graphs);
    public static readonly string Dataset = Ranked(OtterLogic.Core.Sections.Dataset);
    public static readonly string MachineLearning = Ranked(OtterLogic.Core.Sections.MachineLearning);

    /// <summary>
    /// The section name behind a run of zero-width spaces long enough to sort it
    /// into its place: one more than every section after it, so the last needs
    /// none. A section missing from the order sorts after every ranked one, by
    /// its own name, rather than throwing — a forgotten entry should cost a panel
    /// its place, not the plugin its load.
    /// </summary>
    private static string Ranked(string section)
    {
        int index = Array.IndexOf(OtterLogic.Core.Sections.Order, section);
        if (index < 0)
            return section;

        return new string('\u200B', OtterLogic.Core.Sections.Order.Length - index) + section;
    }
}
