using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.MachineLearning.Embedding;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Embeddings;

/// <summary>
/// Multidimensional scaling as a method on a wire. No data input; see
/// <see cref="PrincipalComponentsMethodComponent"/>.
/// </summary>
public sealed class MultidimensionalScalingMethodComponent : GH_Component
{
    public MultidimensionalScalingMethodComponent()
        : base("Multidimensional Scaling", "MDS",
               "The Multidimensional Scaling method for OtterEmbed: place the samples so the distances "
               + "between the points match the distances between the samples as closely as the "
               + "dimensions allow.\n\n"
               + "What OtterEmbed does with nothing wired. It keeps every distance it can, reports in "
               + "Report how far it had to bend the rest, and says per sample in Distortion which points "
               + "not to trust. The axes mean nothing of their own — turn the map and it says the same "
               + "thing. Use Principal Components when the axes should be readable as columns, and "
               + "Spectral Embedding when what connects to what matters more than distance.\n\n"
               + "This component takes no samples: wire its Method output into OtterEmbed.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("25b48a18-643a-4a5b-9a92-f8763d90e5e3");

    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    public override IEnumerable<string> Keywords => new[] { "mds", "scaling", "stress", "distance", "embedding" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("multidimensionalscaling", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddBooleanParameter("Refine", "R",
            "Improve the first map against the distances themselves. On by default; it never makes the "
            + "map worse, and off is only for speed on thousands of samples.",
            GH_ParamAccess.item, true);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new EmbeddingMethodParameter(), "Method", "M", MethodWire.EmbeddingMethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        bool refine = true;
        if (!da.GetData(0, ref refine)) return;

        var method = new MultidimensionalScalingMethod { Refine = refine };
        da.SetData(0, new GH_EmbeddingMethod(method));
        Message = method.Describe();
    }
}
