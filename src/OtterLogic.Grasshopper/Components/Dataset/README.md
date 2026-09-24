# Dataset components

Getting data ready before anything learns from it. Cut for the person who has
results in a spreadsheet, a text file or an analysis export and wants them on a
wire — a different person, on a different day, from the one choosing a
clustering method one panel to the right. Adaptors only: the table, its parsing
and the dataset folder live in
[Dataset](https://github.com/Otter-Logic/Dataset); the shape signature in
[MachineLearning](https://github.com/Otter-Logic/MachineLearning).

| Tier (`GH_Exposure`) | Component | Library call |
|---|---|---|
| `primary` — the table | **Data Table** | `Table`, `DelimitedText.Parse` / `Write`, `TableJson.Parse` |
| `secondary` — datasets on disk | **Write Dataset** | `SampleTable.FromColumns`, `DatasetFolder.Write` |
| | **Read Dataset** | `DatasetFolder.Read` |
| `tertiary` — features from geometry | **Shape Signature** | `ShapeSignature.Fit` |

## Data Table

A table on the canvas, in two modes decided by whether anything is wired in —
the same rule the native Panel uses.

| Input | Behaviour | Output |
|---|---|---|
| nothing wired | editable: double-click opens the editor | one branch per row, one item per column; Headers carries the names |
| a tree wired | a view of it: one row per branch, one column per item | the tree, passed through; Headers empty |

The typed table is kept while a wire is attached and comes back when it goes.

**The editor** is an Eto `GridView` in a modal dialog, so it works wherever
Grasshopper does. Cells edit in place. Eto's grid selects rows, so the editor
keeps a selection of *cells* of its own, painted through `CellFormatting`: click
a cell, Shift+click to extend to a rectangle, click a header for the column,
click a row number for the row, Ctrl+A for everything. Every row and column
button acts on that selection — *Add row* inserts below it, *Delete row* removes
every row it touches, likewise for columns — and Delete clears the selected
cells. The find box selects every cell containing the text and scrolls to the
first. Ctrl+V and *Paste* read the clipboard through `DelimitedText.Parse`: into
an empty grid the clipboard is the whole table and its header row is detected;
into a grid with columns it is a block landing at the selection, growing the
grid to fit. *Copy* takes the selected block, or the whole table with headers
when nothing is selected. *Open…* reads CSV, TSV, TXT and JSON. *Column…* names
the selected column and says whether it holds numbers or text; that is how a
pasted first row becomes headers — select it, copy, delete it, name the columns
— rather than a button that guesses. Blank cells and non-numbers in a number
column are tinted, since they are what the component will warn about.

**Kinds decide the wire.** A column marked as numbers comes out as `GH_Number`,
anything else as `GH_String`, and a blank cell as nothing — so OtterTrain reads a
class target as a class and the tree-reading helpers complain about a blank by
position. A column of 0 and 1 is inferred as numbers, deliberately: the rule has
to be mechanical to be predictable, and the place to say "this is a class" is
the column's kind.

**Saved in the `.gh`.** Names and kinds by index, cells as one comma-separated
block, through the component's `Write` and `Read`. That bounds it to hand-sized
data; the description says to use Write Dataset past a few thousand rows.

**On the canvas** it is an ordinary component whose message strip gives the
size; the custom attributes exist only to catch the double-click. A first cut
drew the first rows the way the Panel does, and the preview cost width without
being readable — the editor is where the table is read.

## Write Dataset and Read Dataset

Unchanged from the Machine Learning panel except for where they sit; see
`ml-components.md` in `docs/` for the folder format's reasons. Read Dataset's
outputs wire straight into OtterTrain.

## Not yet opened in Grasshopper

The Data Table component and its editor are exercised by the build and by
reading the code. The Eto grid, the clipboard round trip and the modal parent
are the places to look first if something misbehaves when it is.
