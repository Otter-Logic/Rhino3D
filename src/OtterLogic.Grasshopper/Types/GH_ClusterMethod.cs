using Grasshopper.Kernel.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Types;

/// <summary>
/// The Method wire: a clustering algorithm with its settings chosen, on its way
/// from a method component to OtterCluster.
/// <para>
/// The method is a record and never changes once made, so a duplicate shares it —
/// the same reason <see cref="GH_Graph"/> shares its graph. Two wires carrying
/// equal settings compare equal, which is what a value on a wire should do.
/// </para>
/// </summary>
public sealed class GH_ClusterMethod : GH_Goo<ClusteringMethod>
{
    public GH_ClusterMethod() { }
    public GH_ClusterMethod(ClusteringMethod method) : base(method) { }

    public override bool IsValid => Value is not null;
    public override string TypeName => "Cluster Method";
    public override string TypeDescription => "A clustering algorithm with its settings chosen, for OtterCluster's Method input";

    public override IGH_Goo Duplicate() => new GH_ClusterMethod(Value);

    public override string ToString() => Value is null ? "<null cluster method>" : Value.Describe();
}
