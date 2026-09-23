using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using OtterLogic.Fabrication;

namespace OtterLogic.Grasshopper.Components.Fabrication;

/// <summary>
/// A line model's lines in; the connection types its joints repeat, an exemplar
/// of each to detail, and the one-offs, out.
/// <para>
/// Adapter only. The types are decided by <see cref="ConnectionTypology.Classify"/>.
/// </para>
/// </summary>
public sealed class ConnectionTypologyComponent : GH_Component
{
    private const int MinimumTypeSizeInput = JointInputs.Count;

    public ConnectionTypologyComponent()
        : base("Connection Typology", "ConnTypes",
               "Find the connection types a model actually has, from its lines alone: every joint described the same "
               + "way, joints alike grouped into types, each type given an exemplar joint to detail and a description "
               + "of what sets it apart — and every joint alike to no other listed as a one-off.\n\n"
               + "Nothing is assumed about what connections exist: no catalogue of corners, splices or bases. A type "
               + "is whatever the model repeats, whatever its grid, skew or shape; naming it is yours. One-offs are "
               + "often modelling errors — a column that misses the column above, a beam off its level — and near-"
               + "identical variants within a type are where a detail could be rationalised.\n\n"
               + "For your own grouping, wire Joint Signature into OtterCluster under Machine Learning.",
               Categories.Root, Categories.Fabrication)
    {
    }

    public override Guid ComponentGuid => new("717bdc00-830f-450f-b822-45e6d137da2a");

    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override Bitmap? Icon => EmbeddedIcons.Load("connectiontypology", 24);

    private static readonly ConnectionTypologyOptions Defaults = new();

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        foreach (var parameter in JointInputs.Parameters())
            pManager.AddParameter(parameter);

        pManager.AddIntegerParameter("Minimum Type Size", "M",
            "Fewest joints a type needs. Two by default — a type is a connection made more than once; a joint alike "
            + "to no other is a one-off. Raise it to see only the types that repeat enough to matter.",
            GH_ParamAccess.item, Defaults.MinimumTypeSize);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Summary", "S",
            "Every type — its size, exemplar, members in words, and what sets it apart — then the one-offs. Read it "
            + "in a panel.",
            GH_ParamAccess.item);

        pManager.AddTextParameter("Types", "T",
            "One name per group: each connection type, largest first, with its members in words, then the one-offs "
            + "as a last group. Item k here is branch {k} of every tree output.",
            GH_ParamAccess.list);

        pManager.AddPointParameter("Joints", "J",
            "One branch per group: the joints of that type. A slider into the Path of a Tree Branch steps through "
            + "the types.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Joint Lines", "JL",
            "One branch per joint, under its group — {type;joint}: the lines meeting at that joint, as Joint "
            + "Signature gives them. A line passing through a joint appears twice.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Joint Index", "JI",
            "One branch per group: each joint's index in Joint Signature's order, to line the two up.",
            GH_ParamAccess.tree);

        pManager.AddPointParameter("Exemplars", "E",
            "One point per group: the most typical joint of each type — the one to detail on behalf of the rest. "
            + "Empty for the one-offs.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Handedness", "H",
            "One branch per group, a value per joint: +1 or -1 for mirror-image versions of the type, 0 for a joint "
            + "that is its own mirror image.",
            GH_ParamAccess.tree);

        pManager.AddNumberParameter("Variation", "V",
            "One branch per group, a value per joint: how far it sits from its type's exemplar, in standard "
            + "deviations. Zero is identical; small but above zero is a near-identical variant. Empty for one-offs.",
            GH_ParamAccess.tree);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var model = JointInputs.Read(this, da);
        if (model is null) return;

        int minimumTypeSize = Defaults.MinimumTypeSize;
        if (!da.GetData(MinimumTypeSizeInput, ref minimumTypeSize)) return;

        try
        {
            var result = ConnectionTypology.Classify(model.Starts, model.Ends, model.Supports, model.Attributes,
                model.AttributeNames, new ConnectionTypologyOptions { Signature = model.Options, MinimumTypeSize = minimumTypeSize });

            foreach (string note in result.Notes)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);

            var joints = JointInputs.Points(result.Signatures.Joints);
            var groups = result.Groups();
            int oneOffs = result.OneOffs.Length;

            if (oneOffs > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"{oneOffs} joint(s) are alike to no other — the last group. Many one-offs are modelling errors.");

            // Every tree is one branch per group, so branch {k} of each is item k of
            // Types; Joint Lines goes one level deeper, a branch per joint within it.
            var groupJoints = new DataTree<Point3d>();
            var jointLines = new DataTree<int>();
            var jointIndex = new DataTree<int>();
            var handedness = new DataTree<int>();
            var variation = new DataTree<double>();

            for (int g = 0; g < groups.Length; g++)
            {
                var path = new GH_Path(g);
                bool isType = g < result.Types.Count;

                for (int k = 0; k < groups[g].Length; k++)
                {
                    int joint = groups[g][k];
                    groupJoints.Add(joints[joint], path);
                    jointIndex.Add(joint, path);
                    handedness.Add(result.Signatures.Handedness[joint], path);
                    jointLines.AddRange(result.Signatures.ArmLines[joint], new GH_Path(g, k));
                    if (isType)
                        variation.Add(result.DistanceFromExemplar[joint], path);
                }

                variation.EnsurePath(path);
            }

            da.SetData(0, result.Summary);
            da.SetDataList(1, result.GroupNames());
            da.SetDataTree(2, groupJoints);
            da.SetDataTree(3, jointLines);
            da.SetDataTree(4, jointIndex);
            da.SetDataList(5, groups.Select((_, g) => g < result.Types.Count ? new GH_Point(joints[result.Types[g].Exemplar]) : null));
            da.SetDataTree(6, handedness);
            da.SetDataTree(7, variation);

            Message = $"{result.Types.Count} types\n{oneOffs} one-offs";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
