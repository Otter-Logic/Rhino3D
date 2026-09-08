using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// Turns the Training Inputs tree every clustering component takes into the
/// rectangular array the library wants.
/// <para>
/// One branch per sample, holding that sample's values. It is the shape
/// LunchBoxML uses for the same job, which matters more than any argument about
/// whether it is the best one: a user who already has a definition wired for
/// those components can retarget it here without rebuilding the data.
/// </para>
/// </summary>
internal static class TrainingData
{
    /// <summary>The wording every component uses for this input, so they cannot drift.</summary>
    public const string Description =
        "One branch per sample, holding that sample's values in a consistent order.\n\n"
        + "Every branch must be the same length: branch position is what ties a result back to the "
        + "sample it came from.";

    /// <summary>
    /// Flattens the tree, or explains why it cannot.
    /// <para>
    /// Ragged and empty branches are refused rather than skipped. Skipping would
    /// shift every later sample's index, and the whole value of the output is
    /// that position i of the result is the sample at branch i of the input —
    /// breaking that silently produces a clustering that looks fine and colours
    /// the wrong things.
    /// </para>
    /// </summary>
    public static bool TryRead(GH_Structure<GH_Number> tree, out double[,] data, out string? problem)
    {
        data = new double[0, 0];
        problem = null;

        var branches = tree.Branches;

        if (branches.Count < 2)
        {
            problem = "Wire one branch per sample, each holding that sample's values. "
                + $"Got {branches.Count} branch(es) — a flat list cannot say where one sample ends "
                + "and the next begins.";
            return false;
        }

        int columns = branches[0].Count;
        if (columns == 0)
        {
            problem = "The first branch is empty.";
            return false;
        }

        for (int i = 0; i < branches.Count; i++)
        {
            if (branches[i].Count != columns)
            {
                problem = $"Every branch must hold the same number of values. Branch 0 has {columns}, "
                    + $"branch {i} has {branches[i].Count}.";
                return false;
            }
        }

        data = new double[branches.Count, columns];

        for (int i = 0; i < branches.Count; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                GH_Number? value = branches[i][j];
                if (value is null)
                {
                    problem = $"Branch {i} holds a null at position {j}.";
                    return false;
                }

                if (double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                {
                    problem = $"Branch {i} holds {value.Value} at position {j}. "
                        + "Clustering needs finite numbers.";
                    return false;
                }

                data[i, j] = value.Value;
            }
        }

        return true;
    }
}
