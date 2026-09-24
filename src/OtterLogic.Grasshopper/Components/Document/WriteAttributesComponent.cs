using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Document;
using Rhino;
using Rhino.DocObjects;

namespace OtterLogic.Grasshopper.Components.Document;

/// <summary>
/// Objects referenced from the Rhino document and values per object in; the
/// values written onto the objects as user text, under the keys given, when Write
/// goes on.
/// <para>
/// Adaptor only: the writing is <see cref="ObjectText"/> in Document. Writing
/// changes the document, so it happens on the rising edge of Write and not on
/// every re-solve — a toggle left on writes once, and a Button writes once per
/// press, which is what a person wiring either expects.
/// </para>
/// </summary>
public sealed class WriteAttributesComponent : GH_Component
{
    private const int GeometryInput = 0;
    private const int KeysInput = 1;
    private const int ValuesInput = 2;
    private const int WriteInput = 3;

    private bool _wasWriting;

    public WriteAttributesComponent()
        : base("Write Attributes", "WriteAttr",
               "Write a value per object onto Rhino objects as user text, under the keys you name — so a "
               + "cluster label, a predicted class or a measured number lives in the model, shows in "
               + "Rhino's properties panel, survives a save, and can be read back by Read Attributes in "
               + "any later definition.\n\n"
               + "Reference the objects from the document into Geometry. Keys names the columns; Values is "
               + "one branch per object with one item per key, or a flat list when there is one key. "
               + "Numbers are written as text. A blank removes the key from that object.\n\n"
               + "Nothing is written until Write goes on, and then once: wire a Button, or a toggle. The "
               + "change is one undo step in Rhino.",
               Categories.Root, Categories.Document)
    {
    }

    public override Guid ComponentGuid => new("cea7ab29-3d4a-47f0-8f93-cc0b5648ca03");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords => new[] { "user text", "attributes", "key", "value", "write", "label" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("writeattributes", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGeometryParameter("Geometry", "G",
            "Objects referenced from the Rhino document, one per row of Values.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Keys", "K",
            "The keys to write under, one per column of Values.",
            GH_ParamAccess.list);

        pManager.AddGenericParameter("Values", "V",
            "One branch per object, one item per key — or a flat list, one per object, when there is one "
            + "key. Text, numbers, anything with a text form.",
            GH_ParamAccess.tree);

        pManager.AddBooleanParameter("Write", "W",
            "Write when this goes on. Wire a Button to write once per press.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Written", "N", "How many objects were written to on the last write.", GH_ParamAccess.item);
        pManager.AddTextParameter("Report", "Rp", "What was written, or what stopped it.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var items = new List<IGH_GeometricGoo>();
        var keys = new List<string>();
        if (!da.GetDataList(GeometryInput, items)) return;
        if (!da.GetDataList(KeysInput, keys)) return;
        if (!da.GetDataTree(ValuesInput, out GH_Structure<IGH_Goo> values)) return;

        bool write = false;
        da.GetData(WriteInput, ref write);
        bool rising = write && !_wasWriting;
        _wasWriting = write;

        keys = keys.Select(k => (k ?? string.Empty).Trim()).ToList();
        if (keys.Count == 0 || keys.Any(k => k.Length == 0))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Every key needs a name.");
            return;
        }

        if (!TryRows(values, items.Count, keys.Count, out var rows, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return;
        }

        if (!rising)
        {
            da.SetData(0, 0);
            da.SetDataList(1, new[] { $"Ready to write {keys.Count} key(s) onto {items.Count} object(s). Turn Write on to write." });
            Message = "ready";
            return;
        }

        var doc = RhinoDoc.ActiveDoc;
        if (doc is null)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No Rhino document is open to write into.");
            return;
        }

        int written = 0, unreferenced = 0, refused = 0;
        uint undo = doc.BeginUndoRecord("Write attributes");
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                var goo = items[i];
                RhinoObject? obj = goo is { IsReferencedGeometry: true } ? doc.Objects.FindId(goo.ReferenceID) : null;
                if (obj is null)
                {
                    unreferenced++;
                    continue;
                }

                var pairs = keys.Select((key, j) => (key, rows[i][j]));
                if (ObjectText.Write(doc, obj, pairs))
                    written++;
                else
                    refused++;
            }
        }
        finally
        {
            doc.EndUndoRecord(undo);
        }

        var report = new List<string> { $"Wrote {keys.Count} key(s) onto {written} object(s)." };
        if (unreferenced > 0)
        {
            report.Add($"{unreferenced} item(s) are not objects in the document and were skipped.");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{unreferenced} item(s) are not objects in the document and were skipped. Reference geometry from Rhino "
                + "rather than making it on the canvas.");
        }
        if (refused > 0)
        {
            report.Add($"Rhino refused the change on {refused} object(s) — locked, or on a locked layer.");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Rhino refused the change on {refused} object(s).");
        }

        da.SetData(0, written);
        da.SetDataList(1, report);
        Message = $"wrote {written}";
    }

    /// <summary>
    /// The values as one row of text per object, however they were wired: one
    /// branch per object, or a flat list when there is one key. Refuses a shape
    /// that does not line up rather than guessing which value goes where.
    /// </summary>
    private static bool TryRows(GH_Structure<IGH_Goo> values, int objects, int keys, out string?[][] rows, out string? problem)
    {
        rows = Array.Empty<string?[]>();
        problem = null;
        var branches = values.Branches;

        static string? Text(IGH_Goo? goo) => goo is null ? null : goo.ToString();

        if (keys == 1 && branches.Count == 1 && branches[0].Count == objects)
        {
            rows = branches[0].Select(goo => new[] { Text(goo) }).ToArray();
            return true;
        }

        if (branches.Count != objects)
        {
            problem = $"Values has {branches.Count} branch(es) for {objects} object(s). Wire one branch per object, "
                + "holding one item per key" + (keys == 1 ? ", or a flat list with one item per object." : ".");
            return false;
        }

        rows = new string?[objects][];
        for (int i = 0; i < objects; i++)
        {
            if (branches[i].Count != keys)
            {
                problem = $"Branch {i} of Values holds {branches[i].Count} item(s) for {keys} key(s).";
                return false;
            }

            rows[i] = branches[i].Select(Text).ToArray();
        }

        return true;
    }
}
