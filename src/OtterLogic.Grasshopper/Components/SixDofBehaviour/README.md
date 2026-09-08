# 6DOF Behaviour components

The end-product layer: analysis results in, behaviour groups out. Adaptors only —
logic lives in the
[6DOF Behaviour Classifier](https://github.com/Otter-Logic/6DOF_Behaviour_Classifier)
repo, `OtterLogic.SixDofBehaviour`.

Deliberately the opposite of the Machine Learning components. Those expose every
setting so an advanced user can drive the methods directly; these expose as close
to nothing as the problem allows, because a structural engineer with a set of
member forces should not need an opinion about covariance shapes to find out
which members behave alike.

Grasshopper only. Wire-data tools with no document-level shape, so nothing here
gets a Rhino command or a toolbar button.
