using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using OtterLogic.Fabrication;

namespace OtterLogic.Grasshopper.Components.Fabrication;

/// <summary>
/// Every joint of a line model, described by the same row of numbers — the level
/// below Connection Typology, for a user who wants to group joints their own way.
/// <para>
/// Adapter only. The description belongs to <see cref="JointSignature.Describe"/>.
/// </para>
/// </summary>
public sealed class JointSignatureComponent : GH_Component
{
    public JointSignatureComponent()
        : base("Joint Signature", "JointSig",
               "Find every joint of a line model and describe each by the same row of numbers: how many members "
               + "meet there and of which kind (plumb up or down, level, pitched up or down), the angles between "
               + "them, how they spread, which way they lean, and whether it is supported.\n\n"
               + "The row does not change when a joint is moved, turned in plan, mirrored, or drawn with its lines in "
               + "another order or split differently — so joints that are the same connection have the same row "
               + "wherever they are. Wire Signature into any Unsupervised Learning method as Training Inputs to "
               + "group joints your own way, or use Connection Typology for the finished grouping.",
               Categories.Root, Categories.Fabrication)
    {
    }

    public override Guid ComponentGuid => new("9918ed70-5f63-4452-9063-78802cf5f814");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("jointsignature", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        foreach (var parameter in JointInputs.Parameters())
            pManager.AddParameter(parameter);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddPointParameter("Joints", "J", "Every joint, in the order Signature describes them.", GH_ParamAccess.list);

        pManager.AddNumberParameter("Signature", "S",
            "One branch per joint, one value per feature — the Training Inputs shape every Unsupervised Learning "
            + "component takes. Scale it with Prepare Features first: its columns are counts, degrees and shares.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Feature Names", "N",
            "What each Signature column measures — wire into Group Signature's Feature Names.", GH_ParamAccess.list);

        pManager.AddIntegerParameter("Joint Lines", "JL",
            "One branch per joint: the line behind each member meeting there. A line passing through appears twice.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Handedness", "H",
            "Per joint: +1 or -1 for the two mirror images of an arrangement that has a hand, 0 for one that is its "
            + "own mirror image. Kept out of Signature so mirrored joints group together.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Orientation", "O",
            "Per line, in the order they came in: Level, Pitched or Plumb, read from the model's own spread of "
            + "inclinations.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var model = JointInputs.Read(this, da);
        if (model is null) return;

        try
        {
            var result = JointSignature.Describe(model.Starts, model.Ends, model.Supports, model.Attributes,
                model.AttributeNames, model.Options);

            foreach (string note in result.Notes)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);

            da.SetDataList(0, JointInputs.Points(result.Joints));
            da.SetDataTree(1, Trees.FromRows(result.Signature));
            da.SetDataList(2, result.FeatureNames);
            da.SetDataTree(3, Trees.FromBuckets(result.ArmLines));
            da.SetDataList(4, result.Handedness);
            da.SetDataList(5, result.Orientation.Select(o => o.ToString()));

            Message = $"{result.JointCount} joints\n{result.FeatureNames.Length} features";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
