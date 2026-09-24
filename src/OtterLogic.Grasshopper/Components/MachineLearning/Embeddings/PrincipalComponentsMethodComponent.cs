using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.MachineLearning.Embedding;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Embeddings;

/// <summary>
/// Principal components as a method on a wire. No data input; the samples go to
/// OtterEmbed, and this only makes the <see cref="PrincipalComponentsMethod"/>
/// record for it.
/// </summary>
public sealed class PrincipalComponentsMethodComponent : GH_Component
{
    public PrincipalComponentsMethodComponent()
        : base("Principal Components", "PCA",
               "The Principal Components method for OtterEmbed: project the samples onto the directions "
               + "they spread furthest along.\n\n"
               + "The one method whose axes mean something. Each is a direction through the columns of "
               + "Data — Axes and Report say which columns make it — so the map can be read: axis 1 is "
               + "mostly length, axis 2 mostly depth. Straight lines only, though: a curved or chained "
               + "structure in the data is flattened. Use Multidimensional Scaling to keep the distances "
               + "between samples instead, and Spectral Embedding when what connects to what matters more "
               + "than how far apart the values are.\n\n"
               + "This component takes no samples: wire its Method output into OtterEmbed.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("3d7abbed-18cd-471c-917b-949fdb8a184c");

    // The embedding methods tier, below the learners.
    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    public override IEnumerable<string> Keywords => new[] { "pca", "principal component analysis", "projection", "embedding" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("principalcomponents", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddBooleanParameter("Whiten", "W",
            "Scale every axis to the same spread. Off by default: the point of the map is usually that "
            + "the first axis is the big one, and whitening hides that.",
            GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new EmbeddingMethodParameter(), "Method", "M", MethodWire.EmbeddingMethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        bool whiten = false;
        if (!da.GetData(0, ref whiten)) return;

        var method = new PrincipalComponentsMethod { Whiten = whiten };
        da.SetData(0, new GH_EmbeddingMethod(method));
        Message = method.Describe();
    }
}
