using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace OtterLogic.Grasshopper.Components.StructuralDesign;

/// <summary>
/// Reads the six force inputs every Structural Design component takes — one tree
/// per force, one branch per element holding its values — and says in the
/// component's own messages why a tree cannot be read.
/// <para>
/// Shared rather than written into each component because the shapes a user
/// wires in, and the mistakes they make wiring them, are the same whatever the
/// forces are then used for. The wording is part of that: a flattened tree gets the
/// same advice in every component.
/// </para>
/// </summary>
internal static class ForceInputs
{
    /// <summary>The six force inputs, in this order.</summary>
    public static readonly string[] Names = { "Fx", "Fy", "Fz", "Mx", "My", "Mz" };

    /// <summary>
    /// Reads input <paramref name="input"/> as one branch per element, each holding
    /// the element's values, or says why it cannot.
    /// <para>
    /// A single flat list with one value per element is taken as one combination,
    /// so a definition wired for one combination works unchanged. Anything else
    /// must have exactly one branch per element: branch position is what ties the
    /// values to an element. Every element must have the same combinations in
    /// every force; the library checks that and names what differs.
    /// </para>
    /// </summary>
    public static bool TryRead(
        GH_Component component, IGH_DataAccess da, int input, string force, int count, string element,
        out double[][] values)
    {
        values = Array.Empty<double[]>();

        if (!da.GetDataTree(input, out GH_Structure<GH_Number> tree))
            return false;

        var branches = tree.Branches;

        if (branches.Count == 1 && count > 1 && branches[0].Count == count)
        {
            if (!TryUnpack(component, branches[0], force, element, null, out var row))
                return false;

            values = row.Select(value => new[] { value }).ToArray();
            return true;
        }

        // A flat list holding a whole number of values per element is almost always
        // a tree that lost its branches on the way — two ends per bar, flattened.
        // Say how to put it back rather than suggest the wrong fix.
        if (branches.Count == 1 && count > 1 && branches[0].Count % count == 0)
        {
            int per = branches[0].Count / count;
            component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"{force} is one flat list of {branches[0].Count} values for {count} {element}(s) — {per} each, "
                + $"it looks like. It needs one branch per {element}: run it through Partition List with a "
                + $"size of {per}, or keep the branches it had before it was flattened.");
            return false;
        }

        if (branches.Count != count)
        {
            component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"{force} has {branches.Count} branch(es) but there are {count} {element}(s). It needs one "
                + $"branch per {element}, holding that {element}'s {force} values. If each branch holds "
                + "a combination instead, run it through Flip Matrix first.");
            return false;
        }

        values = new double[count][];
        for (int i = 0; i < count; i++)
        {
            if (branches[i].Count == 0)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"{force} of {element} {i} is empty. Every {element} needs the same values in "
                    + "every force — zero where there is none.");
                return false;
            }

            if (!TryUnpack(component, branches[i], force, element, i, out values[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The values of one list or branch, or an error naming the first null.
    /// <paramref name="index"/> is the element a branch belongs to, or null when
    /// the list holds one value per element.
    /// </summary>
    private static bool TryUnpack(
        GH_Component component, IList<GH_Number> items, string force, string element, int? index, out double[] values)
    {
        values = new double[items.Count];
        for (int k = 0; k < items.Count; k++)
        {
            if (items[k] is null)
            {
                string where = index is { } e
                    ? $"{force} of {element} {e}, value {k}"
                    : $"{force} of {element} {k}";

                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"{where} is missing. Give it a value — zero if there is none — or every value after it "
                    + "pairs with the wrong one.");
                return false;
            }

            values[k] = items[k].Value;
        }

        return true;
    }
}
