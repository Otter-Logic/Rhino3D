using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Document;
using Rhino;
using Rhino.DocObjects;

namespace OtterLogic.Grasshopper.Components.Document;

/// <summary>
/// Objects referenced from the Rhino document in; the text kept on each of them
/// out, by key, with the layer and name beside it.
/// <para>
/// Adaptor only: the reading is <see cref="ObjectText"/> in Document. What is
/// here is the pairing of a Grasshopper reference with the document object it
/// points at, which is the one thing only an adaptor can do.
/// </para>
/// </summary>
public sealed class ReadAttributesComponent : GH_Component
{
    private const int GeometryInput = 0;
    private const int KeysInput = 1;

    public ReadAttributesComponent()
        : base("Read Attributes", "ReadAttr",
               "Read the text kept on Rhino objects — their user text — by key, along with each object's "
               + "layer and name.\n\n"
               + "Reference the objects from the document into Geometry. With Keys empty, every key any of "
               + "them carries comes back as a column; with Keys given, those columns in that order. A "
               + "blank is an object without that key. Values is one branch per object, the shape Data "
               + "Table shows and the cores take, so a label written with Write Attributes or Bake By "
               + "Group months ago is a training target today.\n\n"
               + "Geometry made on the canvas references nothing and comes back blank; only objects "
               + "picked from the document carry attributes.",
               Categories.Root, Categories.Document)
    {
    }

    public override Guid ComponentGuid => new("030aec89-5efa-4952-89e9-bf9fab1a05b4");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords => new[] { "user text", "attributes", "key", "value", "layer", "name" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("readattributes", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGeometryParameter("Geometry", "G",
            "Objects referenced from the Rhino document, one row each.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Keys", "K",
            "Optional. The keys to read, one column each in this order. Empty reads every key found.",
            GH_ParamAccess.list);

        pManager[KeysInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Values", "V",
            "One branch per object, one item per key in Keys' order; blank where the object has no such key.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Keys", "K", "The keys read, in column order.", GH_ParamAccess.list);

        pManager.AddTextParameter("Layer", "L", "Per object, the full path of its layer.", GH_ParamAccess.list);

        pManager.AddTextParameter("Name", "N", "Per object, its name; blank for none.", GH_ParamAccess.list);

        pManager.AddTextParameter("Id", "Id", "Per object, its id in the document.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var items = new List<IGH_GeometricGoo>();
        if (!da.GetDataList(GeometryInput, items))
            return;

        var wanted = new List<string>();
        da.GetDataList(KeysInput, wanted);
        wanted = wanted.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList();

        var doc = RhinoDoc.ActiveDoc;
        var objects = new RhinoObject?[items.Count];
        int unreferenced = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var goo = items[i];
            objects[i] = goo is { IsReferencedGeometry: true } && doc is not null ? doc.Objects.FindId(goo.ReferenceID) : null;
            if (objects[i] is null)
                unreferenced++;
        }

        if (unreferenced > 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"{unreferenced} of {items.Count} item(s) are not objects in the document and read blank. Reference "
                + "geometry from Rhino rather than making it on the canvas.");

        // Every key any object carries, in the order first met, so the columns are
        // the same for every row and do not depend on which object came first.
        var keys = wanted.Count > 0 ? wanted : new List<string>();
        if (wanted.Count == 0)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var obj in objects)
                if (obj is not null)
                    foreach (string key in ObjectText.Keys(obj))
                        if (seen.Add(key))
                            keys.Add(key);
        }

        var values = new DataTree<string>();
        var layers = new List<string>(items.Count);
        var names = new List<string>(items.Count);
        var ids = new List<string>(items.Count);

        for (int i = 0; i < items.Count; i++)
        {
            var path = new GH_Path(i);
            values.EnsurePath(path);
            var obj = objects[i];

            foreach (string key in keys)
                values.Add(obj is null ? string.Empty : ObjectText.Read(obj, key) ?? string.Empty, path);

            layers.Add(obj is null || doc is null ? string.Empty : doc.Layers[obj.Attributes.LayerIndex].FullPath);
            names.Add(obj?.Attributes.Name ?? string.Empty);
            ids.Add(obj?.Id.ToString() ?? string.Empty);
        }

        da.SetDataTree(0, values);
        da.SetDataList(1, keys);
        da.SetDataList(2, layers);
        da.SetDataList(3, names);
        da.SetDataList(4, ids);

        Message = $"{items.Count - unreferenced} objects\n{keys.Count} keys";
    }
}
