# Architecture

## The one rule

**Adaptor → Domain → Core. Never backwards, and domains never reference each
other.** A domain that needs another domain is the signal that something belongs
in Core — not that the two should be coupled.

```
                    OtterLogic.Core
              small, stable, slow-moving
                          ↑
              OtterLogic.StructuralForm          (+ Fabrication, FormFinding, ...)
              types and logic for one domain
                    ↑              ↑
        OtterLogic.Rhino    OtterLogic.Grasshopper
             (.rhp)                (.gha)
```

Algorithms live in a **domain**, never in an adaptor. If you are writing geometry
logic in a `SolveInstance` or a `RunCommand`, it is in the wrong file.

No project below the adaptor line may reference `Grasshopper.dll`, `Rhino.UI` or
Eto. That constraint is what keeps the two front-ends from drifting apart, and it
is enforced by the fact that Core and the domains simply cannot see those
assemblies.

### Why a domain owns its types as well as its logic

The BHoM-shaped alternative is an object model repo holding every discipline's
data, with the logic in a separate engine layer. That split exists to serve
reflection-driven component generation across hundreds of methods and many
disciplines, and it forces Core to grow a section per discipline.

Here, `TrussType`, `Truss2DOptions`, `Truss2D` and `Truss2DGenerator` live
together, because they change together — every one of them changed in the same
sitting, repeatedly. Splitting them across a boundary would mean a two-step
release dance to add a field to an options record.

The useful half of the BHoM discipline survives *inside* each domain: immutable,
behaviour-free records for anything crossing a wire; ordinary objects for logic
that holds state. Namespaces express that perfectly well; it does not need a
project boundary. And if the reflection-driven component generator ever gets
built, that separation is what it keys off.

### What Core is for

Core holds what **more than one domain** needs, and nothing else. Today that is
`Sections` — the vocabulary the ribbon tab and the Rhino panel both read — plus
shared geometry helpers and tolerance conventions.

One test keeps it honest: *would a second domain plausibly need this?* If no, it
belongs in the domain. The failure mode to avoid is not drift, it is Core
becoming a grab-bag, or a bottleneck where every domain change needs a Core
release first.

### Why RhinoCommon rather than neutral geometry

Using `Point3d`, `Curve` and `Mesh` throughout, rather than a neutral geometry
layer with converters, is a deliberate trade. BHoM must abstract geometry because
it targets Revit, ETABS and Tekla, where RhinoCommon does not exist. Every
context this code runs in is a Rhino host, tests included.

The cost of abstracting would be concrete: `Truss2DGenerator` alone leans on
arc-length parameterisation, curve subdomain length, closest-point, continuity
analysis and least-squares plane fitting. A neutral `ICurve` supplies none of
that, so the choice would be reimplementing a numerical curve library or
converting to Rhino to compute and converting back.

One consequence worth knowing, because it is a design lever: RhinoCommon
**structs** — `Point3d`, `Vector3d`, `Line`, `Plane` — are pure managed code and
work with no Rhino running. The **classes** — `Curve`, `Mesh`, `Brep` — are
native-backed and need Rhino booted. Keeping algorithm inner loops on structs and
arrays, with curves and meshes confined to the entry and exit, is what makes a
test runnable without Rhino.

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

#### The toolbar

`UI/OtterLogic.rui` ships beside the `.rhp` with a matching base name, which is
how Rhino finds a plug-in toolbar. It auto-loads from the package folder, no
install step.

Three things learned the hard way, all verified against a running Rhino:

- **You cannot add a tab to Rhino's own strip.** Every visible tab — Standard,
  CPlanes, and Grasshopper too — is defined in Rhino's `default.rui`. Pointing
  our group's `dock_bar_guid64` at that bar does not add to it, it *takes it
  over*: Rhino's own toolbars disappear. Ours keeps its own dock bar.
- **A group without `<dock_bar_info>` loads but is never shown.** It needs
  `visible="True"`, and `floating="True"` so it appears somewhere the user can
  find it. From there, dragging it into the tab strip is user window-layout
  state that Rhino remembers per machine — it cannot be shipped pre-docked.
- **Custom icons are unfinished.** The `bitmap_id` on every macro in
  `default.rui` indexes Rhino's internal icon library, which a third party
  cannot reference. Own icons go as base64 in the `<bitmaps>` section, whose
  encoding is not documented here. Buttons render their text until then. The
  cheap way to get there is to assign images once in Rhino's toolbar editor and
  commit the file Rhino writes back.

RhinoCommon can open and save `.rui` files but cannot create toolbars or
buttons, so the file is authored by hand or by Rhino's editor — not from code.

## The assembly Guid

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

Pinned to RhinoCommon/Grasshopper **8.0.23304.9001** — the 8.0 API baseline, not
the installed 8.34. Compiling against the oldest supported API is what makes the
plug-in usable on every Rhino 8: you cannot accidentally call something that did
not exist yet. `yak build` reads that back out and tags the package `rh8_0-win`.

The cost is two suppressed warnings. McNeel only began shipping a `net7.0` lib in
the RhinoCommon package at 8.19, so 8.0 resolves through NuGet's net48 fallback
(NU1701), and that fallback in turn confuses the platform-compatibility analyzer
into flagging calls between our own assemblies (CA1416). Neither is actionable —
the managed API surface is identical, and a Rhino plug-in is Windows-only by
construction. Verified by building against 8.0 and loading the result in 8.34:
plug-in registered, commands present, panel opens, component listed.

Raising `RhinoVersion` in `Directory.Build.props` raises the minimum Rhino your
users need, so only do it to reach an API that genuinely is not in 8.0.

Target framework is `net7.0-windows` — Rhino 8's runtime. The SDK here is .NET 10,
which builds net7.0 fine; `CheckEolTargetFramework` is off to silence the nag.
Only Rhino ships a .NET 7 runtime, so the test project sets
`RollForward=LatestMajor` to run outside Rhino.
