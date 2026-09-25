# Training

Training runs inside Rhino, in C#, and nothing in this folder takes part in it.
The folder stays for the notes below and as a place to put dataset folders while
trying the loop out; nothing in it is committed or shipped.

The loop, end to end:

1. **Generate** — wire samples straight into OtterTrain, or gather them one model
   at a time with Write Dataset and read them back with Read Dataset.
2. **Train** — OtterTrain, on a background task, fits the learner on the wire —
   Boosted Trees, Random Forest, Neural Network, Linear Model — scores it on
   held-out groups, writes one `.onnx` carrying its own metadata, and runs the
   file back through ONNX Runtime before keeping it. The code is `TrainRun.Fit`
   in [Supervised](https://github.com/Otter-Logic/Supervised).
3. **Infer** — OtterPredict opens that file and answers in under a millisecond.
   It also opens an `.onnx` from anywhere else — a PyTorch export with no
   OtterLogic metadata runs the same way, with unnamed features.

There used to be a Python trainer behind step 2, fetched as a private runtime
bundle. It was taken out on 2026-09-25 before it shipped; MachineLearning's
`docs/in-process-training.md` records why.

`datasets/` is a convenient place for the folders step 1 writes while trying the
loop out; nothing in it is committed.
