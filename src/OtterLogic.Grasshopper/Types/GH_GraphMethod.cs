using Grasshopper.Kernel.Types;
using OtterLogic.Graphs.Methods;

namespace OtterLogic.Grasshopper.Types;

/// <summary>
/// The Method wire of the Graphs panel: a graph algorithm with its settings
/// chosen, on its way from a method component to OtterPath.
/// <para>
/// The method is a record and never changes once made, so a duplicate shares it —
/// the same reason <see cref="GH_Graph"/> shares its graph and
/// <see cref="GH_ClusterMethod"/> its method. Two wires carrying equal settings
/// compare equal, which is what a value on a wire should do.
/// </para>
/// </summary>
public sealed class GH_GraphMethod : GH_Goo<GraphMethod>
{
    public GH_GraphMethod() { }
    public GH_GraphMethod(GraphMethod method) : base(method) { }

    public override bool IsValid => Value is not null;
    public override string TypeName => "Graph Method";
    public override string TypeDescription => "A graph algorithm with its settings chosen, for OtterPath's Method input";

    public override IGH_Goo Duplicate() => new GH_GraphMethod(Value);

    public override string ToString() => Value is null ? "<null graph method>" : Value.Describe();
}
