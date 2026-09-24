using Grasshopper.Kernel.Types;
using OtterLogic.MachineLearning.Embedding;

namespace OtterLogic.Grasshopper.Types;

/// <summary>
/// The Method wire of OtterEmbed: a way of laying samples out, with its settings
/// chosen, on its way from a method component to the core.
/// <para>
/// The method is a record and never changes once made, so a duplicate shares it —
/// the same reason <see cref="GH_ClusterMethod"/> shares its method. Two wires
/// carrying equal settings compare equal, which is what a value on a wire should do.
/// </para>
/// </summary>
public sealed class GH_EmbeddingMethod : GH_Goo<EmbeddingMethod>
{
    public GH_EmbeddingMethod() { }
    public GH_EmbeddingMethod(EmbeddingMethod method) : base(method) { }

    public override bool IsValid => Value is not null;
    public override string TypeName => "Embedding Method";
    public override string TypeDescription => "A way of laying samples out as points, with its settings chosen, for OtterEmbed's Method input";

    public override IGH_Goo Duplicate() => new GH_EmbeddingMethod(Value);

    public override string ToString() => Value is null ? "<null embedding method>" : Value.Describe();
}
