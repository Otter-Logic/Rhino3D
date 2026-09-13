using System.Drawing;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace OtterLogic.Grasshopper.Components.Document;

/// <summary>One name shared by one or more items: what to call it on screen, and how many items share it.</summary>
internal readonly record struct BranchRow(string Name, int Count);

/// <summary>
/// Ticks items of any data tree by name, and gives just those items back.
/// <para>
/// Grows out of the same need <see cref="LayerPickerComponent"/> answers for
/// layers — an overview with a box to tick, rather than wiring up a Param
/// Viewer, a List Item and a Tree Branch just to look inside one group at a
/// time. Where that component reads the Rhino document, this one reads two
/// wired trees of the same shape: <c>Data</c>, and a matching <c>Names</c>
/// tree, one name per item.
/// </para>
/// <para>
/// Rows are one per <em>distinct name</em>, not one per branch. A component
/// that already gives every branch its own unique name — <c>Topology
/// Mapping</c>'s four-level Groups tree, the motivating case — ends up with
/// one row per branch anyway, since nothing else shares a name with it. But a
/// component like <c>Connectivity QA</c> that reports many items against a
/// flat, repeated set of reasons — a dozen free ends, all reading "free end —
/// a cantilever tip, or a connection that was missed" — collapses those into
/// one row, and ticking it selects every item that shares the wording,
/// wherever in the tree each one came from.
/// </para>
/// <para>
/// Names need not match Data item for item within a branch — only Names'
/// last item in a branch has to keep meaning what it says once Data runs
/// longer. <c>Topology Mapping</c>'s Releases is the case this matters for:
/// six release flags per branch, paired against a Group Names branch holding
/// one name per line in that group. Past Names' last item, its final name is
/// reused for the rest of Data's branch — the same name a moment longer,
/// which is exactly what "fewer lines than DOF flags" already means — so six
/// flags and a five-line group's worth of names still read as one named row.
/// </para>
/// </summary>
public sealed class BranchPickerComponent : GH_Component
{
    private const int DataInput = 0;
    private const int NamesInput = 1;

    /// <summary>Ticked names — the name text itself is the handle, since that is what a row now identifies.</summary>
    private readonly HashSet<string> _ticked = new(StringComparer.Ordinal);

    private IReadOnlyList<BranchRow> _rows = Array.Empty<BranchRow>();

    public BranchPickerComponent()
        : base("Branch Picker", "Branches",
               "Tick items of a data tree by name and get just those items back.\n\n"
               + "Wire the tree to pick from into Data, and a matching tree of names, the same shape, one "
               + "name per item, into Names. Every distinct name becomes one row here, ready to tick — items "
               + "that share a name, wherever they sit in the tree, collapse into the same row and are "
               + "selected together.\n\n"
               + "Works with any data tree: this does not need to know what is in it, only what to call each "
               + "item.",
               Categories.Root, Categories.Document)
    {
    }

    public override Guid ComponentGuid => new("8f3c1a6d-4e29-4b7f-a1d3-6c8e2f905b4a");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("branchpicker", 24);

    public override void CreateAttributes() => m_attributes = new BranchPickerAttributes(this);

    internal IReadOnlyList<BranchRow> Rows => _rows;

    internal bool IsTicked(BranchRow row) => _ticked.Contains(row.Name);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter("Data", "D", "The tree to pick items from.", GH_ParamAccess.tree);

        pManager.AddTextParameter("Names", "N",
            "A name for every item of Data, the same shape — one name per item. Items that share an "
            + "identical name, anywhere in the tree, are read as one group.",
            GH_ParamAccess.tree);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter("Data", "D",
            "The items whose name is ticked, one branch per ticked name in the order the rows are listed.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(DataInput, out GH_Structure<IGH_Goo> data))
            return;
        if (!da.GetDataTree(NamesInput, out GH_Structure<GH_String> names))
            return;

        // Every item paired with its own name, read by matching index within
        // the same branch — not just the branch's first item — so a flat list
        // of many items (Connectivity QA's Outliers) is read item by item
        // rather than as a single, single-named group.
        //
        // Names is not always the same length as Data within a branch: a
        // component reporting one fixed-width value per group — Topology
        // Mapping's Releases, six DOF flags per branch — pairs against a Names
        // branch that instead holds one name per line in that group. Past the
        // end of a shorter Names branch, its last name is reused rather than
        // treating the extra items as unnamed: Names repeats one string
        // through a branch by convention, so reusing it is what "the same
        // name, fewer times" already means, and it is what turns six release
        // flags and a five-line group's worth of names into one named row
        // instead of a name per item and one unnamed leftover.
        var entries = new List<(object? Item, string Name)>();
        int unnamed = 0;

        foreach (var path in data.Paths)
        {
            var branch = data.get_Branch(path);
            var nameBranch = names.PathExists(path) ? names.get_Branch(path) : null;

            for (int i = 0; i < branch.Count; i++)
            {
                string name;
                if (nameBranch is { Count: > 0 } && nameBranch[Math.Min(i, nameBranch.Count - 1)] is GH_String named)
                {
                    name = named.Value;
                }
                else
                {
                    name = $"{path}[{i}]";
                    unnamed++;
                }

                entries.Add((branch[i], name));
            }
        }

        if (unnamed > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{unnamed} item(s) had no matching name in Names and were labelled by their position instead.");

        var groups = entries.GroupBy(e => e.Name, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _rows = groups.Select(g => new BranchRow(g.Key, g.Count())).ToList();

        Attributes?.ExpireLayout();

        var output = new GH_Structure<IGH_Goo>();
        int branchIndex = 0;
        foreach (var group in groups)
        {
            if (!_ticked.Contains(group.Key))
                continue;

            output.AppendRange(group.Select(e => (IGH_Goo)e.Item!), new GH_Path(branchIndex));
            branchIndex++;
        }

        da.SetDataTree(0, output);

        int tickedCount = _rows.Count(row => _ticked.Contains(row.Name));
        if (tickedCount == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Nothing ticked yet — click the names you want.");

        Message = $"{tickedCount} of {_rows.Count}";
    }

    /// <summary>Ticks or clears one row.</summary>
    internal void Toggle(BranchRow row)
    {
        RecordUndoEvent("Tick name");

        if (!_ticked.Remove(row.Name))
            _ticked.Add(row.Name);

        ExpireSolution(true);
    }

    /// <summary>Ticks or clears every row.</summary>
    internal void TickAll(bool ticked)
    {
        RecordUndoEvent(ticked ? "Tick all names" : "Clear name ticks");

        _ticked.Clear();
        if (ticked)
            foreach (var row in _rows)
                _ticked.Add(row.Name);

        ExpireSolution(true);
    }

    // ----------------------------------------------------------------- saving

    public override bool Write(GH_IWriter writer)
    {
        writer.SetInt32("TickedCount", _ticked.Count);

        int i = 0;
        foreach (string name in _ticked)
            writer.SetString("Ticked", i++, name);

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
                string? name = null;
                if (reader.TryGetString("Ticked", i, ref name) && !string.IsNullOrEmpty(name))
                    _ticked.Add(name!);
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
