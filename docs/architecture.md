# Architecture

## The one rule

**Adaptor → Domain → Core. Never backwards, and domains never reference each
other.** A domain that needs another domain is the signal that something belongs
in Core — not that the two should be coupled.

There is one sanctioned exception, the machine learning stack, described below.

```
                    OtterLogic.Core
              small, stable, slow-moving
                          ↑
              OtterLogic.MachineLearning
     shared ML base - features, decomposition, datasets, ONNX
                          ↑
   Unsupervised   Supervised   Reinforcement   DeepLearning
      one repo per paradigm - siblings, never referencing each other
                          ↑
   OtterLogic.StructuralForm  OtterLogic.StructuralDesign  (+ Fabrication, ...)
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

Here, `TrussType`, `FlatTrussOptions`, `FlatTruss` and `FlatTrussGenerator` live
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

### Why Machine Learning is a layer, not a domain

Machine learning is not a discipline alongside trusses and nesting; it is
something every discipline wants to apply to its own results. Left as a domain it
would be unreachable — Fabrication could not cluster its panels without either a
domain-to-domain reference or its own copy of the algorithm.

The alternative was to push the ONNX plumbing and the algorithms down into Core.
That fails the test above: it drags a native runtime dependency into the
foundation every domain compiles against, whether or not it does any inference,
and it turns Core into the grab-bag it is meant not to be.

So the stack sits between the two. It references Core; toolkits may reference it;
it references no toolkit. The arrow is one-way, so the no-cycles rule holds. It
also ships components of its own under the **Machine Learning** section — being a
shared layer does not stop it being a tool.

### Why a repo per paradigm, and one base beneath them

`OtterLogic.MachineLearning` is not where the algorithms live. It holds only what
*more than one paradigm* needs — feature preparation, decomposition, the dataset
contract, and the ONNX runtime — and each paradigm gets its own repo above it:
`Unsupervised` first, then `Supervised`, `Reinforcement` and `DeepLearning` as
each gains a real algorithm.

That is the same test Core passes one layer down, applied one layer up. Paradigm
repos are siblings and never reference each other; a thing two of them need moves
*down* into MachineLearning rather than sideways. Which is what keeps the base
honest: `FeaturePipeline` and `PrincipalComponents` sit there because a regressor
scales and projects its inputs exactly as a mixture does, not because it was
convenient to leave them behind.

The seam that earns a project boundary *inside* MachineLearning is the ONNX one,
because it is the only one with a dependency difference. Fitted-at-solve-time
algorithms carry nothing; inference carries a native runtime. Keeping
`OtterLogic.MachineLearning.Inference` separate means a toolkit doing nothing but
k-means never drags those binaries onto a test runner.

### Where a purpose-built model goes

**A purpose-built model lives in the toolkit for the domain it has an opinion
about — never in a paradigm repo.** The paradigm repo owns the mechanism; the
domain toolkit owns what the numbers mean.

The 6DOF Behaviour Classifier is the worked example, and it split cleanly in two.
Fitting three models across a range of counts, scoring them and applying the
selection rules is `Unsupervised.ClusterSelector`: it reasons about the shape of
a point cloud, which is a property of points and not of beams. Standardising per
degree of freedom, refusing to log demand, projecting onto three components and
mapping centres back into forces and moments is
`StructuralDesign.SixDofBehaviourClassifier`: every one of those is a claim about
structural data.

The test is one question — *would changing this require knowing what a bending
moment is?* If yes it belongs in the toolkit. The payoff is that a Fabrication
tool grouping panels extracts its own features and calls the same selector, with
no domain-to-domain reference and no second copy of the judgement to keep in
step.

### Why RhinoCommon rather than neutral geometry

Using `Point3d`, `Curve` and `Mesh` throughout, rather than a neutral geometry
layer with converters, is a deliberate trade. BHoM must abstract geometry because
it targets Revit, ETABS and Tekla, where RhinoCommon does not exist. Every
context this code runs in is a Rhino host, tests included.

The cost of abstracting would be concrete: `FlatTrussGenerator` alone leans on
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

The panels split into two families, and a user only ever needs one of them.

**Named for a job** — what somebody is here to do:

- **Document** — reading layers and objects out of the Rhino document.
- **Structural Form** — trusses, frames, discrete structural layouts.
- **Structural Design** — tools that act on analysis results rather than
  producing geometry. The 6DOF Behaviour Classifier today; deflection surrogates
  and section sizers as they arrive.
- **Form Finding** — relaxation and equilibrium. Empty; to be designed.
- **Fabrication** — unrolling, nesting, toolpaths.

**Named for a technique** — how it is done:

- **Machine Learning** — every raw method, whatever its paradigm: the three
  clustering algorithms today, regression and classification later, plus dataset
  capture and inference when those exist.

Add sections there rather than typing category strings into components: the
Category string is literally what names the tab, so one typo silently creates a
second one.

### Why the split is by user and not by paradigm

There was a **Clustering** panel holding the three raw methods and the 6DOF
classifier together, on the argument that splitting them across two panels would
hide one half from whoever went looking in the other. That argument is right for
four components and wrong immediately after: the panel would have to hold both
the next algorithm family and the next structural tool, and neither belongs with
the other.

So the raw methods went to **Machine Learning** and the classifier to
**Structural Design**, and the line between them is *who is asking*. A structural
engineer with analysis results is looking for a job, and finds it in a panel
named for one without ever needing to know what a covariance shape is. Somebody
driving a method directly, or reproducing what a finished tool did with their own
choices, goes to the panel named for the technique.

Machine Learning stays **one** panel rather than one per paradigm. `GH_Exposure`
draws a divider between tiers inside a panel, so the pipeline stages — data,
features, learning, evaluation, then enum dropdowns — get their structure without
four near-empty subcategories. Split it when it genuinely overflows, around
twenty-five components.

### Re-cutting the ribbon is free

Grasshopper serialises a component instance by its `ComponentGuid`; `Category`
and `SubCategory` are display-only. **Moving a component between panels does not
break a saved definition.** That is what makes the arrangement above a judgement
call rather than a commitment: it costs one constant in `Sections` and one string
per component to change your mind, so the ribbon should be cut for the user you
have rather than hedged for the one you might.

The GUID is the thing that must never change. Everything else here can.

Those constants live in `OtterLogic.Core.Sections`, and the Grasshopper
`Categories` class is a thin alias over them. That is a deliberate exception to
keeping Core algorithm-only: the section names are domain vocabulary rather than
UI. One copy is what stops the ribbon tab and anything else reading them
drifting apart as tools get added.

## The Rhino side is a toolbar, and only a toolbar

There was a dockable panel listing every tool with a description each. It was
removed. A Rhino panel is where Layers and Properties live — persistent,
document-shaped state you keep open and consult. A launcher for commands is not
that, and putting one there competes for space against the panels that have
earned it. The toolbar is the native idiom for starting a tool, so the plug-in
uses that and nothing else.

What went with it: `OtterLogicPanel`, `ToolCatalog`, `PanelIcon`, the
`OtterLogic` command that toggled it, the panel registration in `OnLoad`, and
the last dependency on Eto.

### The toolbar

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
- **Custom icons ride in the file as base64.** The `bitmap_id` on every macro in
  `default.rui` indexes Rhino's internal icon library, which a third party
  cannot reference, and its `<bitmaps>` section is empty. Ours carries one
  horizontal PNG strip per size — 16, 24 and 32 — with a `bitmap_item` naming
  the slot each icon occupies, and each macro's `bitmap_id` pointing at its
  slot. `build/build-rui-icons.ps1` regenerates the whole block from
  `assets/icons`, so replacing a PNG is one command rather than an edit to
  several kilobytes of base64.

RhinoCommon can open and save `.rui` files but cannot create toolbars or
buttons, so the file is authored by hand or by Rhino's editor — not from code.

## Icons

Masters live once in `assets/icons` as PNGs, and every surface scales from them:

| Surface | How it gets there |
|---|---|
| Grasshopper component and ribbon tab | embedded resource, scaled to 24x24 |
| Rhino toolbar | base64 strips inside the `.rui` |
| Package Manager listing | a loose copy in the package, named by `manifest.yml` |

`EmbeddedIcons` lives in the Grasshopper project, which is now its only
consumer — the Rhino side needs no icons at runtime because the toolbar carries
its own inside the `.rui`. It is not in Core because Core and the domains are
not allowed to reference `System.Drawing`; icons are an adaptor concern.

Its bitmaps are cached and shared, so **callers must not dispose them**. A
`using` on one leaves every later caller holding a dead handle, and the failure
surfaces a long way from the cause. Both callers were written that way first.

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

`FlatTrussGenerator` places nodes at *stations* — positions from 0 to 1 along a
chord, measured as a fraction of its **plan** length. One list, shared by both
chords, so top node `i` and bottom node `i` sit at the same plan position and
every web pattern stays index arithmetic over panel count.

### Why plan distance

A pitched top chord is longer than the level bottom chord beneath it. Divide each
by its own length and node `i` lands a different distance along each of them, so
the member joining the pair leans — visibly, and worse the steeper the pitch.
Measuring in plan puts the pair at the same place on the ground, and the vertical
stands up. The plan ruler is the chord projected onto world XY; the chord itself
is still what the node is evaluated on, so nothing is flattened.

That correspondence has to be exact, which is why chords are converted to NURBS
first. Project an arc as an arc and the projection runs at a different speed
along itself, so a parameter stops meaning the same place on both — measured on a
12 m chord that is a 24 mm error in every node. A NURBS projects to a NURBS with
the same knots and the same domain, and the error is zero. A chord seen edge-on
in plan has no plan length to divide, so it measures along itself instead.

### How the list gets built

**Divisions drive, snap points steer.** With `Divisions` (or `SnapSpacing`) set,
the panel count is fixed up front, the chord is laid out evenly on plan, and the
stations then snap onto nearby targets. Member count is exactly what was asked
for; snap points move members but never add them.

**The two kinds of target are not equal.** The chords' own points — the vertices
and kinks of *either* chord — go first and take every station they can reach. A
node anywhere but a kink leaves a chord member cutting that corner, which is a
truss that is wrong rather than a truss that is arranged differently, so these
are not up for negotiation. Only then are the picked points offered whatever is
left, and each reaches only as far as `SnapDistance`: the radius of a sphere
drawn around it, measured as a real 3D distance from the point to the node it
would move, with zero meaning no limit. Measuring that on plan instead would let
a point far above or below a chord pull a node it is nowhere near.

Both passes cap reach at half a panel — far enough to catch a nearby vertex,
never far enough for two stations to swap places or collapse together — so no
`SnapDistance`, however generous, can reorder the truss. Assignment within a pass
is greedy, nearest pair first, with both sides claimed exclusively, because
otherwise two stations converge on one popular point and the panels either side
degenerate. Ties break on the lower station: `List.Sort` is unstable, and a point
equidistant between two stations is a point in the middle of a panel, which is
nothing unusual.

**Then what did not snap is spread.** Snapping alone leaves the two panels either
side of a snapped node short and long while the whole rest of the chord keeps its
original spacing, which reads as a mistake because it is not how anyone sets a
truss out. The snapped stations are fixed points, the chord ends are fixed
points, and what lies between two fixed points is divided evenly. Panel count is
untouched — stations move between anchors, they are never added or removed.

**Geometry drives.** With neither set, the snap points *are* the stations:
polyline vertices, curve kinks and picked points each become a node, with nothing
to spread. A plain line contributes none, so two lines give a single panel. That
is deliberate: the alternative is inventing a panel count the user did not ask
for.

### One list, not two

Stations used to be per chord, each snapping only to its own points, so that a
point beside the bottom chord moved the bottom node and left the top one alone.
Spreading is what ended that. With one chord anchored and the other not, every
station after the anchor moves on one chord only, and a single picked point tilts
the whole run of verticals after it rather than the one beside it. Measured on a
30 m truss with two snap points, every vertical came out leaning, by up to 1.5 m
at the worst node.

So a snap point now anchors both chords. Which chord it belongs to still decides
*where* it lands — it is measured against the one it sits nearer to, because a
point beside a sagging bottom chord is at a different plan position from the one
directly above it — but a panel point is a place where the whole truss steps, and
both chords step there. That is also what trusses do.

Where the chords converge to a shared point — the tip of a cantilever, the apex
of a tapered truss — `ChordsMeetAtStart` / `ChordsMeetAtEnd` suppress both the
end post and the end diagonal at that end. The post would collapse onto the
shared point and clash with the chords running into it. The diagonal is subtler:
top node *i* and bottom node *i* are the same point there, so a diagonal out of
it runs to the next node along one chord or the other, which is that chord
member drawn a second time. The generator's seen-set does not catch that one —
same line, different node indices — so the panel is skipped outright.

`Flip` mirrors each diagonal within its own panel, implemented by swapping which
way the two web helpers run rather than by branching per pattern. Pratt flipped
is Howe; cross-braced draws both diagonals already, so it comes out identical.

## Where the front-ends legitimately differ

Adapters are thin, but thin is not the same as identical. Both front-ends hand
back every member the engine produced; what they do with them is where they
diverge, and that divergence is the adapter's whole job.

Grasshopper sorts the members onto output ports, because a port is how a canvas
passes work along. Rhino has no ports, so the command uses what Rhino does have:
each run gets a root layer — `OtterFlatTruss1`, then `OtterFlatTruss2`, numbered from the
highest already in the document rather than from a count, so deleting one does
not make a later run merge into what is left of it — with a sub-layer per section
group underneath.

The unit is the **run**, not the truss. Trusses raised together are a bay: picked
together, answered for together, sized together. Splitting them into a tree each
would mean assigning the same section four times over, which is precisely the
sorting the layers exist to avoid. So they share the layers, and a truss keeps
its identity through geometry rather than bookkeeping.

Nothing is grouped. A group was doing the same job the layer tree already does
— saying which run a member came from — while making a single member harder to
pick, since selecting one selects the bay. The layers carry that on their own.

The two sets are the same six groups in the same order, and both take their
names from `TrussMemberRole.DisplayName()` rather than spelling them out, so a
port and a layer cannot come to disagree about what a diagonal is called.

A layer says *what* a member is, so the next tool along can put a section
against a whole layer without inspecting any geometry — which is why the layers
are the likely section groups (top chord, bottom chord, vertical, diagonal, end
post, node) rather than the four structural families.

That taxonomy is the reason `TrussMemberRole` splits `Vertical` from `Diagonal`
rather than carrying a single `Web`. The distinction is structural, not
presentational — a vertical and a diagonal are specified separately — so it
belongs in the engine, where both front-ends can see it. `FlatTruss.Web` still
returns the two together for callers that do not care.

Rhino baking the chord members is not a duplicate of the curves the user picked:
those are whole curves, these are members split at every node, which is the form
a section and an analysis both want. The picked curves are left untouched.

The other real divergence is multiplicity. Grasshopper gets a truss per branch
for free — that is what a data tree is — so the component stays one truss in, one
truss out. Rhino has no such thing, so the command takes a set of top chords and
a set of bottom chords and builds the bay in one pass, pairing them by pick
order: top chord *i* with bottom chord *i*.

Pick order rather than anything cleverer, deliberately. A geometric rule —
nearest chord, say — reads well until two trusses sit closer together than they
are deep, at which point it silently pairs the wrong chords and the user has no
way to see why. Pick order is a rule the user is already following while they
pick, so a wrong result is a wrong pick, and the fix is to run the command again.
Guessing would trade an obvious mistake for an invisible one.

Matching the two counts is therefore the command's business and nobody else's: a
mismatched pick is a question to put back to the user, not a state the engine
should ever be handed.

## What belongs where, checked against the front-ends

Every time both front-ends say the same thing, that is a claim about trusses
rather than about hosts, and it belongs below them:

- **The wording of a warning.** "The two chords are not coplanar, so this truss
  is warped" was written out twice, in two files, in two repositories. It is now
  `FlatTruss.Notes`, a list of `TrussNote` carrying a level the host maps onto
  whatever it has — a Grasshopper bubble, a command-line line. The domain decides
  *what* is worth saying; the adapter decides only how loudly.
- **The rules an option has to obey.** The component used to re-check divisions,
  spacing and truss type before calling a generator that checks all three itself
  and throws with a usable message. The duplicates are gone; `FlatTrussGenerator`
  validates its own options, including the truss type it previously let through
  to fail deeper in.
- **Which nodes are actually distinct.** Where the chords meet, top node *i* and
  bottom node *i* are the same point, and both front-ends wanted the merged list
  — one to bake, one to output. `FlatTruss.DistinctNodes` does the merging;
  `FlatTruss.Nodes` still carries the duplicates, because member connectivity
  indexes into it.
- **How an enum reads to a human.** `Naming.Humanise` is in Core rather than the
  domain, because it knows nothing about trusses: every domain grows option
  enums and both front-ends have to show them. It was Grasshopper-only before,
  which is why the Rhino prompt used to say "WarrenWithVerticals".

What deliberately stayed in the adapter: the layer colours (Core may not
reference `System.Drawing`, and a colour is a host decision anyway), the pairing
of picked chords, and the layer naming. Those describe Rhino, not trusses.

## Enum dropdowns

An option that is a closed set gets a `GH_ValueList` of its own in the ribbon —
`Truss Type` is the first — rather than only a right-click menu on the input.
The menu is fine once you know to look; the dropdown is for when you do not, and
it leaves the choice visible on the canvas to whoever opens the definition next.

`EnumValueList<TEnum>` carries all of the behaviour, so a concrete one is a
constructor call, a GUID and an icon. Generic because every domain grows option
enums and each will want the same thing; writing the second one by hand is how
the two of them drift.

Both the dropdown and the receiving input build their labels from
`EnumChoices.Of<TEnum>()`, which is `Naming.Humanise` over the enum's values. One
call, so a dropdown and the input it feeds cannot come to spell an option two
different ways — the same reason `Humanise` is in Core rather than in either
front-end.

The enum itself stays in the domain. `TrussType` is truss knowledge and lives in
StructuralForm next to the generator that reads it; the dropdown is an adaptor
that knows how to show an enum and nothing about trusses beyond which one to
show. That split is why the next dropdown costs a file and not a design.

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
generator over Core's static entry points — `FlatTrussGenerator.Generate` is
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
plug-in registered, commands present, toolbar loaded, component listed.

Raising `RhinoVersion` in `Directory.Build.props` raises the minimum Rhino your
users need, so only do it to reach an API that genuinely is not in 8.0.

Target framework is `net7.0-windows` — Rhino 8's runtime. The SDK here is .NET 10,
which builds net7.0 fine; `CheckEolTargetFramework` is off to silence the nag.
Only Rhino ships a .NET 7 runtime, so the test project sets
`RollForward=LatestMajor` to run outside Rhino.
