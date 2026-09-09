using Grasshopper;
using Grasshopper.Kernel.Data;

namespace OtterLogic.Grasshopper;

/// <summary>Packing helpers shared by the clustering components.</summary>
internal static class Trees
{
    /// <summary>One branch per row of a rectangular array.</summary>
    public static DataTree<double> FromRows(double[,] rows)
    {
        var tree = new DataTree<double>();
        int width = rows.GetLength(1);

        for (int i = 0; i < rows.GetLength(0); i++)
        {
            var path = new GH_Path(i);
            for (int j = 0; j < width; j++)
                tree.Add(rows[i, j], path);
        }

        return tree;
    }

    /// <summary>One branch per bucket.</summary>
    public static DataTree<int> FromBuckets(int[][] buckets)
    {
        var tree = new DataTree<int>();
        for (int c = 0; c < buckets.Length; c++)
            tree.AddRange(buckets[c], new GH_Path(c));

        return tree;
    }
}
