# Graphs components

Questions asked of a network: what it is made of, which way is cheapest, how much
passes through here, what has to come first. Nothing is trained and nothing
clusters. Adaptors only — the algorithms live in the
[Graphs](https://github.com/Otter-Logic/Graphs) repo, `OtterLogic.Graphs`.

Cut for a layperson on 2026-09-23, the way the Machine Learning panel was: one
core takes a graph and a method on a wire, and every algorithm is a small
component that outputs nothing but that wire. See
[docs/graph-components.md](../../../../docs/graph-components.md) for what was
decided and why.

| Tier (`GH_Exposure`) | Component | Library call |
|---|---|---|
| `primary` — the core | **OtterPath** — Graph, Method, Sources, Targets in; Routes, Curves, Values, Connection Values, Groups, Marked, Report out | `PathRun.Solve` |
| `secondary` — methods | **Dijkstra** | `DijkstraMethod` over `Dijkstra.From` |
| | **A\*** — one setting, Estimate Scale | `AStarMethod` over `AStar.Route` |
| | **Breadth-First** | `BreadthFirstMethod` over `BreadthFirst.From` |
| | **Potential Flow** — one setting, Injection | `PotentialFlowMethod` over `PotentialFlow.Solve` |
| | **Betweenness** — one setting, Maximum Sources | `BetweennessMethod` over `Centrality.Betweenness` |
| | **Connected Pieces** | `ConnectedPiecesMethod` over `WeightedGraph.ConnectedComponents` |
| | **Cut Vertices** — also bridges | `CutVerticesMethod` over `CutVertices.Of` |
| | **Dependency Levels** — also strongly connected components | `DependencyLevelsMethod` over `Condensation.Of` |
| `tertiary` — build | **Graph** (the parameter) | `GH_Graph`, in `Types/` |
| | **Graph From Lines** — line ends welded into nodes, lengths as weights, in 3D | `LineNetwork.Weld` |
| | **Graph From Points** — nearest neighbours in 3D, lengths as weights, obstacles and solids left out | `NeighbourGraph.ByDistance` (MachineLearning) with `PlanarObstacles.Blocks` and `SolidObstacles.Blocks` |
| | **Visibility Graph** — points and obstacle corners, joined where they see each other, in plan | `VisibilityGraph.Of` |
| | **Graph From Connectivity** | `WeightedGraph.FromEdges` |
| | **Deconstruct Graph** | `WeightedGraph.Edges` |

The Method wire is `GH_GraphMethod` over `OtterLogic.Graphs.Methods.GraphMethod`,
with `GraphMethodParameter` hidden from the ribbon. The next algorithm — a
spanning tree, a maximum flow — is one record in Graphs and one component under
`Methods/`, and OtterPath does not change.

## The zero-knowledge path

Wire a Graph into OtterPath and nothing else, and the Report says what the graph
is made of. Wire Sources and it is the cheapest route from them to every node.
Wire one Source and one Target on a graph with positions and it is A*, when the
weights let it be, and Dijkstra otherwise. `AutoMethod` in Graphs reads the
question off the query; the component never decides anything.

## Named so they can be found

A method component is named for its algorithm where that is what somebody will
type — **Dijkstra**, **Breadth-First** — and for the question where it is not:
nobody searches for "low-link", they search for cut vertices or bridges. Either
way `Keywords` carries the other name, so "shortest path", "bfs", "tarjan",
"articulation points", "topological sort" and "scc" all land on the right
component from the canvas search, and every one of them lands on OtterPath too.

## The Graph wire

One wire between components rather than a Connectivity tree, a Weights tree and
a list of points that all have to be kept in step. It carries a `WeightedGraph`
or a `DirectedGraph` and, optionally, where the nodes are. The positions are an
adaptor concern and live in `PlacedGraph` beside the graph, not in it: Graphs
references nothing and knows nothing of geometry. They cross into the library
once, as plain rows of x, y, z in `PathQuery`, in `GraphWire.ToQuery`.

Positions are what let a result come back as geometry — OtterPath's **Curves** —
and what make the wire preview in the viewport. They are also what A* steers by,
and the choice between three dimensions and plan is made in the library from the
graph's own weights.

**A connection has one weight, and the method reading it says what it means.**
Dijkstra reads a cost, so build the graph with lengths. Potential Flow reads how
readily a connection carries, so build it with one over length. Two readings of
one network are two graphs.

**Per-connection outputs follow Deconstruct Graph's order** — each connection
once, lower node first, ascending. OtterPath's **Connection Values** lines up with
Deconstruct Graph's **Lines** item for item, on a directed graph too: a method
only defined on an undirected one reads its answer back onto each arc.

**Node indices are how nodes are named.** To go from a point to its node, use
Closest Point against Deconstruct Graph's **Points**.

## Directed graphs

The wire carries either kind. **Graph From Connectivity** with **Directed** set
reads each branch one way, which is how a one-way street, a dependency or a flow
with a direction is said. Reading an undirected graph as directed loses nothing,
so **Dijkstra**, **A\*** and **Breadth-First** take either without comment and
follow arcs tail to head. Reading a directed one as undirected throws direction
away, so **Connected Pieces**, **Cut Vertices**, **Potential Flow** and
**Betweenness** do it and say so in a remark. **Dependency Levels** refuses an
undirected graph: read as arcs each way it is nothing but cycles.

## In three dimensions

Everything routes in space. **Graph From Lines** welds drawn line ends into nodes
at the document tolerance and weighs each connection by its curve's length, which
is the way a duct run, a member layout or a street map is drawn. **Graph From
Points** measures its lengths in three dimensions and takes two kinds of obstacle:
closed curves are footprints, read in plan and blocking at every height, and
**Solids** are meshes and Breps blocking where they are — a closed one has an
inside, an open one blocks what crosses it. **Visibility Graph** stays in plan,
because the true shortest path round solids in space turns at edges rather than
corners and is a different, much harder problem; through a building, scatter
points and use Graph From Points.

The curves and meshes are unpacked in `ObstacleData`; what blocks what is decided
in Graphs on plain arrays.

## Why a panel of its own

Shortest Paths, Betweenness and Cut Vertices began under **Unsupervised
Learning**, because a clustering was the first thing to want them as features.
That is nowhere anybody else would look: ordering a toolpath, tracing a way out
of a building or sequencing an erection is not machine learning. What builds a
graph *from samples* — Neighbour Graph, Gaussian Affinity — stays there, since
measuring how alike two samples are is a learning question.

The `ComponentGuid`s of the Graph parameter and the four builders that survived
the cut are unchanged. The eight raw algorithm components were deleted, not
hidden, for the reason the machine learning cut gives: nothing has shipped, and
a hidden component still shows in the double-click search.

Grasshopper only. Wire-data tools with no document-level shape, so nothing here
gets a Rhino command or a toolbar button.
