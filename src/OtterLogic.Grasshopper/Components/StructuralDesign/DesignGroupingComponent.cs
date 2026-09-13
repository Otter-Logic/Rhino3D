using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.StructuralDesign;

namespace OtterLogic.Grasshopper.Components.StructuralDesign;

/// <summary>
/// Geometry and its six-degree-of-freedom forces in, the same geometry back out
/// sorted into design groups with one set of forces per element lined up beside it.
/// <para>
/// Adapter only, shared by the foundation and beam end plate components. The
/// grouping belongs to the library; everything here is pairing each piece of
/// geometry with its forces on the way in and bucketing both the same way on the
/// way out. What differs between the two components is declared rather than
/// written twice: the geometry, which forces come back out, and which library call
/// reads them — the last because which reading of the forces is right depends on
/// what is being designed, and that judgement lives in the library, not here.
/// </para>
/// <para>
/// Every force takes one branch per element, holding its values — each load
/// combination, and for a beam either or both ends. A flat list with one value per
/// element is one value each, so a definition wired for a single combination works
/// unchanged.
/// </para>
/// </summary>
public abstract class DesignGroupingComponent : GH_Component
{
    /// <summary>The six force inputs, in this order.</summary>
    protected static readonly string[] ForceNames = ForceInputs.Names;

    /// <summary>Input position of the first force; the other five follow it.</summary>
    private const int FirstForceInput = 1;

    /// <summary>Output position of the first force output; the rest follow it.</summary>
    private const int FirstForceOutput = 1;

    /// <summary>
    /// Input position of the first setting a component adds after the forces — see
    /// <see cref="RegisterSettings"/>.
    /// </summary>
    protected const int FirstSettingInput = FirstForceInput + 6;

    protected DesignGroupingComponent(string name, string nickname, string description)
        : base(name, nickname, description, Categories.Root, Categories.StructuralDesign)
    {
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    /// <summary>One force output: what it is called, and what it holds.</summary>
    protected readonly record struct ForceOutput(string Name, string NickName, string Description);

    /// <summary>
    /// What a grouping hands back: the element indices of each design group, one
    /// value per element for each of <see cref="ForceOutputs"/> in the same order,
    /// the report, the component's message, and what the user should see without
    /// opening the report — remarks for what is worth a look, warnings for input the
    /// library could not read the way it was meant.
    /// <para>
    /// Groups rather than a library result type, because the two components rest on
    /// different groupings — a behaviour classifier for foundations, an efficiency cut
    /// for end plates — and this class only ever needs the groups themselves.
    /// </para>
    /// </summary>
    protected readonly record struct Outcome(
        int[][] Groups, IReadOnlyList<double[]> Values, string Report, string Message,
        IReadOnlyList<string>? Remarks = null, IReadOnlyList<string>? Warnings = null);

    /// <summary>What one element is called in messages and the report — "node", "bar".</summary>
    protected abstract string ElementName { get; }

    /// <summary>The geometry input, one item per element, list access.</summary>
    protected abstract IGH_Param GeometryInput();

    /// <summary>The grouped geometry output, one branch per design group, tree access.</summary>
    protected abstract IGH_Param GeometryOutput();

    /// <summary>The description of force input <paramref name="force"/>.</summary>
    protected abstract string ForceInputDescription(string force);

    /// <summary>The force outputs, each one value per element, grouped like the geometry.</summary>
    protected abstract IReadOnlyList<ForceOutput> ForceOutputs { get; }

    /// <summary>
    /// Groups the elements from <c>forces[dof][element][value]</c>, reading any
    /// settings from <paramref name="da"/> at <see cref="FirstSettingInput"/> on.
    /// Null when a setting could not be read, which stops the solve.
    /// </summary>
    protected abstract Outcome? Group(double[][][] forces, IGH_DataAccess da);

    /// <summary>
    /// Adds any settings the component needs after the six forces. None by
    /// default; the first lands at <see cref="FirstSettingInput"/>.
    /// </summary>
    protected virtual void RegisterSettings(GH_InputParamManager pManager)
    {
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(GeometryInput());

        // One input per force rather than a branch of six per element. A branch
        // holds its values by position, so a branch of five or one in the wrong
        // order looks fine and puts forces under the wrong name; a wire into Fz
        // can only be Fz — which also leaves a branch free to hold combinations.
        foreach (string force in ForceNames)
            pManager.AddNumberParameter(force, force, ForceInputDescription(force), GH_ParamAccess.tree);

        RegisterSettings(pManager);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(GeometryOutput());

        // One output per force, each grouped exactly like the geometry, so a
        // value can be tagged at its element and pulled out of a group without a
        // List Item on the canvas.
        foreach (var output in ForceOutputs)
            pManager.AddNumberParameter(output.Name, output.NickName, output.Description, GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Group", "G",
            "The design group number of each element, grouped like the geometry: branch g holds g once "
            + "for each item of branch g — so it lines up with the geometry and Indices item for item.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Indices", "I",
            "One branch per design group, holding the original index of each element in it.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Report", "!",
            "The design groups and how they were found. Wire it to a panel to check the grouping "
            + "rather than take it on trust.",
            GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var geometry = new List<IGH_GeometricGoo>();
        if (!da.GetDataList(0, geometry))
            return;

        for (int i = 0; i < geometry.Count; i++)
        {
            if (geometry[i] is null || !geometry[i].IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"{Capitalised(ElementName)} {i} is missing or invalid. Every {ElementName} has to "
                    + "be there, or the forces after it pair with the wrong one.");
                return;
            }
        }

        var forces = new double[ForceNames.Length][][];
        for (int j = 0; j < ForceNames.Length; j++)
            if (!TryReadForce(da, j, geometry.Count, out forces[j]))
                return;

        try
        {
            if (Group(forces, da) is not { } outcome)
                return;

            var groups = outcome.Groups;
            var groupedGeometry = new DataTree<IGH_GeometricGoo>();
            var groupedValues = ForceOutputs.Select(_ => new DataTree<double>()).ToArray();
            var groupNumbers = new DataTree<int>();

            for (int g = 0; g < groups.Length; g++)
            {
                var path = new GH_Path(g);
                foreach (int element in groups[g])
                {
                    groupedGeometry.Add(geometry[element], path);
                    groupNumbers.Add(g, path);
                    for (int k = 0; k < groupedValues.Length; k++)
                        groupedValues[k].Add(outcome.Values[k][element], path);
                }
            }

            foreach (string remark in outcome.Remarks ?? Array.Empty<string>())
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, remark);
            foreach (string warning in outcome.Warnings ?? Array.Empty<string>())
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            int afterForces = FirstForceOutput + groupedValues.Length;

            da.SetDataTree(0, groupedGeometry);
            for (int k = 0; k < groupedValues.Length; k++)
                da.SetDataTree(FirstForceOutput + k, groupedValues[k]);
            da.SetDataTree(afterForces, groupNumbers);
            da.SetDataTree(afterForces + 1, Trees.FromBuckets(groups));
            da.SetData(afterForces + 2, outcome.Report);

            Message = outcome.Message;
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }

    /// <summary>Reads force input <paramref name="j"/> as one branch per element; see <see cref="ForceInputs.TryRead"/>.</summary>
    private bool TryReadForce(IGH_DataAccess da, int j, int count, out double[][] values)
        => ForceInputs.TryRead(this, da, FirstForceInput + j, ForceNames[j], count, ElementName, out values);

    protected static string Capitalised(string word)
        => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}
