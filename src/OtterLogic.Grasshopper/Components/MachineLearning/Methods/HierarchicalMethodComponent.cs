using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Methods;

/// <summary>
/// Hierarchical clustering as a method on a wire: how many clusters to cut the
/// tree into, and how the distance between clusters is measured. No data input;
/// see <see cref="KMeansMethodComponent"/>.
/// </summary>
public sealed class HierarchicalMethodComponent : GH_Component
{
    public HierarchicalMethodComponent()
        : base("Hierarchical Clustering", "Hierarchical",
               "The Hierarchical method for OtterCluster: merge the two closest clusters again and again "
               + "until one is left, then cut that tree into as many clusters as you ask for.\n\n"
               + "The tree is the point: cut it at 8 and at 3 and every one of the 8 sits wholly inside "
               + "one of the 3, which two separate fits never promise. Use it when the clusters should "
               + "nest, or when you will try several counts on the same data. It places every sample and "
               + "gives no confidence — a cut says which side of a merge a sample fell, not how nearly it "
               + "fell the other way. For a confidence use Gaussian Mixture; for outliers, HDBSCAN.\n\n"
               + "This component takes no samples: wire its Method output into OtterCluster.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("6fd3e1a8-80dd-4ff4-a841-6a5c774d893b");

    public override GH_Exposure Exposure => GH_Exposure.secondary;

    public override IEnumerable<string> Keywords => new[] { "hierarchical", "agglomerative", "dendrogram", "ward", "linkage", "clustering" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("hierarchicalclustering", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Clusters", "K",
            "How many clusters to cut the tree into.",
            GH_ParamAccess.item, 4);

        pManager.AddIntegerParameter("Linkage", "L",
            "How the distance between two clusters is measured. Ward builds compact clusters of similar "
            + "size; complete keeps every cluster's diameter small; average is the compromise; single "
            + "follows chains of close samples however long.\n\n"
            + "Right-click for the list, or wire a Linkage dropdown in.",
            GH_ParamAccess.item, (int)Linkage.Ward);

        var linkage = (Param_Integer)pManager[1];
        foreach (var (label, value) in EnumChoices.Of<Linkage>())
            linkage.AddNamedValue(label, value);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new ClusterMethodParameter(), "Method", "M", MethodWire.ClusterMethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        int clusters = 4;
        int linkage = (int)Linkage.Ward;
        if (!da.GetData(0, ref clusters)) return;
        if (!da.GetData(1, ref linkage)) return;

        if (!Enum.IsDefined(typeof(Linkage), linkage))
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                "Linkage must be one of "
                + string.Join(", ", EnumChoices.Of<Linkage>().Select(c => $"{c.Value} ({c.Label})")) + ".");
            return;
        }

        var method = new HierarchicalMethod { Clusters = clusters, Linkage = (Linkage)linkage };
        da.SetData(0, new GH_ClusterMethod(method));
        Message = method.Describe();
    }
}
