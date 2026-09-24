using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Core;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.Dataset;

/// <summary>
/// Any geometry in; the same row of numbers describing each piece out, so a bag
/// of curves, surfaces and meshes can be wired into anything that learns from a
/// table.
/// <para>
/// Adaptor only. Which measurements make the row, and how each kind of geometry
/// is measured, belong to <see cref="GeometryDescription"/> in Core; here a tree
/// of Grasshopper goo becomes RhinoCommon geometry and the rows come back as a
/// tree.
/// </para>
/// </summary>
public sealed class DescribeGeometryComponent : GH_Component
{
    private const int GeometryInput = 0;
    private const int PlaneInput = 1;
    private const int PositionInput = 2;

    public DescribeGeometryComponent()
        : base("Describe Geometry", "Describe",
               "Describe every piece of geometry by the same row of numbers — how big each way, how long, "
               + "how much area and volume, how it stands, how straight or flat, whether it closes, how "
               + "many corners, faces and holes — so that anything can be wired into OtterCluster, "
               + "OtterEmbed or OtterTrain.\n\n"
               + "Points, curves, surfaces, Breps, extrusions, SubDs and meshes all get the same columns, "
               + "with zero where a measure does not apply, and Kind says which each is. Position is off "
               + "by default so that two identical parts on opposite sides of a model read the same; turn "
               + "it on to group by place instead. Feature Names goes with the rows into the cores, and "
               + "Notes says what each column means for each kind.\n\n"
               + "For outlines that should be compared shape for shape rather than by a few measurements, "
               + "use Shape Signature.",
               Categories.Root, Categories.Dataset)
    {
    }

    public override Guid ComponentGuid => new("a1f52532-b1c8-471e-9015-c25585a8e89e");

    // Features from geometry: the last tier of the Dataset panel, beside Shape Signature.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords => new[] { "features", "measure", "geometry", "table", "describe", "properties" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("describegeometry", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddGeometryParameter("Geometry", "G",
            "The pieces to describe, one row each, in this order: points, curves, surfaces, Breps, "
            + "extrusions, SubDs or meshes, mixed as you like.",
            GH_ParamAccess.list);

        pManager.AddPlaneParameter("Plane", "P",
            "The frame sizes and positions are read in, and whose Z is up. World XY by default.",
            GH_ParamAccess.item, Plane.WorldXY);

        pManager.AddBooleanParameter("Position", "X",
            "Add each piece's centre as three more columns. Off by default: position tells identical "
            + "parts apart, which is what a grouping by kind does not want and a grouping by place does.",
            GH_ParamAccess.item, false);

        pManager[PlaneInput].Optional = true;
        pManager[PositionInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Features", "F",
            "One branch per piece, holding its numbers in the order Feature Names gives. This is what to "
            + "wire into OtterCluster's Data, OtterEmbed's Data or OtterTrain's Inputs.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Feature Names", "FN",
            "What each value of a branch is — wire it into the cores' Feature Names.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Kind", "K",
            "Per piece: point, curve, surface, solid or mesh.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Notes", "N",
            "What each column means, kind by kind.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var items = new List<IGH_GeometricGoo>();
        if (!da.GetDataList(GeometryInput, items))
            return;

        var plane = Plane.WorldXY;
        bool position = false;
        if (!da.GetData(PlaneInput, ref plane)) return;
        if (!da.GetData(PositionInput, ref position)) return;

        if (items.Count == 0)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wire the geometry to describe into Geometry.");
            return;
        }

        var features = new DataTree<double>();
        var kinds = new List<string>(items.Count);

        for (int i = 0; i < items.Count; i++)
        {
            var geometry = items[i] is null ? null : GH_Convert.ToGeometryBase(items[i]);
            if (geometry is null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Geometry {i} is missing or could not be read. Remove it, or every row after it shifts.");
                return;
            }

            double[]? row;
            try
            {
                row = GeometryDescription.Describe(geometry, plane, position);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Geometry {i}: {ex.Message}");
                return;
            }

            if (row is null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Geometry {i} is a {items[i].TypeName}, which this cannot describe. It reads points, curves, "
                    + "surfaces, Breps, extrusions, SubDs and meshes.");
                return;
            }

            features.AddRange(row, new GH_Path(i));
            kinds.Add(GeometryDescription.Kind(geometry) ?? "other");
        }

        da.SetDataTree(0, features);
        da.SetDataList(1, GeometryDescription.ColumnNames(position));
        da.SetDataList(2, kinds);
        da.SetDataList(3, GeometryDescription.ColumnNotes);

        Message = $"{items.Count} pieces\n{GeometryDescription.ColumnNames(position).Length} numbers each";
    }
}
