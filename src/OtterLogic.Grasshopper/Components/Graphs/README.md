# Graphs components

Questions asked of a network: what it is made of, which way is cheapest, how much
passes through here, what has to come first. Nothing is trained and nothing
clusters. Adaptors only — the algorithms live in the
[Graphs](https://github.com/Otter-Logic/Graphs) repo, `OtterLogic.Graphs`.

| Tier (`GH_Exposure`) | Component | Library call |
|---|---|---|
| `primary` — build | **Graph** (the parameter) | `GH_Graph`, in `Types/` |
| | **Graph From Points** — nearest neighbours, lengths as weights, obstacles left out | `NeighbourGraph.ByDistance` (MachineLearning) with `PlanarObstacles.Blocks` |
| | **Visibility Graph** — points and obstacle corners, joined where they see each other | `VisibilityGraph.Of` |
| | **Graph From Connectivity** | `WeightedGraph.FromEdges` |
| | **Deconstruct Graph** | `WeightedGraph.Edges` |
| `secondary` — structure | **Connected Pieces** | `WeightedGraph.ConnectedComponents` |
| | **Cut Vertices** — also bridges | `CutVertices.Of` |
| | **Dependency Levels** — also strongly connected components and a topological order | `Condensation.Of` |
| `tertiary` — routes and flow | **Dijkstra Shortest Path** | `Dijkstra.From` |
| | **A\* Shortest Path** — same route, far less of the graph searched | `AStar.Route` |
| | **Breadth-First Search** | `BreadthFirst.From` |
| | **Potential Flow** | `PotentialFlow.Solve` |
| `quarternary` — importance | **Betweenness** | `Centrality.Betweenness` |

The next route or flow method — a spanning tree, a maximum flow — goes in
`tertiary`. Tours and orderings, when they arrive, take `quinary`.

## Named so they can be found

A component is named for its algorithm where that is what somebody will type —
**Dijkstra**, **Breadth-First Search** — and for the question where it is not:
nobody searches for "low-link", they search for cut vertices or bridges. Either
way `Keywords` carries the other name, so "shortest path", "bfs", "tarjan",
"articulation points", "topological sort" and "scc" all land on the right
component from the canvas search.

## The Graph wire

One wire between components rather than a Connectivity tree, a Weights tree and
a list of points that all have to be kept in step. It carries a `WeightedGraph`
and, optionally, where the nodes are. The positions are an adaptor concern and
live in `PlacedGraph` beside the graph, not in it: Graphs references nothing and
knows nothing of geometry.

Positions are what let a result come back as geometry — Dijkstra's **Curves**,
Cut Vertices' **Bridge Lines**, Deconstruct Graph's **Lines** — and what make
the wire preview in the viewport.

**A connection has one weight, and the component reading it says what it
means.** Dijkstra reads a cost, so build the graph with lengths. Potential Flow
reads how readily a connection carries, so build it with one over length. The
clustering methods read a similarity from 0 to 1. Two readings of one network
are two graphs; that is cheaper than it sounds and much harder to get wrong than
one graph with a column per meaning.

**Per-connection outputs follow Deconstruct Graph's order** — each connection
once, lower node first, ascending. Potential Flow's **Flow** lines up with
Deconstruct Graph's **Lines** item for item.

**Node indices are how nodes are named.** To go from a point to its node, use
Closest Point against Deconstruct Graph's **Points**.

## Directed graphs

The wire carries either kind. **Graph From Connectivity** with **Directed** set
reads each branch one way — branch i lists only where i leads — which is how a
one-way street, a dependency or a flow with a direction is said; a two-way
street is then listed from both ends and may cost differently each way. Directed
weights may be zero or negative. A placed directed graph previews with arrows.

The two conversions are not alike, and the components keep the difference
visible. Undirected read as directed loses nothing, so **Dijkstra**, **A\*** and
**Breadth-First Search** take either without comment and follow arcs tail to
head. Directed read as undirected throws direction away, so **Connected
Pieces**, **Cut Vertices**, **Potential Flow** and **Betweenness** — all only
defined on an undirected graph — do it and say so in a remark. **Dependency
Levels** goes the other way and refuses an undirected graph: read as arcs each
way it is nothing but cycles, and every connected piece would fold into one
group at level zero — true, and useless. **Deconstruct Graph** lists a directed
graph's arcs by tail then head, which is arc-id order in the library.

## A* and what it steers by

A* needs to know where the target is, so it needs a graph with positions, and it
is one source to one target. The estimate is straight-line distance times
**Estimate Scale**, and that is only safe when no connection weighs less than
scale times its own length. The component checks the weights against the
positions before it runs: distance in 3D when every connection clears that bar,
distance in plan when only that does — which is the case for Graph From Points
and Visibility Graph, whose weights are lengths in plan — and a warning naming
the scale that would be safe when neither does. **Explored** lists what was
looked at, in order, which is the quickest way to see what A* is for.

## From a drawing

Two builders take points and, optionally, closed curves as **Obstacles**, and
hand back a graph already weighted by length and ready for Dijkstra. Both read in
plan — Z is ignored in the arithmetic and kept for drawing — and both let a route
touch an outline, so clearance is an Offset Curve before the obstacles go in.

- **Graph From Points** joins each point to its nearest few. A route follows the
  connections it is given, so this is for routes that *should* follow a grid, a
  scatter or a set of junctions. Node i is point i even when point i is inside an
  obstacle; it is left unconnected and listed under **Enclosed**, so indices
  picked from the user's own list stay good.
- **Visibility Graph** needs only the places routes start and end. Its routes are
  the true shortest ways through open space, straight where they can be and
  turning only at corners. The user's points are numbered first, so a start and
  an end are nodes 0 and 1.

The curves are flattened and unpacked in `ObstacleData`; what blocks what is
decided in Graphs on plain arrays.

## Trees still work, at the edges

The learning components speak Connectivity trees, because those line up branch
for branch with Training Inputs. **Graph From Connectivity** and **Deconstruct
Graph** are the seam: Proximity 3D's Links, Neighbour Graph's Connectivity and
Structural Insight Engine's connectivity all go in through one, and anything
built here goes back out to a clustering through the other.

## Why a panel of its own

Shortest Paths, Betweenness and Cut Vertices began under **Unsupervised
Learning**, because a clustering was the first thing to want them as features.
That is nowhere anybody else would look: ordering a toolpath, tracing a way out
of a building or sequencing an erection is not machine learning. The panel
follows the repo, which moved out from under MachineLearning for the same reason.
What builds a graph *from samples* — Neighbour Graph, Gaussian Affinity — stays
there, since measuring how alike two samples are is a learning question.

No `ComponentGuid` changed in the move or in the rewrite onto the Graph wire.
Shortest Paths became Dijkstra Shortest Path under the GUID it always had.

## What is not here yet

A way in from *lines*. Nothing turns a drawn network — centrelines, a street
map — into a graph by welding ends; today that is a topology plug-in or the
structural tools. The welding exists twice already inside StructuralDesign, so the next copy
should be the one both of those move onto rather than a third.

Grasshopper only. Wire-data tools with no document-level shape, so nothing here
gets a Rhino command or a toolbar button.
