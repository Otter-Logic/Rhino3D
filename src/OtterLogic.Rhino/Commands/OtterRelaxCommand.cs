using OtterLogic.Core.FormFinding;
using OtterLogic.Rhino.Conduits;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace OtterLogic.Rhino.Commands;

/// <summary>
/// Interactive mesh relaxation.
/// <para>
/// The Rhino-side counterpart to the Grasshopper Relax Mesh component. Same
/// solver, same setup call — the difference is that this one steps once per
/// frame and redraws, so you watch the form settle and can stop it with Esc.
/// That live feedback is the whole reason to have a Rhino front-end at all.
/// </para>
/// </summary>
public sealed class OtterRelaxCommand : Command
{
    // Remembered across invocations within a session, the way Rhino commands behave.
    private static double _restLengthFactor = 0.9;
    private static double _springStrength = 1.0;
    private static double _gravity = 0.0;
    private static int _maxIterations = 2000;

    public OtterRelaxCommand() => Instance = this;

    public static OtterRelaxCommand? Instance { get; private set; }

    public override string EnglishName => "OtterRelax";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        Result pick = SelectMesh(doc, out ObjRef meshRef);
        if (pick != Result.Success) return pick;

        Mesh? mesh = meshRef.Mesh();
        if (mesh is null)
        {
            RhinoApp.WriteLine("OtterRelax: could not read that mesh.");
            return Result.Failure;
        }

        Result options = GatherOptions();
        if (options != Result.Success) return options;

        var solver = MeshRelaxation.CreateSolver(
            mesh,
            anchorPoints: null,                       // null pins the naked boundary
            restLengthFactor: _restLengthFactor,
            springStrength: _springStrength,
            gravity: new Vector3d(0, 0, -_gravity));

        Mesh relaxed = RunInteractively(doc, mesh, solver);

        doc.Objects.Replace(meshRef, relaxed);
        doc.Views.Redraw();

        RhinoApp.WriteLine(
            $"OtterRelax: {solver.Iterations} iterations, residual {solver.Residual:0.######}" +
            (solver.HasConverged ? " (converged)." : "."));

        return Result.Success;
    }

    private static Result SelectMesh(RhinoDoc doc, out ObjRef meshRef)
    {
        meshRef = null!;

        using var picker = new GetObject();
        picker.SetCommandPrompt("Select a mesh to relax");
        picker.GeometryFilter = ObjectType.Mesh;
        picker.SubObjectSelect = false;
        picker.Get();

        if (picker.CommandResult() != Result.Success)
            return picker.CommandResult();

        meshRef = picker.Object(0);
        return Result.Success;
    }

    /// <summary>Standard Rhino option loop: tweak values, Enter to run.</summary>
    private static Result GatherOptions()
    {
        var restLength = new OptionDouble(_restLengthFactor, 0.01, 5.0);
        var strength = new OptionDouble(_springStrength, 0.0001, 1e6);
        var gravity = new OptionDouble(_gravity, 0.0, 1e6);
        var iterations = new OptionInteger(_maxIterations, 1, 1_000_000);

        using var getter = new GetOption();
        getter.SetCommandPrompt("Relaxation settings — press Enter to solve");
        getter.AddOptionDouble("RestLengthFactor", ref restLength);
        getter.AddOptionDouble("SpringStrength", ref strength);
        getter.AddOptionDouble("Gravity", ref gravity);
        getter.AddOptionInteger("MaxIterations", ref iterations);
        getter.AcceptNothing(true);

        while (true)
        {
            GetResult result = getter.Get();

            if (result == GetResult.Option) continue;   // value changed, show the prompt again

            if (result != GetResult.Nothing)
                return getter.CommandResult();

            _restLengthFactor = restLength.CurrentValue;
            _springStrength = strength.CurrentValue;
            _gravity = gravity.CurrentValue;
            _maxIterations = iterations.CurrentValue;
            return Result.Success;
        }
    }

    /// <summary>
    /// Step the solver from the message loop, drawing every batch, until it
    /// converges, hits the iteration cap, or the user presses Esc.
    /// </summary>
    private static Mesh RunInteractively(RhinoDoc doc, Mesh source, Core.FormFinding.IRelaxationSolver solver)
    {
        const int stepsPerFrame = 10;

        bool cancelled = false;
        void OnEscape(object? sender, EventArgs e) => cancelled = true;

        var conduit = new RelaxationConduit { Enabled = true };
        RhinoApp.EscapeKeyPressed += OnEscape;

        Mesh current = source;

        try
        {
            while (!cancelled && !solver.HasConverged && solver.Iterations < _maxIterations)
            {
                solver.Step(stepsPerFrame);

                current = MeshRelaxation.ApplyPositions(source, solver.Positions);
                conduit.Mesh = current;

                doc.Views.Redraw();
                RhinoApp.Wait();   // pump the message loop so redraws and Esc land
            }
        }
        finally
        {
            RhinoApp.EscapeKeyPressed -= OnEscape;
            conduit.Enabled = false;
        }

        if (cancelled)
            RhinoApp.WriteLine("OtterRelax: stopped early, keeping the current state.");

        return current;
    }
}
