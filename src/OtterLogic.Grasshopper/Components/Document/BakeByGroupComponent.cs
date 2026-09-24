using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using OtterLogic.Document;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.Document;

/// <summary>
/// Geometry and a label per piece in; the geometry baked into the Rhino document
/// onto one sub-layer per label, each its own colour, with the label written on
/// every object as user text, when Bake goes on.
/// <para>
/// Adaptor only: the layer tree is <see cref="GroupLayers"/>, the colours
/// <see cref="GroupPalette"/> and the user text <see cref="ObjectText"/>, all in
/// Document. Baking changes the document, so it happens on the rising edge of
/// Bake and not on every re-solve, as Write Attributes does.
/// </para>
/// </summary>
public sealed class BakeByGroupComponent : GH_Component
{
    private const int GeometryInput = 0;
    private const int LabelsInput = 1;
    private const int LayerInput = 2;
    private const int KeyInput = 3;
    private const int BakeInput = 4;

    private bool _wasBaking;

    public BakeByGroupComponent()
        : base("Bake By Group", "BakeGroups",
               "Bake geometry into the Rhino document sorted by a label per piece: one sub-layer per label "
               + "under a root layer, each sub-layer its own colour, and the label written on every object "
               + "as user text.\n\n"
               + "This is how a grouping leaves the canvas. Wire OtterCluster's Labels, OtterPredict's "
               + "Prediction, the Insight Engine's Group or any list with one entry per piece — numbers or "
               + "names — and the model comes out the way a truss bake does, with a layer per kind ready "
               + "for Layer Picker, a section per layer, and a schedule per group. Baking again onto the "
               + "same root adds to the layers that are there rather than making a second tree.\n\n"
               + "Nothing is baked until Bake goes on, and then once: wire a Button. The bake is one undo "
               + "step in Rhino.",
               Categories.Root, Categories.Document)
    {
    }

    public override Guid ComponentGuid => new("3d782e81-59d2-487e-8c0e-e6b8a3a6395e");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords => new[] { "bake", "layer", "group", "cluster", "colour", "user text" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("bakebygroup", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGeometryParameter("Geometry", "G",
            "The pieces to bake, one label each.",
            GH_ParamAccess.list);

        pManager.AddGenericParameter("Labels", "L",
            "One label per piece — a cluster number, a class name, anything with a text form. Pieces "
            + "sharing a label share a sub-layer.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Layer", "R",
            "The root layer the sub-layers go under. Made if missing; reused if there.",
            GH_ParamAccess.item, "OtterGroups");

        pManager.AddTextParameter("Key", "K",
            "The user-text key the label is written under on every object, so Read Attributes can read the "
            + "grouping back. Blank writes none.",
            GH_ParamAccess.item, "OtterGroup");

        pManager.AddBooleanParameter("Bake", "B",
            "Bake when this goes on. Wire a Button to bake once per press.",
            GH_ParamAccess.item, false);

        pManager[LayerInput].Optional = true;
        pManager[KeyInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Ids", "Id", "The ids of the objects baked, in the order the pieces came in.", GH_ParamAccess.list);
        pManager.AddTextParameter("Layers", "L", "The full path of each sub-layer used, one per label in the order the labels were first met.", GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "Rp", "What was baked, or what stopped it.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var items = new List<IGH_GeometricGoo>();
        var labels = new List<IGH_Goo>();
        if (!da.GetDataList(GeometryInput, items)) return;
        if (!da.GetDataList(LabelsInput, labels)) return;

        string root = "OtterGroups";
        string key = "OtterGroup";
        bool bake = false;
        da.GetData(LayerInput, ref root);
        da.GetData(KeyInput, ref key);
        da.GetData(BakeInput, ref bake);
        bool rising = bake && !_wasBaking;
        _wasBaking = bake;

        root = (root ?? string.Empty).Trim();
        key = (key ?? string.Empty).Trim();
        if (root.Length == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Layer needs a name for the root layer.");
            return;
        }

        if (labels.Count != items.Count)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"{items.Count} piece(s) but {labels.Count} label(s). Every piece needs one label, in the same order.");
            return;
        }

        var names = labels.Select(goo => GroupLayers.SafeName(goo?.ToString())).ToList();
        var distinct = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var paths = distinct.Select(name => root + LayerNode.Separator + name).ToList();

        if (!rising)
        {
            da.SetDataList(1, paths);
            da.SetDataList(2, new[] { $"Ready to bake {items.Count} piece(s) onto {distinct.Count} layer(s) under {root}. Turn Bake on to bake." });
            Message = $"{distinct.Count} groups\nready";
            return;
        }

        var doc = RhinoDoc.ActiveDoc;
        if (doc is null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No Rhino document is open to bake into.");
            return;
        }

        var ids = new List<string>(items.Count);
        int baked = 0, unreadable = 0, noLayer = 0;
        uint undo = doc.BeginUndoRecord("Bake by group");
        try
        {
            var layerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int g = 0; g < distinct.Count; g++)
                layerIndex[distinct[g]] = GroupLayers.Ensure(doc, paths[g], GroupPalette.Argb(g));

            for (int i = 0; i < items.Count; i++)
            {
                var geometry = items[i] is null ? null : GH_Convert.ToGeometryBase(items[i]);
                if (geometry is null)
                {
                    unreadable++;
                    ids.Add(string.Empty);
                    continue;
                }

                int layer = layerIndex[names[i]];
                if (layer < 0)
                {
                    noLayer++;
                    layer = doc.Layers.CurrentLayerIndex;
                }

                var attributes = new ObjectAttributes { LayerIndex = layer };
                if (key.Length > 0)
                    attributes.SetUserString(key, names[i]);

                Guid id = doc.Objects.Add(geometry, attributes);
                ids.Add(id == Guid.Empty ? string.Empty : id.ToString());
                if (id != Guid.Empty)
                    baked++;
            }
        }
        finally
        {
            doc.EndUndoRecord(undo);
        }

        doc.Views.Redraw();

        var report = new List<string> { $"Baked {baked} piece(s) onto {distinct.Count} layer(s) under {root}." };
        if (unreadable > 0)
        {
            report.Add($"{unreadable} piece(s) could not be read as geometry and were skipped.");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{unreadable} piece(s) could not be read as geometry and were skipped.");
        }
        if (noLayer > 0)
        {
            report.Add($"Rhino would not make a layer for {noLayer} piece(s), which went onto the current layer.");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Rhino would not make a layer for {noLayer} piece(s); they went onto the current layer.");
        }

        da.SetDataList(0, ids);
        da.SetDataList(1, paths);
        da.SetDataList(2, report);
        Message = $"{distinct.Count} groups\nbaked {baked}";
    }
}
