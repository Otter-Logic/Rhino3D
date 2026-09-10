using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.UnsupervisedLearning;

/// <summary>
/// The two propagation inputs shared by Message Passing Clustering and Refine
/// Labels — one wording, one default, one warning, so the two components cannot
/// describe the same setting differently.
/// <para>
/// The defaults are read from <see cref="PropagationOptions"/> itself rather than
/// repeated here, so the component and the library cannot disagree about them.
/// </para>
/// </summary>
internal static class HopsAndRetention
{
    /// <summary>
    /// Hops past which plain averaging, with no retention, has been measured to
    /// start erasing what it found — the library's planted-graph sweep held up to
    /// sixteen and had collapsed by thirty-two.
    /// </summary>
    private const int OversmoothingHops = 16;

    private static readonly PropagationOptions Defaults = new();

    /// <summary>
    /// The Hops input. Built here and added by the component, because
    /// Grasshopper's parameter manager is only reachable from inside one.
    /// </summary>
    public static IGH_Param Hops()
    {
        var hops = new Param_Integer
        {
            Name = "Hops",
            NickName = "H",
            Description = "Rounds of message passing — how many connections away a sample's values "
                + "can reach.\n\n"
                + "Two means a sample's neighbours and theirs. More reaches further and blurs more, "
                + "and with no Retention enough hops make everything connected look the same — on "
                + "the library's test graph that set in between sixteen and thirty-two.",
            Access = GH_ParamAccess.item,
        };

        hops.SetPersistentData(Defaults.Hops);
        return hops;
    }

    /// <summary>The Retention input, added straight after <see cref="Hops"/>.</summary>
    public static IGH_Param Retention()
    {
        var retention = new Param_Number
        {
            Name = "Retention",
            NickName = "A",
            Description = "Share of each sample's own original values restored after every round, "
                + "0 to 1.\n\n"
                + "0 is plain averaging. Raise it when running many hops, or when a sample's own "
                + "values must keep a say however much its neighbours disagree.",
            Access = GH_ParamAccess.item,
        };

        retention.SetPersistentData(Defaults.Retention);
        return retention;
    }

    /// <summary>Reads the two inputs registered at <paramref name="first"/> and the position after it.</summary>
    public static bool TryRead(IGH_DataAccess da, int first, out PropagationOptions options)
    {
        int hops = Defaults.Hops;
        double retention = Defaults.Retention;
        options = Defaults;

        if (!da.GetData(first, ref hops)) return false;
        if (!da.GetData(first + 1, ref retention)) return false;

        options = new PropagationOptions { Hops = hops, Retention = retention };
        return true;
    }

    /// <summary>Warns when the settings are in the range known to oversmooth.</summary>
    public static void Warn(GH_Component component, PropagationOptions options)
    {
        if (options.Hops > OversmoothingHops && options.Retention == 0.0)
            component.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"{options.Hops} hops with no Retention is past where repeated averaging has been "
                + "seen to make every connected sample look alike. If the groups come out blurred, "
                + "raise Retention to around 0.1.");
    }
}
