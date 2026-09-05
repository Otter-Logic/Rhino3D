"""Train a surrogate model on generated design runs and export it to ONNX.

Reads a CSV where the feature columns describe the design (span, sag, rest
length factor, ...) and the target columns are whatever the solver measured
(max deflection, total edge length, ...). Writes <name>.onnx into ../models.

The point of the surrogate is speed: a solver run that takes two seconds
becomes a sub-millisecond forward pass, which is the difference between a
Grasshopper slider you can drag and one you cannot.

Usage:
    python train_surrogate.py ../datasets/truss_sweep.csv --target max_deflection
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
import pandas as pd
from skl2onnx import to_onnx
from sklearn.ensemble import HistGradientBoostingRegressor
from sklearn.metrics import mean_absolute_error, r2_score
from sklearn.model_selection import train_test_split

MODELS_DIR = Path(__file__).resolve().parents[2] / "models"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dataset", type=Path, help="CSV produced by a parameter sweep")
    parser.add_argument("--target", required=True, help="Column to predict")
    parser.add_argument("--name", default=None, help="Output model name (default: target)")
    parser.add_argument("--test-size", type=float, default=0.2)
    parser.add_argument("--seed", type=int, default=0)
    args = parser.parse_args()

    frame = pd.read_csv(args.dataset)
    if args.target not in frame.columns:
        raise SystemExit(f"'{args.target}' is not a column in {args.dataset}")

    features = frame.drop(columns=[args.target])
    # ONNX is strict about dtypes; float32 in, float32 out, matching the C# side.
    x = features.to_numpy(dtype=np.float32)
    y = frame[args.target].to_numpy(dtype=np.float32)

    x_train, x_test, y_train, y_test = train_test_split(
        x, y, test_size=args.test_size, random_state=args.seed
    )

    model = HistGradientBoostingRegressor(random_state=args.seed)
    model.fit(x_train, y_train)

    predictions = model.predict(x_test)
    print(f"features : {list(features.columns)}")
    print(f"rows     : {len(frame)} ({len(x_train)} train / {len(x_test)} test)")
    print(f"MAE      : {mean_absolute_error(y_test, predictions):.5f}")
    print(f"R2       : {r2_score(y_test, predictions):.4f}")

    MODELS_DIR.mkdir(parents=True, exist_ok=True)
    output = MODELS_DIR / f"{args.name or args.target}.onnx"
    onnx_model = to_onnx(model, x_train[:1])
    output.write_bytes(onnx_model.SerializeToString())

    print(f"\nwrote {output}")
    print("Feature order is baked into the model - keep the C# side in the same order.")


if __name__ == "__main__":
    main()
