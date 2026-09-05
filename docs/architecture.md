# Architecture

## The one rule

`OtterLogic.Core` contains every algorithm. `OtterLogic.Rhino` and
`OtterLogic.Grasshopper` are adapters: they gather input, call Core, and present
output. If you are writing geometry logic in a `SolveInstance` or a `RunCommand`,
it is in the wrong file.

```
              OtterLogic.Core
        (RhinoCommon only, no UI)
                 ↑        ↑
    OtterLogic.Rhino    OtterLogic.Grasshopper
        (.rhp)                 (.gha)
```

Core references **RhinoCommon and nothing else** — not `Grasshopper.dll`, not
`Rhino.UI`, not Eto. That single constraint is what keeps the two front-ends
from drifting apart, and it is enforced by the fact that Core simply cannot see
those assemblies.

Using RhinoCommon types (`Point3d`, `Mesh`) inside Core rather than inventing a
neutral geometry layer is a deliberate trade. It costs portability outside Rhino;
it saves an entire conversion layer and every bug that lives in one.

## Why solvers are objects, not functions

`IRelaxationSolver` exposes `Step()`, not `Solve()`:

```csharp
public interface IRelaxationSolver
{
    IReadOnlyList<Point3d> Positions { get; }
    double Residual { get; }
    bool HasConverged { get; }
    void Step(int iterations = 1);
}
```

Because the two hosts want different things from the same computation:

| | Grasshopper | Rhino |
|---|---|---|
| Drives it | `Step(1000)` inside `SolveInstance` | `Step(10)` per frame from the message loop |
| Feedback | final mesh on the wire | `DisplayConduit`, redrawn every frame |
| Cancellation | none needed | `RhinoApp.EscapeKeyPressed` |

A `Solve()` that returns an answer would force the Rhino side to reimplement the
loop — and the two would diverge the first time a solver gained a parameter.

## Goals: data in, opinion out

Relaxation uses the projective / position-based formulation. A goal never applies
a force; asked where its particles *would like* to be, it writes a target and a
weight. The solver takes the weighted mean per particle and integrates the move
into a damped velocity.

```csharp
public interface IGoal
{
    int[] Indices { get; }
    void Calculate(IReadOnlyList<Point3d> positions, Point3d[] targets, double[] weights);
}
```

This is the ShapeOp/Kangaroo approach, and it buys three things: stability at
large step sizes, stiff and soft constraints mixing without timestep tuning, and
a plugin surface where new behaviour means one small class and nothing else.

Adding a goal — planarity, collision, angle, developability — means implementing
that interface. The solver, the Rhino command and the Grasshopper component all
pick it up with no changes.

## Sections

The Grasshopper ribbon tab is `OtterLogic`; the panels within it are
subcategories, listed in `Categories` in `OtterLogicInfo.cs`:

- **Structural Form** — trusses, frames, discrete structural layouts.
- **Form Finding** — relaxation and equilibrium.
- **Fabrication** — unrolling, nesting, toolpaths.
- **Learning** — dataset capture and inference.

Add sections there rather than typing category strings into components: the
Category string is literally what names the tab, so one typo silently creates a
second one.

## Truss stations

`Truss2DGenerator` places nodes at *stations* — normalised arc-length positions
from 0 to 1 along a chord. Each chord carries **its own** station list, always
the same length as the other, so top node `i` still pairs with bottom node `i`
and every web pattern stays index arithmetic over panel count — but the two
chords are free to put that node at different points along their own length.

There are two ways those lists get built, and the distinction is the important
part of the design:

**Divisions drive, snap points steer.** With `Divisions` (or `SnapSpacing`) set,
the panel count is fixed up front, both chords are laid out evenly, and then each
is snapped **independently** onto its own snap points. A chord owns its vertices
and kinks, plus the picked points lying nearer to it than to the other chord — so
a point beside the bottom chord moves the bottom node and leaves the top one
where it was.

Member count is exactly what was asked for; snap points move members but never
add them. Reach is half a panel — far enough to catch a nearby vertex, never far
enough for two stations to swap places or collapse together. Assignment is
greedy, nearest pair first, with both sides claimed exclusively, because
otherwise two stations converge on one popular point and the panels either side
degenerate.

The snapped lists are deliberately *not* de-duplicated afterwards. Merging a
close pair on one chord but not the other would leave the lists different lengths
and break the pairing; the snap radius already guarantees stations stay ordered
and apart.

**Geometry drives.** With neither set, the snap points *are* the stations:
polyline vertices, curve kinks and picked points each become a node. Here the two
chords must share one list — the points are what decide how many panels there
are, so the chords have to agree on that — which means a vertex on either chord
induces a node on both. A plain line contributes none, so two lines give a single
panel. That is deliberate: the alternative is inventing a panel count the user
did not ask for.

Where the chords converge to a shared point, `ChordsMeetAtStart` /
`ChordsMeetAtEnd` suppress that end post. It would otherwise collapse onto the
shared point and clash with the chords running into it.

`Flip` mirrors each diagonal within its own panel, implemented by swapping which
way the two web helpers run rather than by branching per pattern. Pratt flipped
is Howe; cross-braced draws both diagonals already, so it comes out identical.

## Where the front-ends legitimately differ

Adapters are thin, but thin is not the same as identical. The one place they
diverge on purpose: the Rhino command bakes **only web and end posts**, because
the chords are curves the user drew and then picked — adding the generated
copies would leave two curves on top of each other. The Grasshopper component
outputs the chord members, since on a canvas they are the only chords there are.

The filtering lives in the command, not the engine. `Truss2DGenerator` always
produces the full member list; deciding what to do with it is exactly the kind
of host-specific judgement an adapter is for.

## Where BHoM-style layering fits, and where it does not

The layering discipline is worth stealing wholesale: a data layer, a logic layer,
adapters, then UI wrappers, each only depending downward. That is exactly the
split above.

The part not worth copying here is BHoM's rule that logic lives in *static
methods over inert data objects*. That design exists to serve reflection-driven
UI generation — one static method becomes one Grasshopper component for free,
across hundreds of methods and many disciplines. It is a great trade at that
scale.

It is a poor trade for iterative solvers. Dynamic relaxation is inherently
stateful: positions, velocities, iteration count, residual, convergence. Forcing
it into static methods means threading a large state record through every call,
and the `Step()` design above becomes impossible to express cleanly.

The line drawn in this repo:

- **Data crossing a boundary is immutable and behaviour-free.** Goals, materials,
  results, fabrication sheets. These serialise cleanly and wrap in `GH_Goo`
  without ceremony. BHoM-shaped.
- **Algorithms holding state are ordinary objects.** Solvers, meshers, nesters.
  Object-shaped.

If you later want BHoM's free-component trick, add a reflection-driven component
generator over Core's static entry points — `MeshRelaxation.CreateSolver` is
already shaped for it. Build that when you have thirty methods, not three.

## Naming trap

Inside `namespace OtterLogic.Rhino`, a bare `Rhino.Something` binds to *this*
namespace, not McNeel's. Same for `Grasshopper.Something` inside
`OtterLogic.Grasshopper`. Import types with `using` directives above the
namespace declaration and reference them unqualified, or write `global::Rhino.X`.

## Versions

Pinned to RhinoCommon/Grasshopper **8.34.26223.11001**, matching the installed
Rhino. Building against a lower 8.x baseline would run on more machines; matching
the installed version keeps IntelliSense honest. Change `RhinoVersion` in
`Directory.Build.props` to move it.

Target framework is `net7.0-windows` — Rhino 8's runtime. The SDK here is .NET 10,
which builds net7.0 fine; `CheckEolTargetFramework` is off to silence the nag.
Only Rhino ships a .NET 7 runtime, so the test project sets
`RollForward=LatestMajor` to run outside Rhino.
