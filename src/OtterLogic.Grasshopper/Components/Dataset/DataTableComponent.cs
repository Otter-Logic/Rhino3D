using System.Drawing;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Dataset.Data;
using OtterLogic.Dataset.Tables;
using OtterLogic.Dataset.Text;

namespace OtterLogic.Grasshopper.Components.Dataset;

/// <summary>
/// A table behind a component: typed or pasted in and edited in a spreadsheet-like
/// window opened by a double-click, or, with a tree wired in, a view of that tree
/// as rows and columns in the same window.
/// <para>
/// A component rather than a parameter like the native Panel, because it has two
/// outputs. The rule that decides its mode is the one the Panel uses: a wire in
/// makes it a viewer, and the table typed into it is kept until the wire goes —
/// unplugging brings it back rather than leaving an empty component where the work
/// was. Nothing but the size is drawn on the canvas; see
/// <see cref="DataTableAttributes"/> for why.
/// </para>
/// <para>
/// Adaptor only. The table, its parsing from clipboard text and its checks belong
/// to <see cref="Table"/> and <see cref="DelimitedText"/>; this unpacks a tree into
/// one, packs one back into a tree, and opens the editor.
/// </para>
/// <para>
/// The table lives in the <c>.gh</c>, so it is for hand-sized data — hundreds to a
/// few thousand rows. Past that, a definition gets slow to open and the right tool
/// is a file read by Read Dataset; the description says so.
/// </para>
/// </summary>
public sealed class DataTableComponent : GH_Component
{
    private const int DataInput = 0;
    private const int DataOutput = 0;
    private const int HeadersOutput = 1;

    /// <summary>The table typed or pasted here. Kept while a wire is attached.</summary>
    private Table _table = Table.Empty;

    /// <summary>
    /// What the editor opens on and the menu copies: <see cref="_table"/> when
    /// editing, the wired tree read as a table when viewing.
    /// </summary>
    private Table _shown = Table.Empty;

    public DataTableComponent()
        : base("Data Table", "Table",
               "A table you can type or paste into, or a view of whatever tree is wired in.\n\n"
               + "With nothing wired, double-click to open the editor: type cells, paste a block copied "
               + "from a spreadsheet or a text file, or open a CSV or JSON file. Each row comes out as one "
               + "branch and each column as one item in it — the shape OtterCluster, OtterTrain and Write "
               + "Dataset take — and Headers carries the column names. Mark a column as numbers or text in the "
               + "editor: a column of 0 and 1 is numbers until you say it is a class.\n\n"
               + "With a tree wired in, the table shows it — one row per branch, one column per item — and "
               + "passes it through. The table you typed is kept until the wire goes.\n\n"
               + "The table is saved inside this definition, so keep it to hand-sized data. For thousands of "
               + "rows gathered over time, write them with Write Dataset and read them back with Read Dataset.",
               Categories.Root, Categories.Dataset)
    {
    }

    public override Guid ComponentGuid => new("5c2e9f4a-7b31-4d86-a2f0-9e6d1c8b3a57");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("datatable", 24);

    public override void CreateAttributes() => m_attributes = new DataTableAttributes(this);


    /// <summary>True while a tree is wired in and the component is a viewer.</summary>
    internal bool IsViewing => Params.Input[DataInput].SourceCount > 0;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter("Data", "D",
            "Optional. Wire a tree in to see it as a table: one row per branch, one column per item. "
            + "Leave it empty to type or paste a table of your own.",
            GH_ParamAccess.tree);
        pManager[DataInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddGenericParameter("Data", "D",
            "One branch per row, one item per column. A column marked as numbers comes out as numbers, "
            + "any other as text, and a blank cell as nothing. With a tree wired in, that tree passed through.",
            GH_ParamAccess.tree);
        pManager.AddTextParameter("Headers", "H",
            "The column names in order — Write Dataset's Feature Names. Empty when viewing a wired tree.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (IsViewing)
            View(da);
        else
            Edit(da);
    }

    private void View(IGH_DataAccess da)
    {
        if (!da.GetDataTree(DataInput, out GH_Structure<IGH_Goo> tree))
            return;

        _shown = FromTree(tree, out bool ragged);

        da.SetDataTree(DataOutput, tree);
        da.SetDataList(HeadersOutput, Array.Empty<string>());

        if (ragged)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "The branches differ in length. Short rows are padded with blanks here; anything learning "
                + "from this will want every branch the same length.");

        Message = $"{_shown.RowCount} × {_shown.ColumnCount}\nviewing";
    }

    private void Edit(IGH_DataAccess da)
    {
        _shown = _table;

        if (_table.IsEmpty)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Double-click to type or paste a table, or wire a tree in to view it.");
            Message = _table.ColumnCount == 0 ? "empty" : $"0 × {_table.ColumnCount}";
            da.SetDataList(HeadersOutput, _table.Columns.Select(Header));
            return;
        }

        var tree = new GH_Structure<IGH_Goo>();
        for (int i = 0; i < _table.RowCount; i++)
        {
            var path = new GH_Path(i);
            for (int j = 0; j < _table.ColumnCount; j++)
            {
                string cell = _table[i, j];

                // A blank goes out as nothing rather than as an empty string or a
                // zero: the downstream components refuse a null by position, which
                // is the complaint a user can act on. An empty string would be a
                // class; a zero would be a measurement.
                if (string.IsNullOrWhiteSpace(cell))
                {
                    tree.Append(null!, path);
                    continue;
                }

                if (_table.Columns[j].Kind == ColumnKind.Number && _table.TryNumber(i, j, out double value))
                    tree.Append(new GH_Number(value), path);
                else
                    tree.Append(new GH_String(cell), path);
            }
        }

        var problems = _table.Problems(limit: 5);
        if (problems.Count > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Some cells are blank or not the numbers their column says: " + string.Join(" ", problems));

        da.SetDataTree(DataOutput, tree);
        da.SetDataList(HeadersOutput, _table.Columns.Select(Header));
        Message = $"{_table.RowCount} × {_table.ColumnCount}";
    }

    /// <summary>A tree as a table: rows are branches, columns are items, cells are the items' text.</summary>
    private static Table FromTree(GH_Structure<IGH_Goo> tree, out bool ragged)
    {
        var branches = tree.Branches;
        int width = branches.Count == 0 ? 0 : branches.Max(b => b.Count);
        ragged = branches.Any(b => b.Count != width);

        var rows = new List<IReadOnlyList<string?>>(branches.Count);
        foreach (var branch in branches)
            rows.Add(branch.Select(goo => goo?.ToString() ?? string.Empty).ToArray());

        return Table.FromRows(null, rows);
    }

    private static string Header(TableColumn column) => column.Name;

    // ---------------------------------------------------------------- editing

    /// <summary>Replaces the table, as one undoable step, and re-solves.</summary>
    internal void Apply(Table table)
    {
        RecordUndoEvent("Edit table");
        _table = table;
        ExpireSolution(true);
    }

    /// <summary>
    /// Opens the editor on the table — read-only over a wired tree, so a long tree
    /// can still be scrolled through and copied out.
    /// </summary>
    internal void OpenEditor()
    {
        if (IsViewing)
        {
            DataTableEditor.Show(_shown, readOnly: true, NickName);
            return;
        }

        var edited = DataTableEditor.Show(_table, readOnly: false, NickName);
        if (edited is not null)
            Apply(edited);
    }

    private void PasteFromClipboard()
    {
        string text;
        try
        {
            text = Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            Apply(DelimitedText.Parse(text));
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show(ex.Message, "Data Table", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopyToClipboard()
    {
        if (_shown.IsEmpty)
            return;

        // Tabs, because that is what a spreadsheet reads back into cells.
        try
        {
            Clipboard.SetText(DelimitedText.Write(_shown, '\t'));
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
    }

    // ----------------------------------------------------------------- saving

    public override bool Write(GH_IWriter writer)
    {
        // Names and kinds by index, cells as one comma-separated block without a
        // header: a name can hold a comma, and a blank name has to come back blank
        // rather than as whatever the parser would call an unnamed column.
        writer.SetInt32("Columns", _table.ColumnCount);
        writer.SetInt32("Rows", _table.RowCount);
        for (int j = 0; j < _table.ColumnCount; j++)
        {
            writer.SetString("Name", j, _table.Columns[j].Name);
            writer.SetInt32("Kind", j, (int)_table.Columns[j].Kind);
        }

        writer.SetString("Cells", DelimitedText.Write(_table, ',', header: false));

        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        _table = Table.Empty;

        int columns = 0;
        if (reader.TryGetInt32("Columns", ref columns) && columns > 0)
        {
            var named = new TableColumn[columns];
            for (int j = 0; j < columns; j++)
            {
                string? name = null;
                int kind = (int)ColumnKind.Category;
                reader.TryGetString("Name", j, ref name);
                reader.TryGetInt32("Kind", j, ref kind);
                named[j] = new TableColumn(name ?? string.Empty, (ColumnKind)kind);
            }

            string? text = null;
            reader.TryGetString("Cells", ref text);

            var parsed = string.IsNullOrWhiteSpace(text)
                ? Table.Empty
                : DelimitedText.Parse(text!, new DelimitedTextOptions { Delimiter = ',', HasHeader = false, InferKinds = false });

            // The block is written as wide as the columns, so a mismatch means a
            // hand-edited file; the cells that fit are kept rather than all lost.
            var cells = new string?[parsed.RowCount, columns];
            for (int i = 0; i < parsed.RowCount; i++)
                for (int j = 0; j < Math.Min(columns, parsed.ColumnCount); j++)
                    cells[i, j] = parsed[i, j];

            try
            {
                _table = Table.Create(named, cells);
            }
            catch (ArgumentException)
            {
                _table = Table.Empty;
            }
        }

        return base.Read(reader);
    }

    // ------------------------------------------------------------------- menu

    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);

        Menu_AppendItem(menu, IsViewing ? "View table…" : "Edit table…", (_, _) => OpenEditor());
        Menu_AppendItem(menu, "Paste table from clipboard", (_, _) => PasteFromClipboard(), !IsViewing);
        Menu_AppendItem(menu, "Copy table to clipboard", (_, _) => CopyToClipboard(), !_shown.IsEmpty);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Clear table", (_, _) => Apply(Table.Empty), !IsViewing && _table.ColumnCount > 0);
    }
}
