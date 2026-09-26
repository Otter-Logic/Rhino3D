using Grasshopper;
using Grasshopper.Kernel.Data;
using OtterLogic.StructuralForm;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper;

/// <summary>Packing helpers shared by the clustering and structural form components.</summary>
internal static class Trees
{
    /// <summary>
    /// A lattice's nodes with one branch per row, so a node's place in the
    /// tree is its place in the grid. Positions with no node — a clipped
    /// grid's openings, the cells of an offset space truss with no pyramid —
    /// are left out of their row rather than filled with a null.
    /// </summary>
    public static DataTree<Point3d> ByRow(Lattice lattice)
    {
        var tree = new DataTree<Point3d>();

        for (int j = 0; j < lattice.CountV; j++)
        {
            var path = new GH_Path(j);
            for (int i = 0; i < lattice.CountU; i++)
                if (lattice.IsPresent(i, j))
                    tree.Add(lattice.Node(i, j), path);
        }

        return tree;
    }

    /// <summary>
    /// One branch per row of a list of rows, for results that come out of the
    /// domain already grouped: a grid's nodes per gridline, a radial grid's
    /// per ray. Rows may differ in length; the branch is whatever the row is.
    /// </summary>
    public static DataTree<T> FromRows<T>(IEnumerable<IReadOnlyList<T>> rows)
    {
        var tree = new DataTree<T>();
        int i = 0;

        foreach (IReadOnlyList<T> row in rows)
        {
            var path = new GH_Path(i++);
            foreach (T item in row)
                tree.Add(item, path);
        }

        return tree;
    }

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

    /// <summary>One branch per row of a rectangular array of indices.</summary>
    public static DataTree<int> FromRows(int[,] rows)
    {
        var tree = new DataTree<int>();
        int width = rows.GetLength(1);

        for (int i = 0; i < rows.GetLength(0); i++)
        {
            var path = new GH_Path(i);
            for (int j = 0; j < width; j++)
                tree.Add(rows[i, j], path);
        }

        return tree;
    }

    /// <summary>One branch per row of a rectangular array of text.</summary>
    public static DataTree<string> FromRows(string[,] rows)
    {
        var tree = new DataTree<string>();
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
