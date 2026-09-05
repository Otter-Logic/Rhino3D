using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using OtterLogic.Core.FormFinding;
using Rhino.Geometry;

namespace OtterLogic.Grasshopper.Components.FormFinding;

/// <summary>
/// Relaxes a mesh toward equilibrium.
/// <para>
/// The Grasshopper counterpart to the OtterRelax Rhino command. Both call
/// <see cref="MeshRelaxation.CreateSolver"/> and <see cref="MeshRelaxation.ApplyPositions"/> —
/// this one just runs the solver to convergence in one go instead of animating it.
/// Keep it that way: if you find yourself writing form-finding logic in this
/// file, it belongs in Core.
/// </para>
/// </summary>
public sealed class RelaxMeshComponent : GH_Component
{
    public RelaxMeshComponent()
        : base("Relax Mesh", "Relax",
               "Relax a mesh toward equilibrium using goal-based dynamic relaxation.",
               Categories.Root, Categories.FormFinding)
    {
    }

    public override Guid ComponentGuid => new("0101bcc6-97f8-4839-bb1a-12f7eaf5dccc");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap? Icon => null;   // drop a 24x24 in Resources and return it here

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Mesh to relax.", GH_ParamAccess.item);

        pManager.AddPointParameter("Anchors", "A",
            "Points to pin, snapped to the nearest vertex. Leave empty to pin the naked boundary.",
            GH_ParamAccess.list);
        pManager[1].Optional = true;

        pManager.AddNumberParameter("Rest Factor", "R",
            "Spring rest length as a fraction of the starting edge length. Below 1 contracts the mesh.",
            GH_ParamAccess.item, 0.9);

        pManager.AddNumberParameter("Strength", "S",
            "Spring strength.", GH_ParamAccess.item, 1.0);

        pManager.AddNumberParameter("Gravity", "G",
            "Downward load per free vertex. Zero for pure minimal-surface behaviour.",
            GH_ParamAccess.item, 0.0);

        pManager.AddIntegerParameter("Iterations", "I",
            "Maximum iterations. The solver stops early once it converges.",
            GH_ParamAccess.item, 1000);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Relaxed mesh.", GH_ParamAccess.item);
        pManager.AddIntegerParameter("Iterations", "I", "Iterations actually run.", GH_ParamAccess.item);
        pManager.AddNumberParameter("Residual", "R", "Largest vertex movement on the final iteration.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        Mesh? mesh = null;
        var anchors = new List<GH_Point>();
        double restFactor = 0.9;
        double strength = 1.0;
        double gravity = 0.0;
        int iterations = 1000;

        if (!da.GetData(0, ref mesh)) return;
        da.GetDataList(1, anchors);
        if (!da.GetData(2, ref restFactor)) return;
        if (!da.GetData(3, ref strength)) return;
        if (!da.GetData(4, ref gravity)) return;
        if (!da.GetData(5, ref iterations)) return;

        if (mesh is null || !mesh.IsValid)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh is null or invalid.");
            return;
        }

        if (iterations < 1)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Iterations must be at least 1.");
            return;
        }

        try
        {
            var solver = MeshRelaxation.CreateSolver(
                mesh,
                anchorPoints: anchors.Select(p => p.Value),
                restLengthFactor: restFactor,
                springStrength: strength,
                gravity: new Vector3d(0, 0, -gravity));

            solver.Step(iterations);

            if (!solver.HasConverged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Hit the iteration cap at residual {solver.Residual:0.######}. Raise Iterations to settle further.");

            da.SetData(0, MeshRelaxation.ApplyPositions(mesh, solver.Positions));
            da.SetData(1, solver.Iterations);
            da.SetData(2, solver.Residual);
        }
        catch (ArgumentException ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
