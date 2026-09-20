using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace OtterLogic.Grasshopper;

/// <summary>
/// The text counterpart of <see cref="TrainingData"/>: labels, names and targets,
/// which arrive as text because a class is a name and not a quantity.
/// <para>
/// The same rule holds as there, for the same reason. Position is what ties a label
/// to its sample, so anything that would shift a position — a null, a ragged branch
/// — is refused with its index rather than skipped.
/// </para>
/// </summary>
internal static class TextData
{
    /// <summary>A flat list of text, none of it missing.</summary>
    public static bool TryReadList(IReadOnlyList<string?> values, string what, out string[] text, out string? problem)
    {
        text = new string[values.Count];
        problem = null;

        for (int i = 0; i < values.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(values[i]))
            {
                problem = $"{what} is blank at position {i}. Every sample needs one, and skipping it would "
                    + "pair every later one with the wrong sample.";
                return false;
            }

            text[i] = values[i]!.Trim();
        }

        return true;
    }

    /// <summary>One branch per sample, every branch the same length, as a rectangular array.</summary>
    public static bool TryReadRows(
        GH_Structure<GH_String> tree, string what, out string[,] rows, out string? problem)
    {
        rows = new string[0, 0];
        problem = null;

        var branches = tree.Branches;
        if (branches.Count == 0)
        {
            problem = $"{what} is empty.";
            return false;
        }

        int columns = branches[0].Count;
        rows = new string[branches.Count, columns];

        for (int i = 0; i < branches.Count; i++)
        {
            if (branches[i].Count != columns)
            {
                problem = $"{what}: every branch must hold the same number of values. Branch 0 has {columns}, "
                    + $"branch {i} has {branches[i].Count}.";
                return false;
            }

            for (int j = 0; j < columns; j++)
            {
                string? value = branches[i][j]?.Value;
                if (string.IsNullOrWhiteSpace(value))
                {
                    problem = $"{what}: branch {i} is blank at position {j}.";
                    return false;
                }

                rows[i, j] = value.Trim();
            }
        }

        return true;
    }
}
