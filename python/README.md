# Training

Training is offline. It never runs inside Rhino, and it no longer lives in this
repo: the trainer is `python/trainer/` in
[MachineLearning](https://github.com/Otter-Logic/MachineLearning), and OtterTrain,
under Machine Learning, runs it as a separate process.

The loop, end to end:

1. **Generate** — wire samples straight into OtterTrain, or gather them one model
   at a time with Write Dataset and read them back with Read Dataset.
2. **Train** — OtterTrain, or `python -m otterlogic_trainer job.json` from a
   terminal, writes one `.onnx` carrying its own metadata. The learner — Boosted
   Trees, Neural Network, Linear Model, Nearest Neighbours — goes in on a wire.
3. **Infer** — OtterPredict opens that file and answers in under a millisecond.

The trainer needs its own Python. OtterTrain installs one from the MachineLearning
release through its right-click menu; a developer working against a checkout
points `OTTERLOGIC_TRAINER` at a virtual environment with the trainer package
installed instead.

`datasets/` is a convenient place for the folders step 1 writes while trying the
loop out; nothing in it is committed.
