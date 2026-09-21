using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using OtterLogic.Graphs;
using OtterLogic.Grasshopper.Parameters.Graphs;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// How much passes along every connection of a graph when something enters at
/// some nodes and drains at others.
/// <para>
/// Adapter only. The solve belongs to <see cref="PotentialFlow.Solve"/>.
/// </para>
/// </summary>
public sealed class PotentialFlowComponent : GH_Component
{
    public PotentialFlowComponent()
        : base("Potential Flow", "Flow",
               "Find how much passes along every connection when something enters the graph at some "
               + "nodes and drains at others — current in a resistor network, heat in a conductor, "
               + "footfall towards the exits.\n\n"
               + "Dijkstra Shortest Path says which way is nearest and has to pick one; this says how much "
               + "goes each way and does not pick. Where two routes are as good as each other the "
               + "flow divides between them, so a symmetric graph gets a symmetric answer. Reach for "
               + "it when you want a route to draw, and for this when you want to know "
               + "what every connection carries.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("abc377e5-fc33-4b75-9ec1-046b86fd5b9c");

    // The routes and flow tier.
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override IEnumerable<string> Keywords
        => new[] { "laplacian", "resistance", "current", "kirchhoff", "random walk", "diffusion", "load path", "crowd" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("potentialflow", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G",
            "The graph to solve over. Each connection's weight is how readily it carries flow — a "
            + "heavier connection carries more for the same drop in potential. For a connection "
            + "that gets harder with length, build the graph with one over its length as the weight."
            + GraphWire.UndirectedNote,
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Injection", "I",
            "What enters at each node, one value per node in node order; negative draws off. A "
            + "single value is applied to every node — 1 for 'everything drains from everywhere'. "
            + "Ignored at grounded nodes, where whatever arrives leaves.",
            GH_ParamAccess.list, 1.0);

        pManager.AddIntegerParameter("Grounded", "G",
            "Nodes held at zero potential, where everything drains — the supports, the exits, the "
            + "plant room. At least one is needed in every connected piece of the graph; a piece "
            + "without one is reported as Unreached rather than solved.",
            GH_ParamAccess.list);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Potential", "P",
            "Per node, its potential: zero at a grounded node, higher the harder it is to drain "
            + "from. Null where no grounded node can be reached.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Flow", "F",
            "Per connection, in the order Deconstruct Graph lists them for an undirected graph: what passes from its "
            + "lower-numbered node to its higher, negative when it runs the other way. Zero where "
            + "either end is unreached. Its size is what a connection carries — wire it into a "
            + "line weight or a colour against Deconstruct Graph's Lines.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Unreached", "U",
            "Nodes in a piece of the graph with no grounded node, so nothing entering there has "
            + "anywhere to go.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!GraphWire.TryGetUndirected(this, da, 0, out _, out var graph)) return;

        var injection = new List<double>();
        if (!da.GetDataList(1, injection)) return;

        var grounded = new List<int>();
        if (!da.GetDataList(2, grounded)) return;

        int n = graph.NodeCount;

        if (injection.Count == 1 && n > 1)
            injection = Enumerable.Repeat(injection[0], n).ToList();

        try
        {
            var result = PotentialFlow.Solve(graph, injection, grounded);

            var potential = new List<GH_Number?>(n);
            var unreached = new List<int>();
            for (int i = 0; i < n; i++)
            {
                potential.Add(result.Reached[i] ? new GH_Number(result.Potential[i]) : null);
                if (!result.Reached[i])
                    unreached.Add(i);
            }

            var flow = graph.Edges().Select(e => result.Flow(e.A, e.B, e.Weight)).ToArray();

            if (unreached.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{unreached.Count} node(s) are in a piece of the graph with no grounded node. See Unreached.");

            if (!result.Converged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The solve stopped after {result.Iterations} iterations short of its tolerance, so "
                    + "the flows are approximate. Conductances spanning many orders of magnitude are "
                    + "the usual cause.");

            da.SetDataList(0, potential);
            da.SetDataList(1, flow);
            da.SetDataList(2, unreached);

            Message = $"{grounded.Distinct().Count()} grounded";
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
