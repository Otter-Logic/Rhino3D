using Eto.Forms;
using OtterLogic.Dataset.Data;
using OtterLogic.Dataset.Tables;
using OtterLogic.Dataset.Text;

// Eto and WinForms both name a Button, a Label, a Clipboard and the rest, and the
// Grasshopper project imports WinForms globally. The aliases pick Eto for every
// name this file uses that both define.
using Binding = Eto.Forms.Binding;
using Button = Eto.Forms.Button;
using Clipboard = Eto.Forms.Clipboard;
using Color = Eto.Drawing.Color;
using Colors = Eto.Drawing.Colors;
using Control = Eto.Forms.Control;
using DialogResult = Eto.Forms.DialogResult;
using KeyEventArgs = Eto.Forms.KeyEventArgs;
using Keys = Eto.Forms.Keys;
using Label = Eto.Forms.Label;
using MessageBox = Eto.Forms.MessageBox;
using OpenFileDialog = Eto.Forms.OpenFileDialog;
using Orientation = Eto.Forms.Orientation;
using Padding = Eto.Drawing.Padding;
using Panel = Eto.Forms.Panel;
using Size = Eto.Drawing.Size;
using TextAlignment = Eto.Forms.TextAlignment;
using TextBox = Eto.Forms.TextBox;

namespace OtterLogic.Grasshopper.Components.Dataset;

/// <summary>
/// The spreadsheet-like window behind <see cref="DataTableComponent"/>: a grid of
/// editable cells with a selection of its own, paste from the clipboard, a file to
/// open, and a find box.
/// <para>
/// Eto rather than WinForms because Rhino ships Eto on both platforms and
/// Grasshopper runs on Mac; a WinForms <c>DataGridView</c> would not. Eto's grid
/// selects rows, edits one cell at a time and has no notion of pasting a block, so
/// the three things a spreadsheet user expects are built on top of it here: a set
/// of selected <em>cells</em> painted through <c>CellFormatting</c>, Ctrl+V handed
/// to <see cref="DelimitedText"/>, and every row and column button acting on the
/// selection rather than on the end of the list.
/// </para>
/// <para>
/// The editor keeps its own mutable rows and builds an immutable
/// <see cref="Table"/> when the user presses OK, so Cancel costs nothing and the
/// component never sees a half-edited table.
/// </para>
/// </summary>
internal sealed class DataTableEditor : Dialog<bool>
{
    /// <summary>One row of the grid. Its index is kept so the row-number column need not search for it.</summary>
    private sealed class Row
    {
        public int Index;
        public List<string> Cells = new();
    }

    private static readonly Color SelectedFill = Color.FromArgb(0x99, 0xC2, 0xF0);
    private static readonly Color MatchFill = Color.FromArgb(0xFF, 0xE0, 0x80);
    private static readonly Color ProblemFill = Color.FromArgb(0xF6, 0xD6, 0xD6);
    private static readonly Color IndexFill = Color.FromArgb(0xEE, 0xEE, 0xEE);

    private readonly bool _readOnly;
    private readonly List<TableColumn> _columns = new();
    private readonly List<Row> _rows = new();

    private readonly GridView _grid = new()
    {
        ShowHeader = true,
        AllowMultipleSelection = true,
        GridLines = GridLines.Both,
    };

    private readonly TextBox _find = new() { PlaceholderText = "Find…", Width = 180 };
    private readonly Label _status = new();
    private readonly Label _hint = new() { TextColor = Colors.Gray };

    /// <summary>
    /// The selected cells, and the one the selection grew from. A set rather than a
    /// rectangle so that a find can select every match at once; a rectangle is the
    /// common case and is what a shift-click builds.
    /// </summary>
    private readonly HashSet<(int Row, int Col)> _selected = new();
    private (int Row, int Col) _anchor = (-1, -1);

    /// <summary>Cells the find box currently matches, painted whether or not they stay selected.</summary>
    private readonly HashSet<(int Row, int Col)> _matches = new();

    private Table? _result;

    private DataTableEditor(Table table, bool readOnly, string title)
    {
        _readOnly = readOnly;

        Title = readOnly ? $"{title} — viewing" : $"{title} — Data Table";
        ClientSize = new Size(900, 540);
        MinimumSize = new Size(560, 340);
        Resizable = true;
        Padding = new Padding(10);

        _grid.CellClick += OnCellClick;
        _grid.ColumnHeaderClick += OnHeaderClick;
        _grid.CellFormatting += OnCellFormatting;
        _grid.CellEdited += (_, _) => UpdateStatus();
        _grid.KeyDown += OnKeyDown;
        _find.TextChanged += (_, _) => Find(_find.Text);

        LoadTable(table);

        var ok = new Button { Text = readOnly ? "Close" : "OK" };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Text = "Cancel", Visible = !readOnly };
        cancel.Click += (_, _) => Close(false);

        DefaultButton = ok;
        AbortButton = readOnly ? ok : cancel;

        _hint.Text = readOnly
            ? "Click a cell, Shift+click to extend, click a header for the column. Ctrl+C copies the selection."
            : "Click a cell, Shift+click to extend, click a header for the column. Ctrl+C / Ctrl+V copy and paste, Delete clears.";

        var bottom = new TableLayout(
            new TableRow(
                new TableCell(new StackLayout { Spacing = 2, Items = { _status, _hint } }, scaleWidth: true),
                new TableCell(new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6, Items = { ok, cancel } })))
        {
            Spacing = new Size(6, 0),
        };

        Content = new TableLayout
        {
            Spacing = new Size(6, 8),
            Rows =
            {
                new TableRow(Toolbar()),
                new TableRow(_grid) { ScaleHeight = true },
                new TableRow(bottom),
            },
        };
    }

    /// <summary>
    /// Opens the editor modally over Rhino's main window and returns the edited
    /// table, or null when the user cancelled or was only looking.
    /// </summary>
    public static Table? Show(Table table, bool readOnly, string title)
    {
        var editor = new DataTableEditor(table, readOnly, title);
        bool accepted = editor.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow);
        return accepted && !readOnly ? editor._result : null;
    }

    // ---------------------------------------------------------------- toolbar

    private Control Toolbar()
    {
        var edit = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 4, VerticalContentAlignment = VerticalAlignment.Center };

        void Add(string text, Action action, string tip, bool editing = true)
        {
            var button = new Button { Text = text, Enabled = !editing || !_readOnly, ToolTip = tip };
            button.Click += (_, _) => action();
            edit.Items.Add(button);
        }

        void Gap() => edit.Items.Add(new Panel { Width = 10 });

        Add("Paste", Paste, "Paste the clipboard at the selected cell (Ctrl+V). Into an empty table, the first row is read as the headers when it looks like one.");
        Add("Copy", Copy, "Copy the selected cells, or the whole table with its headers when nothing is selected (Ctrl+C).", editing: false);
        Add("Open…", OpenFile, "Replace the table with a CSV, TSV, TXT or JSON file.");
        Gap();
        Add("Add row", AddRow, "Insert a row below the selection, or at the end.");
        Add("Delete row", DeleteRows, "Delete every row the selection touches.");
        Add("Add column", AddColumn, "Insert a column to the right of the selection, or at the end.");
        Add("Delete column", DeleteColumns, "Delete every column the selection touches.");
        Add("Column…", EditColumn, "Name the selected column and say whether it holds numbers or text.");
        Gap();
        Add("Clear", () => LoadTable(Table.Empty), "Remove every row and column.");

        return new TableLayout(
            new TableRow(
                new TableCell(edit),
                new TableCell(null, scaleWidth: true),
                new TableCell(_find)))
        {
            Spacing = new Size(6, 0),
        };
    }

    // -------------------------------------------------------------- selection

    private void OnCellClick(object? sender, GridCellMouseEventArgs e)
    {
        if (e.Row < 0)
            return;

        int column = e.Column - 1;
        bool extend = e.Modifiers.HasFlag(Keys.Shift);

        // The row-number column selects the row.
        if (column < 0)
        {
            SelectRectangle(extend && _anchor.Row >= 0 ? _anchor.Row : e.Row, 0, e.Row, _columns.Count - 1, anchor: (e.Row, 0));
            return;
        }

        if (extend && _anchor.Row >= 0)
            SelectRectangle(_anchor.Row, _anchor.Col, e.Row, column, _anchor);
        else
            SelectRectangle(e.Row, column, e.Row, column, (e.Row, column));
    }

    private void OnHeaderClick(object? sender, GridColumnEventArgs e)
    {
        int column = _grid.Columns.IndexOf(e.Column) - 1;
        if (column < 0 || _rows.Count == 0)
            return;

        bool extend = _anchor.Col >= 0 && Keyboard.Modifiers.HasFlag(Keys.Shift);
        int from = extend ? _anchor.Col : column;
        SelectRectangle(0, from, _rows.Count - 1, column, anchor: (0, from));
    }

    private void SelectRectangle(int r0, int c0, int r1, int c1, (int Row, int Col) anchor)
    {
        _selected.Clear();
        _anchor = anchor;

        if (_rows.Count == 0 || _columns.Count == 0)
        {
            Repaint();
            return;
        }

        int rowFrom = Math.Clamp(Math.Min(r0, r1), 0, _rows.Count - 1);
        int rowTo = Math.Clamp(Math.Max(r0, r1), 0, _rows.Count - 1);
        int colFrom = Math.Clamp(Math.Min(c0, c1), 0, _columns.Count - 1);
        int colTo = Math.Clamp(Math.Max(c0, c1), 0, _columns.Count - 1);

        for (int i = rowFrom; i <= rowTo; i++)
            for (int j = colFrom; j <= colTo; j++)
                _selected.Add((i, j));

        Repaint();
    }

    private void SelectAll()
    {
        if (_rows.Count == 0 || _columns.Count == 0)
            return;

        SelectRectangle(0, 0, _rows.Count - 1, _columns.Count - 1, (0, 0));
    }

    private void ClearSelection()
    {
        _selected.Clear();
        _anchor = (-1, -1);
    }

    /// <summary>The rows the selection touches, ascending.</summary>
    private int[] SelectedRows() => _selected.Select(s => s.Row).Distinct().OrderBy(r => r).ToArray();

    /// <summary>The columns the selection touches, ascending.</summary>
    private int[] SelectedColumns() => _selected.Select(s => s.Col).Distinct().OrderBy(c => c).ToArray();

    /// <summary>
    /// Selects every cell whose text contains <paramref name="term"/>, ignoring
    /// case, and scrolls to the first. An empty term clears the matches and leaves
    /// whatever was selected alone.
    /// </summary>
    private void Find(string? term)
    {
        _matches.Clear();
        term = (term ?? string.Empty).Trim();

        if (term.Length > 0)
        {
            for (int i = 0; i < _rows.Count; i++)
                for (int j = 0; j < _columns.Count; j++)
                    if (_rows[i].Cells[j].Contains(term, StringComparison.OrdinalIgnoreCase))
                        _matches.Add((i, j));

            _selected.Clear();
            foreach (var match in _matches)
                _selected.Add(match);

            if (_matches.Count > 0)
            {
                var first = _matches.OrderBy(m => m.Row).ThenBy(m => m.Col).First();
                _anchor = first;
                _grid.ScrollToRow(first.Row);
            }
        }

        Repaint();
    }

    private void OnCellFormatting(object? sender, GridCellFormatEventArgs e)
    {
        int column = _grid.Columns.IndexOf(e.Column) - 1;

        if (column < 0)
        {
            e.BackgroundColor = IndexFill;
            e.ForegroundColor = Colors.Gray;
            return;
        }

        var cell = (e.Row, column);
        if (_selected.Contains(cell))
            e.BackgroundColor = SelectedFill;
        else if (_matches.Contains(cell))
            e.BackgroundColor = MatchFill;
        else if (e.Row >= 0 && e.Row < _rows.Count && IsProblem(e.Row, column))
            e.BackgroundColor = ProblemFill;
    }

    /// <summary>A blank, or text in a column that says it holds numbers — the cells the component will warn about.</summary>
    private bool IsProblem(int row, int column)
    {
        string text = _rows[row].Cells[column];
        if (string.IsNullOrWhiteSpace(text))
            return true;

        return _columns[column].Kind == ColumnKind.Number && Table.InferKind(new[] { text }) != ColumnKind.Number;
    }

    /// <summary>Repaints every row so the selection and match colours follow the sets.</summary>
    private void Repaint()
    {
        if (_rows.Count > 0)
            _grid.ReloadData(Enumerable.Range(0, _rows.Count));

        UpdateStatus();
    }

    // --------------------------------------------------------------- keyboard

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool control = e.Modifiers.HasFlag(Keys.Control) || e.Modifiers.HasFlag(Keys.Application);

        if (control && e.Key == Keys.V && !_readOnly)
        {
            Paste();
            e.Handled = true;
        }
        else if (control && e.Key == Keys.C)
        {
            Copy();
            e.Handled = true;
        }
        else if (control && e.Key == Keys.A)
        {
            SelectAll();
            e.Handled = true;
        }
        else if (control && e.Key == Keys.F)
        {
            _find.Focus();
            e.Handled = true;
        }
        else if ((e.Key == Keys.Delete || e.Key == Keys.Backspace) && !_readOnly && _selected.Count > 0)
        {
            ClearSelectedCells();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ model

    /// <summary>Replaces everything in the grid with <paramref name="table"/>.</summary>
    private void LoadTable(Table table)
    {
        _columns.Clear();
        _columns.AddRange(table.Columns);

        _rows.Clear();
        for (int i = 0; i < table.RowCount; i++)
            _rows.Add(new Row { Index = i, Cells = table.Row(i).ToList() });

        ClearSelection();
        _matches.Clear();

        RebuildColumns();
        Reload();
    }

    /// <summary>The grid as an immutable table. Throws when two columns share a name.</summary>
    private Table Current()
    {
        var cells = new string?[_rows.Count, _columns.Count];
        for (int i = 0; i < _rows.Count; i++)
            for (int j = 0; j < _columns.Count; j++)
                cells[i, j] = _rows[i].Cells[j];

        return Table.Create(_columns, cells);
    }

    private void Accept()
    {
        if (_readOnly)
        {
            Close(true);
            return;
        }

        try
        {
            _result = Current();
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "Data Table", MessageBoxType.Warning);
            return;
        }

        Close(true);
    }

    private void RebuildColumns()
    {
        _grid.Columns.Clear();

        _grid.Columns.Add(new GridColumn
        {
            HeaderText = string.Empty,
            Width = 44,
            Editable = false,
            Resizable = false,
            Sortable = false,
            DataCell = new TextBoxCell
            {
                TextAlignment = TextAlignment.Right,
                Binding = Binding.Delegate<Row, string>(r => r.Index.ToString()),
            },
        });

        for (int j = 0; j < _columns.Count; j++)
        {
            int column = j;
            bool number = _columns[j].Kind == ColumnKind.Number;
            _grid.Columns.Add(new GridColumn
            {
                HeaderText = HeaderText(column),
                Width = 120,
                Editable = !_readOnly,
                Sortable = false,
                DataCell = new TextBoxCell
                {
                    TextAlignment = number ? TextAlignment.Right : TextAlignment.Left,
                    Binding = Binding.Delegate<Row, string>(
                        r => r.Cells[column],
                        (r, value) => r.Cells[column] = value ?? string.Empty),
                },
            });
        }
    }

    private string HeaderText(int j)
    {
        var column = _columns[j];
        string name = column.Name.Length > 0 ? column.Name : $"({TableColumn.DefaultName(j)})";
        return column.Kind == ColumnKind.Number ? name + "  #" : name;
    }

    /// <summary>
    /// Hands the grid its rows again. A fresh list every time rather than an
    /// observable one: structural edits are rare and small, and one code path that
    /// always works beats two that must agree about what changed.
    /// </summary>
    private void Reload()
    {
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].Index = i;

        _grid.DataStore = null;
        _grid.DataStore = _rows.ToList();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int blanks = _rows.Sum(r => r.Cells.Count(c => string.IsNullOrWhiteSpace(c)));
        var parts = new List<string> { $"{_rows.Count} rows × {_columns.Count} columns" };
        if (blanks > 0)
            parts.Add($"{blanks} blank");
        if (_selected.Count > 0)
            parts.Add($"{_selected.Count} selected");
        if (_find.Text?.Trim().Length > 0)
            parts.Add($"{_matches.Count} match{(_matches.Count == 1 ? string.Empty : "es")}");
        if (_readOnly)
            parts.Add("viewing a wired tree");

        _status.Text = string.Join("  ·  ", parts);
    }

    /// <summary>Grows the grid so that <paramref name="rows"/> by <paramref name="columns"/> fits. New columns are text until something says otherwise.</summary>
    private void EnsureSize(int rows, int columns)
    {
        while (_columns.Count < columns)
        {
            _columns.Add(TableColumn.Category(TableColumn.DefaultName(_columns.Count)));
            foreach (var row in _rows)
                row.Cells.Add(string.Empty);
        }

        while (_rows.Count < rows)
            _rows.Add(new Row { Cells = Enumerable.Repeat(string.Empty, _columns.Count).ToList() });
    }

    // ---------------------------------------------------------------- actions

    private void Paste()
    {
        if (_readOnly)
            return;

        string text = Clipboard.Instance.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return;

        Table pasted;
        try
        {
            // Into an empty grid, the clipboard is the whole table and its header
            // is read. Into a grid with columns, it is a block of cells landing at
            // the selection, and its first row is cells like the rest.
            pasted = _columns.Count == 0
                ? DelimitedText.Parse(text)
                : DelimitedText.Parse(text, new DelimitedTextOptions { HasHeader = false, InferKinds = false });
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show(this, ex.Message, "Paste", MessageBoxType.Warning);
            return;
        }

        if (pasted.IsEmpty)
            return;

        if (_columns.Count == 0)
        {
            LoadTable(pasted);
            return;
        }

        int r0 = _selected.Count > 0 ? _selected.Min(s => s.Row) : 0;
        int c0 = _selected.Count > 0 ? _selected.Min(s => s.Col) : 0;
        int before = _columns.Count;

        EnsureSize(r0 + pasted.RowCount, c0 + pasted.ColumnCount);
        for (int i = 0; i < pasted.RowCount; i++)
            for (int j = 0; j < pasted.ColumnCount; j++)
                _rows[r0 + i].Cells[c0 + j] = pasted[i, j];

        // A column that did not exist before the paste takes its kind from what
        // was pasted into it; an existing column keeps whatever the user decided.
        for (int j = before; j < _columns.Count; j++)
            _columns[j] = _columns[j] with { Kind = Table.InferKind(_rows.Select(r => r.Cells[j])) };

        RebuildColumns();
        Reload();
        SelectRectangle(r0, c0, r0 + pasted.RowCount - 1, c0 + pasted.ColumnCount - 1, (r0, c0));
    }

    /// <summary>
    /// The selected block to the clipboard as tabs, cells outside the selection
    /// blank; with nothing selected, the whole table with its headers.
    /// </summary>
    private void Copy()
    {
        if (_columns.Count == 0)
            return;

        if (_selected.Count == 0)
        {
            Clipboard.Instance.Text = DelimitedText.Write(Named(), '\t');
            return;
        }

        int r0 = _selected.Min(s => s.Row), r1 = _selected.Max(s => s.Row);
        int c0 = _selected.Min(s => s.Col), c1 = _selected.Max(s => s.Col);
        var cells = new string?[r1 - r0 + 1, c1 - c0 + 1];
        for (int i = r0; i <= r1; i++)
            for (int j = c0; j <= c1; j++)
                cells[i - r0, j - c0] = _selected.Contains((i, j)) ? _rows[i].Cells[j] : string.Empty;

        var columns = Enumerable.Range(c0, c1 - c0 + 1).Select(j => TableColumn.Category(TableColumn.DefaultName(j))).ToArray();
        Clipboard.Instance.Text = DelimitedText.Write(Table.Create(columns, cells), '\t', header: false);
    }

    /// <summary>The grid as a table even when two columns share a name — a copy is not a contract.</summary>
    private Table Named()
    {
        try
        {
            return Current();
        }
        catch (ArgumentException)
        {
            var cells = new string?[_rows.Count, _columns.Count];
            for (int i = 0; i < _rows.Count; i++)
                for (int j = 0; j < _columns.Count; j++)
                    cells[i, j] = _rows[i].Cells[j];
            return Table.Create(_columns.Select((c, j) => c with { Name = TableColumn.DefaultName(j) }).ToArray(), cells);
        }
    }

    private void OpenFile()
    {
        if (_readOnly)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Open a table",
            Filters =
            {
                new FileFilter("Tables", ".csv", ".tsv", ".txt", ".json"),
                new FileFilter("All files", ".*"),
            },
        };

        if (dialog.ShowDialog(this) != DialogResult.Ok)
            return;

        try
        {
            string text = File.ReadAllText(dialog.FileName);
            bool json = dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            LoadTable(json ? TableJson.Parse(text) : DelimitedText.Parse(text));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Open a table", MessageBoxType.Error);
        }
    }

    private void ClearSelectedCells()
    {
        foreach (var (row, col) in _selected)
            _rows[row].Cells[col] = string.Empty;
        Repaint();
    }

    private void AddRow()
    {
        if (_readOnly)
            return;

        if (_columns.Count == 0)
            InsertColumnAt(0);

        var rows = SelectedRows();
        int at = rows.Length == 0 ? _rows.Count : rows[^1] + 1;
        _rows.Insert(at, new Row { Cells = Enumerable.Repeat(string.Empty, _columns.Count).ToList() });

        Reload();
        SelectRectangle(at, 0, at, _columns.Count - 1, (at, 0));
    }

    private void AddColumn()
    {
        if (_readOnly)
            return;

        var columns = SelectedColumns();
        int at = columns.Length == 0 ? _columns.Count : columns[^1] + 1;
        InsertColumnAt(at);

        RebuildColumns();
        Reload();
        if (_rows.Count > 0)
            SelectRectangle(0, at, _rows.Count - 1, at, (0, at));
    }

    private void InsertColumnAt(int at)
    {
        at = Math.Clamp(at, 0, _columns.Count);
        _columns.Insert(at, TableColumn.Category(string.Empty));
        foreach (var row in _rows)
            row.Cells.Insert(at, string.Empty);
    }

    private void DeleteRows()
    {
        if (_readOnly)
            return;

        var rows = SelectedRows();
        if (rows.Length == 0)
        {
            _status.Text = "Select a cell in the row to delete.";
            return;
        }

        foreach (int row in rows.OrderByDescending(r => r))
            _rows.RemoveAt(row);

        ClearSelection();
        Reload();
    }

    private void DeleteColumns()
    {
        if (_readOnly)
            return;

        var columns = SelectedColumns();
        if (columns.Length == 0)
        {
            _status.Text = "Select a cell in the column to delete.";
            return;
        }

        foreach (int column in columns.OrderByDescending(c => c))
        {
            _columns.RemoveAt(column);
            foreach (var row in _rows)
                row.Cells.RemoveAt(column);
        }

        ClearSelection();
        RebuildColumns();
        Reload();
    }

    /// <summary>
    /// Name and kind of the selected column. With several columns selected the
    /// kind applies to all of them and the name to the one the selection grew from,
    /// since two columns cannot share a name.
    /// </summary>
    private void EditColumn()
    {
        if (_readOnly || _columns.Count == 0)
            return;

        var columns = SelectedColumns();
        if (columns.Length == 0)
        {
            _status.Text = "Select a cell in the column to edit, or click its header.";
            return;
        }

        int primary = _anchor.Col >= 0 && columns.Contains(_anchor.Col) ? _anchor.Col : columns[0];
        var column = _columns[primary];

        var name = new TextBox { Text = column.Name, Width = 240 };
        var kind = new DropDown
        {
            DataStore = new[] { "Numbers", "Text or classes" },
            SelectedIndex = column.Kind == ColumnKind.Number ? 0 : 1,
        };

        var ok = new Button { Text = "OK" };
        var cancel = new Button { Text = "Cancel" };

        string scope = columns.Length > 1 ? $"Kind applies to all {columns.Length} selected columns." : string.Empty;

        var dialog = new Dialog<bool>
        {
            Title = columns.Length > 1 ? $"Columns {primary} and {columns.Length - 1} more" : $"Column {primary}",
            Padding = new Padding(12),
            DefaultButton = ok,
            AbortButton = cancel,
            Content = new TableLayout
            {
                Spacing = new Size(8, 8),
                Rows =
                {
                    new TableRow(new Label { Text = "Name" }, name),
                    new TableRow(new Label { Text = "Holds" }, kind),
                    new TableRow(new TableCell(new Label
                    {
                        Text = "A column of 0 and 1 is numbers until you say it holds classes. " + scope,
                        TextColor = Colors.Gray,
                        Wrap = WrapMode.Word,
                        Width = 320,
                    }, scaleWidth: true)) { },
                    new TableRow(new TableCell(new StackLayout
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Items = { new StackLayoutItem(null, expand: true), ok, cancel },
                    }, scaleWidth: true)),
                },
            },
        };

        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        if (!dialog.ShowModal(this))
            return;

        var newKind = kind.SelectedIndex == 0 ? ColumnKind.Number : ColumnKind.Category;
        foreach (int j in columns)
            _columns[j] = _columns[j] with { Kind = newKind };
        _columns[primary] = _columns[primary] with { Name = (name.Text ?? string.Empty).Trim() };

        RebuildColumns();
        Reload();
        if (_rows.Count > 0)
            SelectRectangle(0, columns[0], _rows.Count - 1, columns[^1], (0, primary));
    }
}
