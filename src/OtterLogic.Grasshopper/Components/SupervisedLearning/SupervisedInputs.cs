using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using OtterLogic.Supervised;
using OtterLogic.Supervised.Neighbours;

namespace OtterLogic.Grasshopper.Components.SupervisedLearning;

/// <summary>
/// The inputs every supervised method shares, registered and read in one place so
/// that four components cannot describe the same wire four ways.
/// <para>
/// Training Inputs, the known answers, and Test Inputs always come first and in that
/// order, so a definition wired for one method can be retargeted at another by
/// dragging three wires across.
/// </para>
/// </summary>
internal static class SupervisedInputs
{
    public const string StandardiseDescription =
        "Bring every column to the same scale before fitting, using the training samples only.\n\n"
        + "Leave it on unless the columns already share a scale that means something. These methods add "
        + "columns together, so without it a length in millimetres beside an angle in radians is a model "
        + "that has only looked at the length.";

    public const string TestDescription =
        "The samples to predict, one branch each, with the same values in the same order as Training "
        + "Inputs.\n\n"
        + "To find out how good the method is, these should be samples whose answer you know but did not "
        + "train on — Split By Group makes that pair — and the prediction goes into an Evaluate component.";

    /// <summary>
    /// Training Inputs, then the known answers as classes, then Test Inputs. Built
    /// here and added by the component — Grasshopper's parameter manager is only
    /// reachable from inside one.
    /// </summary>
    public static IEnumerable<IGH_Param> ForClasses()
    {
        yield return TrainingInputs();
        yield return new Param_String
        {
            Name = "Training Labels", NickName = "L", Access = GH_ParamAccess.list,
            Description = "The known class of every training sample, in the same order. Any text will do; "
                + "numbers are read as names, not quantities.",
        };
        yield return TestInputs();
    }

    /// <summary>Training Inputs, then the known answers as numbers, then Test Inputs.</summary>
    public static IEnumerable<IGH_Param> ForValues()
    {
        yield return TrainingInputs();
        yield return new Param_Number
        {
            Name = "Training Values", NickName = "V", Access = GH_ParamAccess.list,
            Description = "The known value of every training sample, in the same order.",
        };
        yield return TestInputs();
    }

    private static Param_Number TrainingInputs() => new()
    {
        Name = "Training Inputs", NickName = "T", Access = GH_ParamAccess.tree,
        Description = TrainingData.Description,
    };

    private static Param_Number TestInputs() => new()
    {
        Name = "Test Inputs", NickName = "X", Access = GH_ParamAccess.tree,
        Description = TestDescription,
    };

    /// <summary>Reads inputs 0 and 2. Reports its own errors; false means stop.</summary>
    public static bool TryReadSamples(
        GH_Component component, IGH_DataAccess da, out double[,] train, out double[,] test)
    {
        train = test = new double[0, 0];

        if (!da.GetDataTree(0, out GH_Structure<GH_Number> trainTree)) return false;
        if (!da.GetDataTree(2, out GH_Structure<GH_Number> testTree)) return false;

        if (!TrainingData.TryRead(trainTree, out train, out string? problem))
        {
            component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Training Inputs: " + problem);
            return false;
        }

        // One test sample is a real request, where one training sample is a mistake.
        if (!TrainingData.TryRead(testTree, out test, out problem, minimumSamples: 1))
        {
            component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Test Inputs: " + problem);
            return false;
        }

        return true;
    }

    /// <summary>Reads input 1 as class names.</summary>
    public static bool TryReadLabels(GH_Component component, IGH_DataAccess da, out string[] labels)
    {
        labels = Array.Empty<string>();
        var raw = new List<string>();
        if (!da.GetDataList(1, raw)) return false;

        if (!TextData.TryReadList(raw, "Training Labels", out labels, out string? problem))
        {
            component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return false;
        }

        return true;
    }

    /// <summary>A classification needs something to choose between; says so once, the same way everywhere.</summary>
    public static bool RequireTwoClasses(GH_Component component, ClassLabels classes)
    {
        if (classes.Count >= 2)
            return true;

        component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
            $"Every training sample is '{classes.Classes[0]}', so there is nothing to tell apart. If that is "
            + "what this model really holds, the training set needs samples from a model where it is not.");
        return false;
    }

    /// <summary>Reads input 1 as numbers.</summary>
    public static bool TryReadValues(IGH_DataAccess da, out double[] values)
    {
        var raw = new List<double>();
        bool read = da.GetDataList(1, raw);
        values = raw.ToArray();
        return read;
    }

    /// <summary>The Weighting input, with its choices on the right-click menu.</summary>
    public static IGH_Param Weighting()
    {
        var weighting = new Param_Integer
        {
            Name = "Weighting", NickName = "W", Access = GH_ParamAccess.item,
            Description = "Whether nearer neighbours count for more. Uniform gives each an equal say; Distance "
                + "weights each by one over how far away it is, which helps where samples are unevenly spread.\n\n"
                + "Right-click for the list, or wire a Neighbour Weighting dropdown in.",
        };

        weighting.SetPersistentData((int)NeighbourWeighting.Uniform);
        foreach (var (label, value) in EnumChoices.Of<NeighbourWeighting>())
            weighting.AddNamedValue(label, value);

        return weighting;
    }

    /// <summary>Checks a wired integer is one of the weightings, and says which are if not.</summary>
    public static bool TryWeighting(GH_Component component, int value, out NeighbourWeighting weighting)
    {
        weighting = (NeighbourWeighting)value;
        if (Enum.IsDefined(typeof(NeighbourWeighting), value))
            return true;

        component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
            "Weighting must be one of "
            + string.Join(", ", EnumChoices.Of<NeighbourWeighting>().Select(c => $"{c.Value} ({c.Label})")) + ".");
        return false;
    }
}
