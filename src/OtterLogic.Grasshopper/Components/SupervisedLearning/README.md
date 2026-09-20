# Supervised Learning components

The raw supervised methods, for somebody assembling their own pipeline: fit to
samples whose answer is known, predict the answer where it is not, score how that
went. Adaptors only — the algorithms live in the
[Supervised](https://github.com/Otter-Logic/Supervised) repo,
`OtterLogic.Supervised`.

| Tier (`GH_Exposure`) | Component | Library call |
|---|---|---|
| `tertiary` — methods | **Nearest Neighbour Classifier** | `KNearestNeighbours.FitClassifier` |
| | **Nearest Neighbour Regressor** | `KNearestNeighbours.FitRegressor` |
| | **Ridge Regression** | `RidgeRegression.Fit` |
| | **Logistic Regression** | `LogisticRegression.Fit` |
| `quarternary` — evaluation | **Evaluate Classification** | `ClassificationReport.From` |
| | **Evaluate Regression** | `RegressionReport.From` |
| `quinary` — dropdowns | **Neighbour Weighting** | `EnumValueList`, in `Parameters/SupervisedLearning` |

`primary` and `secondary` are left free, to line up with the other paradigm
panels. The dataset components — Write Dataset, Read Dataset, Split By Group — are
under **Machine Learning**, because every paradigm gathers data before it does
anything else.

## Shape

Every method takes the same three inputs first, in the same order, so a
definition wired for one can be retargeted at another by dragging three wires:

1. **Training Inputs** — one branch per sample, the LunchBoxML shape every
   clustering component already takes.
2. **Training Labels** (text) or **Training Values** (numbers) — the known answer
   per training sample.
3. **Test Inputs** — one branch per sample to predict.

Fit and predict happen in one component, on every solve. These models are fitted
at solve time — there is nothing to save and nothing to load — which is what makes
them usable with no Python and no download. A model that *is* trained and saved
comes back through ONNX, in phase 1, and the Evaluate components score its
predictions exactly as they score these.

Labels are text. A class is a name, not a quantity, and the commonest classes of
all are written 0 and 1 — which is why nothing here ever guesses. Classes are put
in one fixed order (by value when every label is a number, otherwise by text), and
every per-class output lists them in that order on its **Classes** output.

**Standardise** is on by default on every method, fitted on the training samples
only and applied to the test samples. That is not an opinion about the data: these
methods add columns together, so without it the model has only looked at whichever
column has the largest units.

## The intended loop

```
Read Dataset ──► Split By Group ──► a method ──► Evaluate
                  (whole models        ▲
                   held back)          └── try another, compare
```

Never evaluate on samples the method was fitted on, and never split by sample:
samples from one model are near-copies of each other, so a random split scores
recognition rather than prediction. Evaluate Classification warns when accuracy
does not beat always answering the commonest class, which on lopsided targets is
a much higher bar than it sounds.

Grasshopper only. Wire-data tools with no document-level shape, so nothing here
gets a Rhino command or a toolbar button.
