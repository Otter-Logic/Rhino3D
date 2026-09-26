# Structural Design components: the one-wire cut

Decided 2026-09-26. Cuts the Structural Insight Engine from eleven inputs and
twenty-two outputs to six and eleven, moves its tuning onto a Settings wire,
trims the four tools beside it, and tiers the panel by the step a user is at.
This file records what was decided and why, so the next person who wants an
output back knows what it cost to have it there.

## Who this is for

The same person the [machine learning cut](ml-components.md) was made for: an
engineer, a fabricator, a site team. They want to plug a model's geometry in
and get three things out — the natural groups its elements fall into, what
rests on what, and a table they can take on to analysis or to a model of their
own. They will not learn what an eigengap is, and should not have to scroll
past four outputs that assume they did.

## What the engine looked like, and why

It had grown one output at a time, each appended after Report so no saved wire
moved. By 2026-09 the twenty-two outputs were four answers said several ways:

| The answer | How many ways it was said | Outputs |
|---|---|---|
| The grouping | five | Group, Indices, Line Groups, Surface Groups, Hierarchy |
| How the fusion voted | five | Agreement, Connectivity Groups, Geometry Groups, Density Groups, Role Groups |
| The engine's reading | seven | Features, Feature Names, Member, Assembly, Level, Flow, Report — all of which Describe Member also gives |
| What is worth a look | four | Issue Elements, Issue Points, Issue Reasons, Flags — six of the ten flags being checks Geometry QA does more carefully |

Seven of the eleven inputs were tuning: four view weights, Maximum Groups,
Minimum Group Size and Groups. Every one already had a default the engine was
designed to run on.

## The shape now

```
   Lines ─────────► ┌──────────────────────────┐ ──► Group, Grouped Geometry
   Surfaces ──────► │ Structural Insight Engine│ ──► Level, Hierarchy, Confidence
   Supports ──────► │                          │ ──► Features, Feature Names, Graph
   Tolerance, Groups│                          │ ──► Issue Points, Issue Reasons
   [Settings] ────► └──────────────────────────┘ ──► Report

   Insight Settings ──► one "Settings" wire: Maximum Groups, Minimum Group Size,
                        the four view weights, Weight By Agreement, Chain Members
```

What each surviving output is for:

- **Group** and **Grouped Geometry** — the answer, per element and as geometry
  per group. Line Groups and Surface Groups were one tree split in two; Indices
  is Group inverted, which Grasshopper does natively.
- **Level** and **Hierarchy** — what rests on what, and the `{level; group}`
  schedule view that is the reason the engine exists.
- **Confidence** — was Agreement; the same word OtterCluster uses for the same
  idea. The four per-view labels it summarises are in Report.
- **Features** and **Feature Names** — the table that wires into OtterCluster,
  OtterTrain and Write Dataset. Kept here although Describe Member gives the
  same table, because "geometry in, ML-ready data out" is the one-component
  story; Describe Member owns the member-level table, member curves and
  orientation.
- **Graph** — one Graph wire in place of a Connectivity integer tree, with each
  node at its element's centroid so it draws over the model. Deconstruct Graph
  gives the tree back for anyone who wants it.
- **Issue Points** and **Issue Reasons** — where to look and why. Issue Elements
  is named in every reason; Flags said the same list per element.
- **Report** — carries every number the dropped outputs did.

Member, Assembly and Flow are Describe Member's. Ask there.

## The Settings wire

`GH_InsightSettings` wraps `StructuralInsightOptions`, which was already an
immutable record with a default for everything. Insight Settings makes one and
outputs it; the engine reads it, or defaults, and overrides Tolerance and Groups
from its own inputs. Tolerance stays on the engine because it is the
document's; Groups stays because it is the one setting a user actually reaches
for. This is the method-on-a-wire pattern from the Machine Learning panel, for
the same reason: a setting a user has to understand before they can ignore it
should live on a component they never need to place.

## The panel

One panel, ordered by `GH_Exposure`, which draws a divider between tiers:

| Exposure | Step | Components |
|---|---|---|
| `primary` | Geometry in, answers out | Structural Insight Engine, Geometry QA |
| `secondary` | More detail or control | Describe Member, Grid and Level Inference, Insight Settings |
| `tertiary` | Needs analysis results | 6DOF Behaviour Classifier |
| `quarternary` | Dropdowns | Unassigned |

The divider before the classifier makes visible on the ribbon the line the
toolkit keeps in its code: tools that read geometry before a model solves, and
tools that read forces after.

## The lighter trims

| Component | Before | After | Dropped, and where it went |
|---|---|---|---|
| Geometry QA | 9 | 6 | Counts (the length of a Findings branch), Elements (what Geometry holds), Element Issue Count (whether Element Issues is empty); Summary renamed Report |
| Grid and Level Inference | 10 | 8 | Elevations (a Level Plane's origin Z), Orientation (Describe Member's) |
| 6DOF Behaviour Classifier | 10 | 6 | Indices (Group inverted), Centres, Projection and the Model text (how the answer was reached; all in Report). Minimum and Maximum stay: they are the envelope a group is designed for |
| Describe Member | 12 | 11 | Members (the Member label inverted) |

## What it cost

Outputs are wired by position, so every saved definition on the 0.1.0 test
builds needs rewiring where it touched these components. Component GUIDs did
not change, so the components themselves still load, with their wires
displaced rather than lost. The plugin is undistributed, which is why this was
a delete rather than a hide-and-obsolete; once anything ships, the rule in
[grasshopper.md](../../../.claude/skills/otterlogic/references/grasshopper.md)
is back in force.
