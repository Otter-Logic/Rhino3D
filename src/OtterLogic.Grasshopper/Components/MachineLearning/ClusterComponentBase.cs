using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Clustering;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// The inputs both clustering components share, and the reading of them.
/// <para>
/// Two components asking for the same eight preprocessing settings is two places
/// for a default or a description to drift. Only the group-count inputs differ,
/// so those are the hook and everything else lives here.
/// </para>
/// </summary>
public abstract class ClusterComponentBase : GH_Component
{
    protected ClusterComponentBase(string name, string nickName, string description)
        : base(name, nickName, description, Categories.Root, Categories.MachineLearning)
    {
    }

    /// <summary>
    /// How many inputs the derived component adds between Data and the shared
    /// block — one for a fixed group count, two for a range.
    /// </summary>
    protected abstract int GroupParameterCount { get; }

    private int SharedStart => 1 + GroupParameterCount;

    protected sealed override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Data", "D",
            "One branch per member, holding that member's values in a consistent order — "
            + "typically six unsigned degree-of-freedom magnitudes, three forces then three "
            + "moments. Every branch must be the same length, because branch position is what "
            + "ties a result back to the member it came from.",
            GH_ParamAccess.tree);

        RegisterGroupParams(pManager);

        pManager.AddNumberParameter("Weights", "W",
            "One multiplier per column, applied after standardisation — so 1 means \"count this "
            + "the same as the others\", 2 means twice as much, and 0 drops the column outright. "
            + "Weighting works through the principal component rotation rather than through the "
            + "mixture, which is why it does nothing when PCA Variance is 0.",
            GH_ParamAccess.list);
        pManager[SharedStart].Optional = true;

        pManager.AddBooleanParameter("Log Transform", "L",
            "Apply log(1 + x) before anything else. Worth leaving on for magnitudes: a few members "
            + "carry most of the load and a long tail carries very little, which is not a shape a "
            + "Gaussian describes. Off, one group tends to swallow the tail while the rest split "
            + "hairs among the small values.",
            GH_ParamAccess.item, true);

        pManager.AddBooleanParameter("Normalise Rows", "N",
            "Scale each member to unit length first, discarding overall magnitude and keeping only "
            + "the proportion between degrees of freedom. Off groups members that could share one "
            + "physical detail; on groups members that want the same kind of detail whatever their "
            + "size. Usually paired with Log Transform off. This changes the answer more than any "
            + "other input here.",
            GH_ParamAccess.item, false);

        pManager.AddNumberParameter("PCA Variance", "V",
            "Fraction of variance the retained principal components must cover. At six columns this "
            + "is decorrelation and whitening rather than dimensionality reduction, and it is what "
            + "drops the directions a structure genuinely has no demand in. 0 skips it entirely.",
            GH_ParamAccess.item, 0.99);

        pManager.AddIntegerParameter("Covariance", "C",
            "Shape each group's spread may take. Wire a Covariance Type dropdown in, or right-click "
            + "for the same list. Diagonal suits whitened components; Full costs many more "
            + "parameters and wants a lot more members to justify them.",
            GH_ParamAccess.item, (int)CovarianceType.Diagonal);

        pManager.AddIntegerParameter("Restarts", "R",
            "How many times to refit from a different start, keeping the best. The fit climbs to a "
            + "local optimum and stops, so this is the cheapest accuracy available — ten costs "
            + "milliseconds and reliably beats one.",
            GH_ParamAccess.item, 10);

        pManager.AddIntegerParameter("Seed", "S",
            "Seeds the initialisation. Change it to sample a different set of starting points; leave "
            + "it fixed the rest of the time, because it is the only reason this returns the same "
            + "groups every time Grasshopper re-solves.",
            GH_ParamAccess.item, 1);

        var covariance = (Param_Integer)pManager[SharedStart + 4];
        foreach (var (label, value) in EnumChoices.Of<CovarianceType>())
            covariance.AddNamedValue(label, value);
    }

    /// <summary>Adds the inputs that say how many groups to look for.</summary>
    protected abstract void RegisterGroupParams(GH_InputParamManager pManager);

    /// <summary>
    /// Reads the data tree and the shared settings.
    /// <para>
    /// Returns false having already posted the reason, so a caller aborts on
    /// false and never has to decide how to report anything.
    /// </para>
    /// </summary>
    protected bool TryRead(IGH_DataAccess da, out double[,] data, out DesignGroupingOptions options)
    {
        data = new double[0, 0];
        options = new DesignGroupingOptions();

        if (!da.GetDataTree(0, out GH_Structure<GH_Number> tree))
            return false;

        if (!TryConvert(tree, out data, out string? problem))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, problem);
            return false;
        }

        var weights = new List<GH_Number>();
        da.GetDataList(SharedStart, weights);

        bool logTransform = true;
        bool normaliseRows = false;
        double pcaVariance = 0.99;
        int covariance = (int)CovarianceType.Diagonal;
        int restarts = 10;
        int seed = 1;

        if (!da.GetData(SharedStart + 1, ref logTransform)) return false;
        if (!da.GetData(SharedStart + 2, ref normaliseRows)) return false;
        if (!da.GetData(SharedStart + 3, ref pcaVariance)) return false;
        if (!da.GetData(SharedStart + 4, ref covariance)) return false;
        if (!da.GetData(SharedStart + 5, ref restarts)) return false;
        if (!da.GetData(SharedStart + 6, ref seed)) return false;

        if (!Enum.IsDefined(typeof(CovarianceType), covariance))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Covariance must be one of {string.Join(", ", EnumChoices.Of<CovarianceType>().Select(c => $"{c.Value} ({c.Label})"))}.");
            return false;
        }

        if (weights.Count > 0 && weights.Count != data.GetLength(1))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                $"Expected {data.GetLength(1)} weights to match the columns, got {weights.Count}.");
            return false;
        }

        if (weights.Any(w => w.Value < 0.0))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Weights cannot be negative.");
            return false;
        }

        options = new DesignGroupingOptions
        {
            LogTransform = logTransform,
            NormaliseRows = normaliseRows,
            Weights = weights.Count > 0 ? weights.Select(w => w.Value).ToArray() : null,
            PcaVariance = pcaVariance,
            Whiten = true,
            Covariance = (CovarianceType)covariance,
            Restarts = restarts,
            Seed = seed,
        };

        return true;
    }

    /// <summary>
    /// Flattens the tree into a rectangular array.
    /// <para>
    /// Ragged and empty branches are refused rather than skipped. Skipping would
    /// shift every later member's index, and the whole value of the result is
    /// that position i of the output is the member at branch i of the input —
    /// silently breaking that would produce a grouping that looks fine and
    /// colours the wrong members.
    /// </para>
    /// </summary>
    private static bool TryConvert(GH_Structure<GH_Number> tree, out double[,] data, out string? problem)
    {
        data = new double[0, 0];
        problem = null;

        var branches = tree.Branches;

        if (branches.Count < 2)
        {
            problem = "Wire one branch per member, each holding that member's values. "
                + $"Got {branches.Count} branch(es) — a flat list cannot say where one member ends "
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

                data[i, j] = value.Value;
            }
        }

        return true;
    }
}
