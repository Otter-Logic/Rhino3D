using System.Drawing;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace OtterLogic.Grasshopper.Components.Document;

/// <summary>One branch of the wired tree: its path, and how many items it holds.</summary>
internal readonly record struct BranchRow(GH_Path Path, int Count)
{
    /// <summary>The path as text — what the row is labelled with, and what a tick is stored under.</summary>
    public string Key => Path.ToString();
}

/// <summary>
/// Ticks branches of any data tree, and gives just those branches back.
/// <para>
/// Reads like Grasshopper's own <c>Explode Tree</c> — the branches of whatever
/// is wired in, listed with their item counts — except that the branches are
/// ticked rather than taken one per output. That keeps one component and one
/// output however many branches arrive, where Explode Tree has to be zoomed
/// and re-wired every time the branch count changes, and it replaces the
/// Param Viewer / Tree Branch pair a user otherwise assembles just to look
/// inside one group at a time.
/// </para>
/// <para>
/// Rows are one per branch, labelled by path, because the path is the tree's
/// own handle on a branch: it survives a re-solve, it is what the tooltip on
/// the upstream wire already shows, and it needs nothing wired in beside the
/// data to be meaningful. An earlier version paired the data against a second
/// tree of names and grouped by name instead; that bought cross-branch
/// grouping at the cost of a second input that had to be built and kept the
/// same shape, which is the complication this component exists to avoid.
/// </para>
/// <para>
/// Ticked branches come out at <em>their own paths</em>, not renumbered. A
/// picked branch therefore still lines up with any tree still carrying the
/// full set — group names, a colour per group — so downstream components
/// match them up without the user re-deriving which branch a selection came
/// from.
/// </para>
/// </summary>
public sealed class BranchPickerComponent : GH_Component
{
    private const int DataInput = 0;

    /// <summary>
    /// Ticked paths as text. Paths absent from the current tree are kept
    /// rather than pruned: an upstream change that drops a branch and brings
    /// it back — a filter being adjusted — should not quietly lose the tick.
    /// </summary>
    private readonly HashSet<string> _ticked = new(StringComparer.Ordinal);

    private IReadOnlyList<BranchRow> _rows = Array.Empty<BranchRow>();

    public BranchPickerComponent()
        : base("Branch Picker", "Branches",
               "Tick branches of a data tree and get just those branches back.\n\n"
               + "Wire in any tree: every branch becomes one row here, labelled by its path and showing how "
               + "many items it holds, ready to tick. Ticked branches come out at their own paths, so they "
               + "still line up with any tree carrying the full set.\n\n"
               + "Use this instead of Explode Tree when the number of branches changes, or when the point is "
               + "to look through the groups one at a time rather than wire all of them up at once.",
               Categories.Root, Categories.Document)
    {
    }

    public override Guid ComponentGuid => new("8f3c1a6d-4e29-4b7f-a1d3-6c8e2f905b4a");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("branchpicker", 24);

    public override void CreateAttributes() => m_attributes = new BranchPickerAttributes(this);

    internal IReadOnlyList<BranchRow> Rows => _rows;

    internal bool IsTicked(BranchRow row) => _ticked.Contains(row.Key);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter("Data", "D", "The tree to pick branches from.", GH_ParamAccess.tree);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter("Data", "D",
            "The items of every ticked branch, each kept at the path it came in on.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(DataInput, out GH_Structure<IGH_Goo> data))
            return;

        _rows = data.Paths
            .Select(path => new BranchRow(path, data.get_Branch(path).Count))
            .ToList();

        // The row list is the component's size, so the canvas has to be told
        // the moment the tree changes shape rather than at the next repaint.
        Attributes?.ExpireLayout();

        var output = new GH_Structure<IGH_Goo>();

        foreach (BranchRow row in _rows)
        {
            if (!_ticked.Contains(row.Key))
                continue;

            output.AppendRange(data.get_Branch(row.Path).Cast<IGH_Goo>(), row.Path);
        }

        da.SetDataTree(0, output);

        int tickedCount = _rows.Count(row => _ticked.Contains(row.Key));
        if (tickedCount == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Nothing ticked yet — click the branches you want.");

        Message = $"{tickedCount} of {_rows.Count}";
    }

    /// <summary>Ticks or clears one row.</summary>
    internal void Toggle(BranchRow row)
    {
        RecordUndoEvent("Tick branch");

        if (!_ticked.Remove(row.Key))
            _ticked.Add(row.Key);

        ExpireSolution(true);
    }

    /// <summary>Ticks or clears every row.</summary>
    internal void TickAll(bool ticked)
    {
        RecordUndoEvent(ticked ? "Tick all branches" : "Clear branch ticks");

        _ticked.Clear();
        if (ticked)
            foreach (BranchRow row in _rows)
                _ticked.Add(row.Key);

        ExpireSolution(true);
    }

    // ----------------------------------------------------------------- saving

    public override bool Write(GH_IWriter writer)
    {
        writer.SetInt32("TickedCount", _ticked.Count);

        int i = 0;
        foreach (string path in _ticked)
            writer.SetString("Ticked", i++, path);

        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        _ticked.Clear();

        int count = 0;
        if (reader.TryGetInt32("TickedCount", ref count))
        {
            for (int i = 0; i < count; i++)
            {
                string? path = null;
                if (reader.TryGetString("Ticked", i, ref path) && !string.IsNullOrEmpty(path))
                    _ticked.Add(path!);
            }
        }

        return base.Read(reader);
    }

    // ------------------------------------------------------------------- menu

    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);

        Menu_AppendItem(menu, "Tick all", (_, _) => TickAll(true), _rows.Count > 0);
        Menu_AppendItem(menu, "Tick none", (_, _) => TickAll(false), _ticked.Count > 0);
    }
}
