# Learning

A future domain — and the one that will not fit the pattern cleanly.

Machine learning is cross-cutting: every other domain will want to capture
datasets and evaluate surrogates over its own results. So the split is likely to
be ONNX inference plumbing and the dataset contract in **Core**, with each domain
owning its own feature extraction.

That is a guess, and it should be decided when there is a second domain to look
at rather than now. Training itself stays offline in `/python`; see
[docs/machine-learning.md](../../docs/machine-learning.md).
