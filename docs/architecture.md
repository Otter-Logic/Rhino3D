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

## Sections

The Grasshopper ribbon tab is `OtterLogic`; the panels within it are
subcategories, listed in `Categories` in `OtterLogicInfo.cs`:

- **Structural Form** — trusses, frames, discrete structural layouts.
- **Form Finding** — relaxation and equilibrium. Empty; to be designed.
- **Fabrication** — unrolling, nesting, toolpaths.
- **Learning** — dataset capture and inference.

Add sections there rather than typing category strings into components: the
Category string is literally what names the tab, so one typo silently creates a
second one.

Those constants live in `OtterLogic.Core.Sections`, and the Grasshopper
`Categories` class is now a thin alias over them. That is a deliberate exception
to keeping Core algorithm-only: the section names are domain vocabulary rather
than UI, and the Rhino panel reads the same list to build its headings. One copy
is the only thing stopping the ribbon tab and the panel drifting apart as tools
get added.

## The Rhino panel

`OtterLogic` opens a dockable Eto panel listing every tool. Rhino identifies
docked panels by **icon alone** — the caption is only a tooltip — so a panel
registered without one gets a blank tab nobody can find. `PanelIcon` draws a
small truss glyph at runtime rather than shipping a binary asset for the sake of
32 pixels, in a mid-tone accent colour so it reads against both the light and
dark themes.

The contents come from `ToolCatalog`, a flat list of
`(Section, Name, Command, Summary)` records. Adding a command to the panel is one
line; there is no layout code to touch. Descriptions are the reason to prefer a
panel over a toolbar — a button label alone will not tell you what a tool does
three months from now.

Registration happens in `OnLoad`, and the plug-in loads `AtStartup`. Both matter:
a panel registered lazily is one Rhino has already decided does not exist by the
time it restores the previous session layout.

Registration is wrapped in a try/catch that records the failure and returns
`Success` anyway. A panel that will not register is a nuisance; a plug-in that
refuses to load because of it takes every command down with it.

### The assembly Guid

Rhino takes a plug-in identity from the **assembly-level** `[Guid]`, not from the
`[Guid]` on the `PlugIn` class. Miss it and `PlugIn.Id` is `Guid.Empty`, which
fails in a thoroughly misleading way: commands still register and the tools all
work, so nothing looks wrong. But the plug-in never appears in the plug-in
manager, Rhino re-runs its package install on every startup because it can never
record the thing as installed, and anything keyed on the id fails outright —
`Panels.RegisterPanel` throws `plugInId Can't be Guid.Empty`.

`Properties/AssemblyInfo.cs` now carries it, and it must stay equal to the
attribute on `OtterLogicPlugIn`.

One build wrinkle worth knowing. The Rhino project has `UseWindowsForms` on for
`System.Drawing`, whose implicit usings collide with Eto over `Control`,
`Button`, `Font`, `Size` and `Padding`. Rather than alias every one of them in
every UI file, the csproj drops both implicit usings:

```xml
<Using Remove="System.Windows.Forms" />
<Using Remove="System.Drawing" />
```

Files that want `System.Drawing` import it themselves, which is only the icon and
the display conduits.

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

It is a poor trade for anything iterative. A relaxation or optimisation loop is
inherently stateful — current positions, velocities, iteration count, residual,
convergence — and forcing that into static methods means threading a large state
record through every call. It also rules out the shape such a thing wants: a
`Step()` the two front-ends drive differently, Grasshopper running it to
convergence while Rhino advances it a frame at a time and draws each one.

The line drawn in this repo:

- **Data crossing a boundary is immutable and behaviour-free.** Goals, materials,
  results, fabrication sheets. These serialise cleanly and wrap in `GH_Goo`
  without ceremony. BHoM-shaped.
- **Algorithms holding state are ordinary objects.** Solvers, meshers, nesters.
  Object-shaped.

If you later want BHoM's free-component trick, add a reflection-driven component
generator over Core's static entry points — `Truss2DGenerator.Generate` is
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
