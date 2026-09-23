# Graph components: the layperson's cut

Decided 2026-09-23, the same day as the machine learning cut and for the same
person. Replaces eight raw algorithm components, each with its own inputs and
outputs, with one core and eight method components that output nothing but a
wire, and makes every builder work in three dimensions. This file records what
was decided and why, so the next person who finds a decision inconvenient knows
what it cost.

## Who this is for

The same Rhino user the machine learning cut was made for: an engineer, a
fabricator, a site team, someone routing a duct or tracing a way out of a
building. They will look up what Dijkstra or betweenness *is*. They will not
learn that A* needs a consistent heuristic, that potential flow wants a grounded
node in every piece, or that an undirected graph read as arcs is nothing but
cycles. The previous surface asked them to: eight components, each with a
different reading of the same graph, and a graph built in plan from a drawing
that was very often not flat.

## The shape

```
                  ┌───────────┐
   Graph ───────► │ OtterPath │ ──► Routes, Curves, Values, Connection Values, Groups, Marked, Report
   [Dijkstra] ──► │           │
   Sources ─────► │           │
   Targets ─────► │           │
                  └───────────┘
   Dijkstra, A*, Breadth-First, Potential Flow,
   Betweenness, Connected Pieces, Cut Vertices, Dependency Levels ──► one "Method" wire
```

One core takes a graph, a method on a wire, and the only two things a person
ever asks of a network: from where, and to where. A method component has no
graph input — only its one setting, if it has one — and outputs nothing but the
wire. Kangaroo's pattern, goals into a solver, with the two properties that
matter:

- **The zero-knowledge path is one wire.** A graph and nothing else gives the
  pieces it is in. Add sources and it is the cheapest route from them; add one
  target on a placed graph and it is A* where the weights allow. `AutoMethod`
  in Graphs reads the question off what was wired and says which method it
  chose and why, in Report.
- **Adding an algorithm never touches the core.** A spanning tree or a maximum
  flow is one record in the Graphs repo and one small component here.

## One core, not two

The eight algorithms do not answer in one shape. A route search has a tree of
routes; potential flow has a number per connection; cut vertices has a count
per node and a list of pairs; dependency levels has groups. The machine learning
cut had the same problem across six clustering results and solved it with a
common outcome — labels, a count, a confidence, notes — and this does the same.
`PathOutcome` reduces every answer to the five shapes an answer about a graph
can take: routes, a value per node, a value per connection, groups of nodes,
and nodes singled out. Each carries a name — Cost, Steps, Betweenness, Flow,
Ring, Piece, Unreached, Explored — and the Report says what each output holds
this time, so **Values** on the canvas reads as Cost when Dijkstra ran and as
Potential when Potential Flow did.

A second core for the structural questions was considered and rejected. It
would have mirrored a distinction the person writing the code cares about —
routes versus readings — and left a user choosing between cores before they
had chosen a method, which is exactly what the panel-per-paradigm split did to
clustering.

## Sources and targets

Every method reads the same two inputs and says what it makes of them, rather
than each component having its own. Dijkstra routes from the sources to the
targets. Potential Flow drains at the targets, and takes the sources as where
things enter — or every node, when none is wired, which is the usual question.
Betweenness, Connected Pieces and Cut Vertices score the whole graph and say in
a remark that the ends played no part; Connected Pieces uses them for the one
thing they are good for there, saying whether a source and a target share a
piece. A method that needs something it was not given — a source, a target,
positions, direction — says so in a sentence that names what to wire.

## In three dimensions

The previous builders read a drawing in plan and kept Z only for display, so a
scatter of points through a building routed as if it were one floor. Three
things changed:

- **Graph From Lines** is new. Line ends are welded into nodes at the document
  tolerance, on plain coordinates in Graphs (`LineNetwork`), and each
  connection weighs its curve's length. It is the way a duct run, a member
  layout or a street map is actually drawn, and it was the one thing the
  Graphs README listed as missing.
- **Graph From Points** measures its lengths in space and takes **Solids**:
  meshes and Breps, unpacked to triangles and answered by `SolidObstacles` in
  Graphs on the same terms as the planar obstacles — the surface is not inside,
  a step may run along a wall and touch an edge, a seam between two triangles
  of one flat face is not an edge. Closed curves stay as footprints, read in
  plan and blocking at every height, because that is what a plan drawing means.
- **A\*'s estimate** moved into the library. Whether the straight line is
  measured in three dimensions or in plan, and whether the weights can vouch
  for it at all, is arithmetic on the graph's weights and positions, and it now
  lives in `AStarMethod.ChooseMetric` where `AutoMethod` can ask it too.

**Visibility Graph** stays in plan and says so. The true shortest path round
solids in space turns at edges rather than corners and is a different, much
harder problem; through a building, scatter points and use Graph From Points.

## The panel

One **Graphs** panel, ordered by `GH_Exposure`:

| Tier | Components |
|---|---|
| primary, the core | OtterPath |
| secondary, methods | Dijkstra, A*, Breadth-First, Potential Flow, Betweenness, Connected Pieces, Cut Vertices, Dependency Levels |
| tertiary, build | Graph, Graph From Lines, Graph From Points, Visibility Graph, Graph From Connectivity, Deconstruct Graph |

## What was deleted, and why not hidden

The eight raw components — Dijkstra Shortest Path, A* Shortest Path,
Breadth-First Search, Potential Flow, Betweenness, Connected Pieces, Cut
Vertices, Dependency Levels — were deleted rather than hidden, for the reason
the machine learning cut gives: nothing has shipped, and a hidden component
still shows in the double-click search. Their algorithms are untouched in Graphs
and now reached through the method records. The Graph parameter and the four
surviving builders keep their `ComponentGuid`s.

## Below the adaptor line

| Repo | Added |
|---|---|
| Graphs | `Methods/`: `GraphMethod` and one record per algorithm, `AutoMethod`, `PathQuery` (the graph, n x 3 positions, sources, targets), `PathOutcome` as the common result, `PathRun.Solve` as the one entry point. `Spatial/`: `SolidObstacles`, `LineNetwork`. Warnings and remarks travel as two lists of strings, because Graphs references nothing, not even Core's `Note`. |
| Rhino3D | The Method wire and its parameter, OtterPath, eight method components, Graph From Lines, Solids on Graph From Points, three icons, the panel cut, and the deletions above. |

The seam is the one the skill already draws: a method record is a mechanism
and lives in the foundation repo; the component only unpacks a wire and packs
the answer back.

## Still open

- **Not yet opened in Grasshopper.** The wire, the zero-input method
  components and the Solids input are exercised by the build and the library
  tests, not yet on a canvas.
- **StructuralEngine's welder.** `StructureGraph` carries a private endpoint
  welder of the same shape as `LineNetwork`. The skill's rule says a second
  copy is the signal to move the first down; that move is a separate change,
  and it must leave no expected test value different.
