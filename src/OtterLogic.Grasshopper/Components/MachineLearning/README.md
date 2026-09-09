# Machine Learning components

The raw methods, for somebody assembling their own pipeline. Adaptors only — the
algorithms live in the
[MachineLearning](https://github.com/Otter-Logic/MachineLearning) repo,
`OtterLogic.MachineLearning`.

- **K-Means Clustering** — hard partition into a fixed number of clusters.
- **Gaussian Mixture** — soft assignment, with a probability per group.
- **HDBSCAN Clustering** — density-based, finds its own cluster count, and can
  leave a sample unassigned.
- **Covariance Type** — an `EnumValueList` dropdown, in `Parameters/`.

## One panel, ordered by exposure

Every method ships here regardless of paradigm — clustering today, regression and
classification later. Grasshopper draws a divider between `GH_Exposure` values
inside a panel, so the pipeline stages get their structure without a subcategory
each:

| Exposure | Stage |
|---|---|
| `primary` | Data — dataset capture, writers, train/test split |
| `secondary` | Features — standardise, log, weights, principal components |
| `tertiary` | Learn — the algorithms themselves. The three above |
| `quarternary` | Evaluate — silhouette, Davies-Bouldin, R², confusion matrix |
| `quinary` | Enum dropdowns. Covariance Type |

Split this into a second panel only when it genuinely overflows — around
twenty-five components, or when deep learning arrives with its own vocabulary.
The panel name is one constant in `OtterLogic.Core.Sections`, and Grasshopper
serialises a component by its `ComponentGuid` rather than its category, so
re-cutting the ribbon never breaks a saved definition.

## Why the 6DOF classifier is not here

It used to be, under a **Clustering** panel holding these three and it. That
works for four components and stops working immediately after: the panel would
have to hold both the next algorithm family and the next structural tool, and
neither belongs with the other.

So the split is by *who is asking*. These are named for techniques and answer
"how"; the classifier is named for a job and answers "what". It moved to
**Structural Design**, next to the surrogates and sizers that will follow it,
because a structural engineer with analysis results is looking for a job and not
a method. Anyone wanting to drive the methods directly — or reproduce what the
classifier does with their own choices — comes here.

## Shape

They take a `Training Inputs` tree — one branch per sample — following
LunchBoxML, so a definition already wired for those components can be retargeted
here without rebuilding the data.

Grasshopper only. Wire-data tools with no document-level shape, so nothing here
gets a Rhino command or a toolbar button: the `.rhp` stays out of it.
