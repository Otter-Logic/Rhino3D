# Machine Learning components

The steps every learning paradigm shares, rather than the methods themselves.
Adaptors only; the engines are in
[MachineLearning](https://github.com/Otter-Logic/MachineLearning).

A method belongs in the panel for its paradigm — clustering is an unsupervised
question before it is anything else, and lives in **Unsupervised Learning**. What
belongs here is what comes *before* a method and would come before any of them.

| Tier (`GH_Exposure`) | Component | Library call |
|---|---|---|
| `primary` — data | **Write Dataset** | `Dataset.FromColumns`, `DatasetFolder.Write` |
| | **Read Dataset** | `DatasetFolder.Read` |
| | **Split By Group** | `GroupSplit.Holdout` |
| `secondary` — features | **Shape Signature** | `ShapeSignature.Fit` |

## The dataset

A dataset is gathered one model at a time, over months, from definitions that get
edited in between. Everything about these three components follows from that.

- **Write Dataset** saves this definition's samples into a folder as one model
  among many: `schema.json`, and `models/<Model ID>.csv`. Writing the same Model
  ID again replaces that model's rows, so it is safe to leave switched on while
  Grasshopper re-solves — and an unchanged write does not touch the file at all,
  which matters when the folder is synced. The folder remembers its columns and
  refuses rows whose names, order, kinds or **Extractor Version** differ. Feature
  names are required, not decoration: order is all a model knows about its inputs,
  so the names are the only thing that can catch two being swapped later. A class
  the folder has not met before raises a warning, because it is either news or a
  typo about to become a category.

  Whether a target is a quantity or a class is **stated, never guessed**. Classes
  are very often written 0 and 1, and treated as a quantity those train a model
  that answers 0.37.

- **Read Dataset** reads every model back as one table, each sample tagged with
  the model it came from, and its Report says what to know before fitting
  anything: rows per model, how each class target is balanced, and any feature
  that never changes.

- **Split By Group** holds back *whole models* for testing. There is no split by
  sample, on purpose — samples from one model resemble each other far more than
  they resemble the next model's, so a random split fills the test set with
  near-copies of the training set and scores recognition rather than prediction.
  It renumbers branches from zero on each side, so branch *i* of every output is
  sample *i* of that side.

The methods these feed are under **Supervised Learning**.

## Features

- **Shape Signature** — outlines in; the same row of numbers describing each of
  them out, learned from the outlines rather than chosen in advance. Wire
  Signature into any clustering to find the shape families, into Data Map to see
  them, or into Group Signature to ask what sets one apart. Two outlines that are
  the same shape get the same row however they were drawn — from a different
  corner, the other way round, with extra points along an edge — and how far apart
  two rows are is how far apart the two outlines are, in model units.

  Variation is the output to look at rather than read: it draws each component as
  a shape, which is the only way a component gets a name. One population's first
  component is how raked, another's is how deep the notch, and no number says so.

  What counts as the same shape is yours to set — whether a mirror is the same
  thing, whether size matters, how many turns of it are still it — because that is
  a claim about what you are making, not about geometry.
