using System.Drawing;
using Grasshopper.Kernel;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.Grasshopper.Types;
using OtterLogic.MachineLearning.Embedding;

namespace OtterLogic.Grasshopper.Components.MachineLearning.Embeddings;

/// <summary>
/// A spectral embedding as a method on a wire. No data input; see
/// <see cref="PrincipalComponentsMethodComponent"/>.
/// </summary>
public sealed class SpectralEmbeddingMethodComponent : GH_Component
{
    public SpectralEmbeddingMethodComponent()
        : base("Spectral Embedding", "SpecEmbed",
               "The Spectral Embedding method for OtterEmbed: lay samples out by what they connect to, "
               + "not by how far apart their values are.\n\n"
               + "Samples joined by a path of connections land together whatever shape the path takes, "
               + "which is what neither Principal Components nor Multidimensional Scaling can do. Wire a "
               + "Graph into OtterEmbed and it lays out the graph — a network of rooms, a frame's members, "
               + "a street map — reading each connection as present or absent. With Data alone it links "
               + "each sample to its nearest few and lays out those links. What OtterEmbed does with a "
               + "Graph and nothing else wired.\n\n"
               + "This component takes no samples: wire its Method output into OtterEmbed.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("ec7a1fd4-f9cb-4aaa-8d43-b32a4c9699f5");

    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    public override IEnumerable<string> Keywords => new[] { "spectral", "laplacian", "eigenmap", "graph layout", "embedding" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("spectralembedding", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddIntegerParameter("Neighbours", "N",
            "How many of its nearest samples each sample is linked to when the links are built from "
            + "Data. Ten by default; fewer follows thinner shapes, more smooths them. Ignored when a "
            + "Graph is wired.",
            GH_ParamAccess.item, 10);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddParameter(new EmbeddingMethodParameter(), "Method", "M", MethodWire.EmbeddingMethodOutput, GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        int neighbours = 10;
        if (!da.GetData(0, ref neighbours)) return;

        var method = new SpectralEmbeddingMethod { Neighbours = neighbours };
        da.SetData(0, new GH_EmbeddingMethod(method));
        Message = method.Describe();
    }
}
