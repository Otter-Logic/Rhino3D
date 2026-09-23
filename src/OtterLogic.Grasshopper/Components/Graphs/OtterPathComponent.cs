using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Graphs.Methods;
using OtterLogic.Grasshopper.Parameters.Graphs;

namespace OtterLogic.Grasshopper.Components.Graphs;

/// <summary>
/// A graph and a question in, an answer a person can act on out — with whatever
/// method is on the wire, or a chosen one when the wire is empty.
/// <para>
/// Adaptor only. Everything between a graph wire and a coloured model — which
/// method runs when none was chosen, what its answer means, whether it fits the
/// graph — belongs to <see cref="PathRun"/>. This unpacks the wire, reads the
/// method off its wire, makes one call, and packs the answer back as routes,
/// curves, a value per node, a value per connection, groups and marked nodes.
/// Adding an algorithm never touches this file: it is one record in Graphs and
/// one method component.
/// </para>
/// </summary>
public sealed class OtterPathComponent : GH_Component
{
    private const int GraphInput = 0;
    private const int MethodInput = 1;
    private const int SourcesInput = 2;
    private const int TargetsInput = 3;

    public OtterPathComponent()
        : base("OtterPath", "Path",
               "Ask a question of a graph: which way is cheapest, how much passes through here, what is it "
               + "made of, what holds it together, what has to come first.\n\n"
               + "Wire a Graph and nothing else for a first answer: the pieces it is in. Add Sources and "
               + "the answer is the cheapest route from them to every node; add one Target too and it is the "
               + "route between the two, found with A* where the graph allows. To choose the method yourself, "
               + "wire Dijkstra, A*, Breadth-First, Potential Flow, Betweenness, Connected Pieces, Cut "
               + "Vertices or Dependency Levels into Method — each says what it answers and what it needs.\n\n"
               + "Routes and Curves draw the answer; Values colours the nodes by it and Connection Values "
               + "the connections; Groups and Marked pick nodes out. Report says, in words, what ran and what "
               + "each output holds this time.",
               Categories.Root, Categories.Graphs)
    {
    }

    public override Guid ComponentGuid => new("0f3b8c1e-6d2a-4e7f-9b4c-2a5d8e1f7c93");

    // The core tier: the first thing a person reaches for, above every method.
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords
        => new[] { "shortest path", "route", "pathfinding", "path finding", "navigation", "flow", "network", "graph", "dijkstra", "astar" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("otterpath", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddParameter(new GraphParameter(), "Graph", "G", GraphWire.InputDescription, GH_ParamAccess.item);

        pManager.AddParameter(new GraphMethodParameter(), "Method", "M",
            "Wire Dijkstra, A*, Breadth-First, Potential Flow, Betweenness, Connected Pieces, Cut Vertices "
            + "or Dependency Levels. With nothing wired the question is read off Sources and Targets: none "
            + "gives the pieces of the graph, sources give the cheapest routes from them, one source and one "
            + "target on a placed graph gives A*. Report says which ran and why.",
            GH_ParamAccess.item);

        pManager.AddIntegerParameter("Sources", "S", GraphWire.SourcesDescription, GH_ParamAccess.list);
        pManager.AddIntegerParameter("Targets", "T", GraphWire.TargetsDescription, GH_ParamAccess.list);

        pManager[MethodInput].Optional = true;
        pManager[SourcesInput].Optional = true;
        pManager[TargetsInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddIntegerParameter("Routes", "R",
            "One branch per target — or per node, without targets — holding its route as node indices, "
            + "source first. Empty where none reaches it, and for a method that finds no routes.",
            GH_ParamAccess.tree);

        pManager.AddCurveParameter("Curves", "Cv",
            "The same routes as polylines, where the graph has positions. Null for a route of one node or none.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Values", "V",
            "Per node, the method's number for it — a route's Cost, Steps, Betweenness, Potential, Level, "
            + "Piece, Stranded. Null where the method has none for that node. Report says which it is. "
            + "Wire it into a gradient against Deconstruct Graph's Points.",
            GH_ParamAccess.list);

        pManager.AddNumberParameter("Connection Values", "CV",
            "Per connection, in Deconstruct Graph's order — Potential Flow's Flow, Cut Vertices' Bridge "
            + "flags. Empty for a method with no per-connection answer. Wire it into a line weight or a "
            + "colour against Deconstruct Graph's Lines.",
            GH_ParamAccess.list);

        pManager.AddIntegerParameter("Groups", "Gp",
            "Sets of nodes, one branch each — Breadth-First's rings, the pieces of the graph, its levels, "
            + "the two ends of each bridge. Empty for a method with none.",
            GH_ParamAccess.tree);

        pManager.AddIntegerParameter("Marked", "Mk",
            "Nodes the method singles out — the unreached, the cut vertices, the busiest, what A* looked "
            + "at. Report says what being marked means this time.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Report", "Rp",
            "The run in words: which method and why, what was reached, and what each output holds.",
            GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!GraphWire.TryGet(da, GraphInput, out var placed)) return;

        GraphMethod? method = GraphWire.ReadMethod(da, MethodInput);

        var sources = new List<int>();
        var targets = new List<int>();
        da.GetDataList(SourcesInput, sources);
        da.GetDataList(TargetsInput, targets);

        PathOutcome outcome;
        try
        {
            var query = GraphWire.ToQuery(placed, sources, targets);
            outcome = PathRun.Solve(query, method);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // ArgumentOutOfRangeException is an ArgumentException, so a source off the
            // graph or a setting the method refuses lands here too, with its message.
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            return;
        }

        foreach (string warning in outcome.Warnings)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
        foreach (string remark in outcome.Remarks)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, remark);

        var routes = new DataTree<int>();
        var curves = new List<GH_Curve?>();
        if (outcome.Routes is { } found)
        {
            for (int k = 0; k < found.Length; k++)
            {
                var path = new GH_Path(k);
                routes.EnsurePath(path);
                routes.AddRange(found[k], path);
                curves.Add(placed.Trace(found[k]) is { } line ? new GH_Curve(line.ToPolylineCurve()) : null);
            }
        }

        da.SetDataTree(0, routes);
        da.SetDataList(1, placed.Points is null ? new List<GH_Curve?>() : curves);
        da.SetDataList(2, (outcome.Values ?? Array.Empty<double>()).Select(v => double.IsNaN(v) ? null : new GH_Number(v)));
        da.SetDataList(3, outcome.ConnectionValues ?? Array.Empty<double>());
        da.SetDataTree(4, Trees.FromBuckets(outcome.Groups ?? Array.Empty<int[]>()));
        da.SetDataList(5, outcome.Marked ?? Array.Empty<int>());
        da.SetDataList(6, outcome.Report());

        Message = outcome.Method + (outcome.Rationale is null ? "" : "\nchosen") + (outcome.Routes is { } r ? $"\n{outcome.ReachedCount} of {r.Length} reached" : "");
    }
}
