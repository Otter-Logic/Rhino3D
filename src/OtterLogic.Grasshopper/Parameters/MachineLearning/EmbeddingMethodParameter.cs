using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Types;

namespace OtterLogic.Grasshopper.Parameters.MachineLearning;

/// <summary>
/// The parameter every OtterEmbed Method input and output is made of. Hidden and
/// not persistent, for the same reasons as <see cref="ClusterMethodParameter"/>.
/// </summary>
public sealed class EmbeddingMethodParameter : GH_Param<GH_EmbeddingMethod>
{
    public EmbeddingMethodParameter()
        : base("Embedding Method", "Method",
               "A way of laying samples out as points, with its settings chosen. Made by Principal "
               + "Components, Multidimensional Scaling or Spectral Embedding; read by OtterEmbed.",
               Categories.Root, Categories.MachineLearning, GH_ParamAccess.item)
    {
    }

    public override Guid ComponentGuid => new("50bca32b-362a-4d1a-bcad-b9f6e7e7a0dc");

    public override GH_Exposure Exposure => GH_Exposure.hidden;

    protected override Bitmap? Icon => EmbeddedIcons.Load("embeddingmethod", 24);
}
