using System.Drawing;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using OtterLogic.Document;
using Rhino;
using Rhino.DocObjects.Tables;

namespace OtterLogic.Grasshopper.Components.Document;

/// <summary>
/// The Rhino layer tree as a tick list, giving back the full path of every
/// layer ticked.
/// <para>
/// This is the other half of the OtterFlatTruss command. That command bakes a run
/// onto <c>OtterFlatTruss1</c> with a sub-layer per section group — Top Chord,
/// Diagonal, and so on — precisely so the members can be picked up by role
/// later. Picking them up meant typing <c>OtterFlatTruss1::Diagonal</c> into a
/// panel by hand, once per group, and re-typing it whenever a layer was renamed.
/// Here the document itself is the list: tick the groups you want and the paths
/// come out ready for the Layer filter of Query Model Objects.
/// </para>
/// <para>
/// Rhino writes full paths with <c>::</c> between the levels, and that is what
/// comes out — no separator of our own invention, so what the component emits is
/// what every other Rhino and Grasshopper tool already expects.
/// </para>
/// <para>
/// Nothing here is specific to trusses. Any layer in the document can be ticked,
/// which is why it sits under Document rather than Structural Form.
/// </para>
/// </summary>
public sealed class LayerPickerComponent : GH_Component
{
    /// <summary>
    /// Ticked layers, held by full path rather than by index or id.
    /// <para>
    /// A path is the only handle that survives the round trip through a saved
    /// file: layer indices are per-document and ids are per-document too, so a
    /// definition opened against a rebuilt model would tick nothing, or worse,
    /// tick the wrong thing. A path also reads properly in the saved XML, which
    /// matters the day someone has to work out why a definition picked the layer
    /// it did.
    /// </para>
    /// <para>
    /// Case-insensitive, because Rhino treats layer names that way.
    /// </para>
    /// </summary>
    private readonly HashSet<string> _ticked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parents whose children are folded away, by full path.</summary>
    private readonly HashSet<string> _collapsed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The whole tree, depth first, in Rhino's own layer-panel order.</summary>
    private IReadOnlyList<LayerNode> _rows = Array.Empty<LayerNode>();

    /// <summary>The rows a collapsed parent is not hiding — what actually gets drawn.</summary>
    private IReadOnlyList<LayerNode> _visible = Array.Empty<LayerNode>();

    private bool _listening;

    public LayerPickerComponent()
        : base("Layer Picker", "Layers",
               "Tick layers in the Rhino document and get their full paths back, one per "
               + "ticked layer. Feed them to the Layer input of Query Model Objects to pull "
               + "out the objects on them — a section group per truss layer, with no typing.",
               Categories.Root, Categories.Document)
    {
    }

    public override Guid ComponentGuid => new("3d7529a0-ab42-4c67-bd60-e594f2bda35a");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap? Icon => EmbeddedIcons.Load("layerpicker", 24);

    public override void CreateAttributes() => m_attributes = new LayerPickerAttributes(this);

    internal IReadOnlyList<LayerNode> VisibleRows => _visible;

    internal bool IsTicked(LayerNode row) => _ticked.Contains(row.Path);

    internal bool IsCollapsed(LayerNode row) => row.HasChildren && _collapsed.Contains(row.Path);

    /// <summary>
    /// Whether anything folded away under this row is ticked. A collapsed parent
    /// draws differently when it is true, so folding a branch away never hides
    /// the fact that it is contributing to the output.
    /// </summary>
    internal bool HidesTicks(LayerNode row)
        => IsCollapsed(row) && _ticked.Any(path => LayerNode.IsUnder(path, row.Path));

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        // Deliberately none. Everything this component knows comes from the
        // document and from what has been ticked, and an input that could
        // disagree with the ticks would only raise the question of which wins.
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Layer", "L",
            "Full path of each ticked layer, in the order the Rhino layer panel lists them — "
            + "for example OtterFlatTruss1::Diagonal. Goes straight into the Layer filter of "
            + "Query Model Objects.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        // Document order rather than the order things were ticked: the list is a
        // reading of the layer panel, and it should not depend on which box was
        // clicked first.
        var paths = _rows.Where(row => _ticked.Contains(row.Path))
                         .Select(row => row.Path)
                         .ToList();

        // A tick for a layer that is no longer there is kept rather than dropped.
        // Deleting a layer is undoable, and quietly forgetting the tick would
        // make undo restore the layer but not the selection that went with it.
        int missing = _ticked.Count - paths.Count;
        if (missing > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{missing} ticked {(missing == 1 ? "layer is" : "layers are")} not in the document "
                + "and left out. They come back if the layer does.");

        if (paths.Count == 0 && missing == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Nothing ticked yet — click the layers you want.");

        da.SetDataList(0, paths);

        Message = $"{paths.Count} of {_rows.Count}";
    }

    // ---------------------------------------------------------------- ticking

    /// <summary>
    /// Turn one row's tick on or off, or every row in its branch at once.
    /// <para>
    /// A whole branch takes the state the row it was clicked on is moving to,
    /// rather than each descendant flipping its own: flipping individually turns
    /// a half-ticked branch inside out, which is never what shift-clicking a
    /// parent is asking for.
    /// </para>
    /// </summary>
    internal void Toggle(LayerNode row, bool wholeBranch)
    {
        RecordUndoEvent("Tick layer");

        bool ticked = !_ticked.Contains(row.Path);
        Apply(row.Path, ticked);

        if (wholeBranch)
            foreach (LayerNode other in _rows)
                if (other.IsUnder(row.Path))
                    Apply(other.Path, ticked);

        ExpireSolution(true);

        void Apply(string path, bool on)
        {
            if (on) _ticked.Add(path);
            else _ticked.Remove(path);
        }
    }

    /// <summary>Ticks or clears every layer in the document.</summary>
    internal void TickAll(bool ticked)
    {
        RecordUndoEvent(ticked ? "Tick all layers" : "Clear layer ticks");

        _ticked.Clear();
        if (ticked)
            foreach (LayerNode row in _rows)
                _ticked.Add(row.Path);

        ExpireSolution(true);
    }

    /// <summary>Folds a parent's children away, or opens them back up.</summary>
    internal void ToggleCollapse(LayerNode row)
    {
        if (!row.HasChildren) return;

        if (!_collapsed.Remove(row.Path))
            _collapsed.Add(row.Path);

        Rebuild();
    }

    /// <summary>Folds or opens every parent at once.</summary>
    internal void CollapseAll(bool collapsed)
    {
        _collapsed.Clear();

        if (collapsed)
            foreach (LayerNode row in _rows.Where(r => r.HasChildren))
                _collapsed.Add(row.Path);

        Rebuild();
    }

    // ------------------------------------------------------------ layer table

    /// <summary>
    /// Re-read the document's layers. Cheap enough to do on every layer table
    /// event: a document with a thousand layers is a list of a thousand small
    /// records, built once and drawn from thereafter.
    /// </summary>
    internal void ReadLayers()
    {
        _rows = LayerTree.Read(RhinoDoc.ActiveDoc);
        Rebuild();
    }

    /// <summary>Works out which rows a collapsed parent is hiding, and redraws.</summary>
    private void Rebuild()
    {
        _visible = LayerTree.Visible(_rows, _collapsed);

        Attributes?.ExpireLayout();
        Instances.RedrawCanvas();
    }

    /// <summary>
    /// Re-read the layers and re-solve, without doing either from inside
    /// Rhino's event. Grasshopper may be part way through a solution when a
    /// layer is added — baking a truss from a definition does exactly that —
    /// and expiring a component in the middle of one corrupts the run.
    /// </summary>
    private void ScheduleRefresh()
    {
        GH_Document? doc = OnPingDocument();
        if (doc is null) return;

        doc.ScheduleSolution(20, _ =>
        {
            ReadLayers();
            ExpireSolution(false);
        });
    }

    private void OnLayerTableEvent(object? sender, LayerTableEventArgs e) => ScheduleRefresh();

    private void OnDocumentOpened(object? sender, DocumentOpenEventArgs e) => ScheduleRefresh();

    private void OnActiveDocumentChanged(object? sender, DocumentEventArgs e) => ScheduleRefresh();

    private void Listen(bool listening)
    {
        if (listening == _listening) return;
        _listening = listening;

        if (listening)
        {
            RhinoDoc.LayerTableEvent += OnLayerTableEvent;
            RhinoDoc.EndOpenDocument += OnDocumentOpened;
            RhinoDoc.ActiveDocumentChanged += OnActiveDocumentChanged;
        }
        else
        {
            RhinoDoc.LayerTableEvent -= OnLayerTableEvent;
            RhinoDoc.EndOpenDocument -= OnDocumentOpened;
            RhinoDoc.ActiveDocumentChanged -= OnActiveDocumentChanged;
        }
    }

    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        Listen(true);
        ReadLayers();
    }

    public override void RemovedFromDocument(GH_Document document)
    {
        Listen(false);
        base.RemovedFromDocument(document);
    }

    /// <summary>
    /// Rhino's layer events are static, so a component that stopped listening
    /// only when it was deleted would keep a closed definition alive for as long
    /// as Rhino runs. Closing one lets go; opening it again takes hold.
    /// </summary>
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        base.DocumentContextChanged(document, context);

        switch (context)
        {
            case GH_DocumentContext.Open:
            case GH_DocumentContext.Loaded:
                Listen(true);
                ReadLayers();
                break;

            case GH_DocumentContext.Close:
            case GH_DocumentContext.Unloaded:
                Listen(false);
                break;
        }
    }

    // ----------------------------------------------------------------- saving

    public override bool Write(GH_IWriter writer)
    {
        writer.SetInt32("TickedCount", _ticked.Count);
        WriteAll("Ticked", _ticked);

        writer.SetInt32("CollapsedCount", _collapsed.Count);
        WriteAll("Collapsed", _collapsed);

        return base.Write(writer);

        void WriteAll(string name, IEnumerable<string> paths)
        {
            int i = 0;
            foreach (string path in paths)
                writer.SetString(name, i++, path);
        }
    }

    public override bool Read(GH_IReader reader)
    {
        ReadAll("TickedCount", "Ticked", _ticked);
        ReadAll("CollapsedCount", "Collapsed", _collapsed);

        return base.Read(reader);

        void ReadAll(string countName, string name, HashSet<string> into)
        {
            into.Clear();

            int count = 0;
            if (!reader.TryGetInt32(countName, ref count)) return;

            for (int i = 0; i < count; i++)
            {
                string? path = null;
                if (reader.TryGetString(name, i, ref path) && !string.IsNullOrEmpty(path))
                    into.Add(path!);
            }
        }
    }

    // ------------------------------------------------------------------- menu

    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);

        Menu_AppendItem(menu, "Tick all", (_, _) => TickAll(true), _rows.Count > 0);
        Menu_AppendItem(menu, "Tick none", (_, _) => TickAll(false), _ticked.Count > 0);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "Expand all", (_, _) => CollapseAll(false), _collapsed.Count > 0);
        Menu_AppendItem(menu, "Collapse all", (_, _) => CollapseAll(true), _rows.Any(r => r.HasChildren));
        Menu_AppendSeparator(menu);

        // The layer events cover every change Rhino tells us about. This is for
        // the ones it does not — a plug-in editing the table behind its back —
        // and for reassurance that what is on screen is what is in the document.
        Menu_AppendItem(menu, "Reload from Rhino", (_, _) =>
        {
            ReadLayers();
            ExpireSolution(true);
        });
    }
}
