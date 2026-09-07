# Training

Training is offline. It never runs inside Rhino.

The loop, end to end:

1. **Generate** — sweep parameters in Grasshopper or a Rhino command, write rows
   to `python/datasets/*.csv` (or `.npz` for anything mesh-shaped).
2. **Train** — `python training/train_surrogate.py` reads the dataset and writes
   a `.onnx` into `../models/`.
3. **Infer** — a component in `OtterLogic.Grasshopper` loads that `.onnx` through
   `OtterLogic.MachineLearning` and evaluates it in under a millisecond.

Step 2 takes seconds to hours. Steps 1 and 3 are the interactive ones. Nothing
about this is "live training" — see `docs/machine-learning.md`.

## Setup

```
py -3.12 -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
```
