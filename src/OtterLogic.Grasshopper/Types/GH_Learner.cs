using Grasshopper.Kernel.Types;
using OtterLogic.Supervised.Learners;

namespace OtterLogic.Grasshopper.Types;

/// <summary>
/// The Learner wire: a method the trainer can fit, with its settings chosen, on
/// its way from a learner component to OtterTrain.
/// <para>
/// Immutable like <see cref="GH_ClusterMethod"/>'s method, so a duplicate shares
/// it. What travels is the record the Supervised repo fits from, so the canvas and
/// the trainer cannot disagree about a setting.
/// </para>
/// </summary>
public sealed class GH_Learner : GH_Goo<Learner>
{
    public GH_Learner() { }
    public GH_Learner(Learner learner) : base(learner) { }

    public override bool IsValid => Value is not null;
    public override string TypeName => "Learner";
    public override string TypeDescription => "A method the trainer can fit, with its settings chosen, for OtterTrain's Learner input";

    public override IGH_Goo Duplicate() => new GH_Learner(Value);

    public override string ToString() => Value is null ? "<null learner>" : Value.Describe();
}
