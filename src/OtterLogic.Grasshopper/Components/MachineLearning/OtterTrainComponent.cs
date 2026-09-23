using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OtterLogic.Grasshopper.Parameters.MachineLearning;
using OtterLogic.MachineLearning.Data;
using OtterLogic.MachineLearning.Training;

namespace OtterLogic.Grasshopper.Components.MachineLearning;

/// <summary>
/// Trains a model on the samples wired in and writes it as one <c>.onnx</c> file.
/// <para>
/// Adaptor only. The dataset, the job, the process and the progress protocol
/// belong to <see cref="Dataset"/> and <see cref="TrainerProcess"/>; the training
/// itself runs in a separate Python process, so the canvas never waits on it.
/// What this adds is the edge-driven Run toggle and the polling: a job starts when
/// Run goes on, is cancelled when it goes off, and never restarts because something
/// upstream changed. While it runs the component re-solves itself every half second
/// to show Status, which is the Grasshopper way of watching something without
/// blocking it. The runtime install from the right-click menu runs the same way —
/// on a background task, reporting through Status — because a download on the UI
/// thread is a frozen Rhino for as long as it takes.
/// </para>
/// </summary>
public sealed class OtterTrainComponent : GH_Component
{
    private const int InputsInput = 0;
    private const int TargetInput = 1;
    private const int TargetNameInput = 2;
    private const int NamesInput = 3;
    private const int GroupsInput = 4;
    private const int LearnerInput = 5;
    private const int HoldoutInput = 6;
    private const int ModelInput = 7;
    private const int RunInput = 8;

    private const int PollMilliseconds = 500;

    private const string NotInstalled =
        "The training runtime is not installed. Right-click the component to install it.";

    private TrainerProcess? _run;
    private TrainerProgress? _result;
    private bool _lastRun;

    /// <summary>Said on every solve until the next Run, so it outlives the polling re-solves that clear messages.</summary>
    private string? _remark;

    private CancellationTokenSource? _installCancel;
    private volatile InstallState? _install;

    public OtterTrainComponent()
        : base("OtterTrain", "Train",
               "Train a model on these samples and write it as one .onnx file that OtterPredict, on any "
               + "machine with OtterLogic, can run.\n\n"
               + "Wire Inputs, one branch per sample, and Target, the known answer for each. Whether it "
               + "learns a class or a number comes from what is on the Target wire: numbers train a "
               + "number model, anything else is read as class names. Classes are very often written 0 "
               + "and 1 — wired as numbers those train a model that answers 0.37, so write classes as "
               + "text. Wire Groups, the model each sample came from, and whole groups are held back to "
               + "score on; without them rows are held back at random and the score may be optimistic. "
               + "Boosted Trees is fitted unless another learner is wired.\n\n"
               + "Training runs in a separate process and the canvas stays live; Status says what it is "
               + "doing. The first time, right-click to install the training runtime.",
               Categories.Root, Categories.MachineLearning)
    {
    }

    public override Guid ComponentGuid => new("749cac94-f446-4da6-9c02-9259d6bc421c");

    // The cores tier, beside OtterCluster and OtterPredict.
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override IEnumerable<string> Keywords
        => new[] { "train", "training", "fit", "model", "onnx", "supervised", "regression", "classification" };

    protected override Bitmap? Icon => EmbeddedIcons.Load("ottertrain", 24);

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddNumberParameter("Inputs", "X",
            TrainingData.Description + "\n\nMake them independent of the model's size and of the software "
            + "it came from — ratios, angles, counts — or what is learned will not carry to the next model.",
            GH_ParamAccess.tree);

        pManager.AddGenericParameter("Target", "Y",
            "The known answer for every sample, in the same order as Inputs: a flat list, or one branch "
            + "per sample.\n\n"
            + "What is on the wire decides what is learned. If every item is a number, the model learns "
            + "a quantity; otherwise the items are read as text and the model learns to choose between "
            + "those classes. So 0 and 1 wired as numbers train a number model that answers 0.37 — write "
            + "classes as text, even when they look like numbers.",
            GH_ParamAccess.tree);

        pManager.AddTextParameter("Target Name", "TN",
            "What the answer is called. Written into the model, so OtterPredict can say what it predicts.",
            GH_ParamAccess.item, "Target");

        pManager.AddTextParameter("Feature Names", "FN",
            "Optional. One name per value in a sample, in branch order; Feature 0, Feature 1, ... when "
            + "left out. Written into the model, where they are the only thing that can catch two inputs "
            + "being wired the wrong way round six months from now.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Groups", "G",
            "Optional. Which model each sample came from — Read Dataset's Groups, or a job number per "
            + "sample.\n\n"
            + "When given, whole groups are held back for the score, so it is a score on projects the fit "
            + "never saw. When not, rows are held back at random; samples from one model are near-copies "
            + "of each other, so that score may be optimistic, and the Report says so.",
            GH_ParamAccess.list);

        pManager.AddParameter(new LearnerParameter(), "Learner", "L",
            "Wire Boosted Trees, Neural Network, Linear Model or Nearest Neighbours. With nothing wired, "
            + "Boosted Trees is fitted — usually the most accurate on a table of a few thousand rows, and "
            + "unbothered by scale or by a useless feature.",
            GH_ParamAccess.item);

        pManager.AddNumberParameter("Holdout", "H",
            "Share of the groups — or of the rows, without groups — held back to score on, between 0 and "
            + "1. A quarter by default: enough that the score is not one project's luck, without starving "
            + "the fit.",
            GH_ParamAccess.item, 0.25);

        pManager.AddTextParameter("Model", "M",
            "Where to write the model, ending in .onnx. Written beside and renamed over, so OtterPredict "
            + "never sees half a file.",
            GH_ParamAccess.item);

        pManager.AddBooleanParameter("Run", "R",
            "Training starts when this goes on and is cancelled when it goes off. Nothing upstream "
            + "restarts it — toggle it again to train again.",
            GH_ParamAccess.item, false);

        pManager[NamesInput].Optional = true;
        pManager[GroupsInput].Optional = true;
        pManager[LearnerInput].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Model", "M", "The file written, once training ends.", GH_ParamAccess.item);

        pManager.AddTextParameter("Report", "Rp",
            "The score on the held-out samples, the confusion matrix or the residuals, and what was used. "
            + "The same score is written into the model, so OtterPredict shows it too.",
            GH_ParamAccess.list);

        pManager.AddTextParameter("Status", "S", "What the trainer, or the runtime install, is doing now.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(InputsInput, out GH_Structure<GH_Number> inputs)) return;
        if (!da.GetDataTree(TargetInput, out GH_Structure<IGH_Goo> target)) return;

        string targetName = "Target";
        var featureNames = new List<string>();
        var groups = new List<string>();
        double holdout = 0.25;
        string output = string.Empty;
        bool run = false;

        if (!da.GetData(TargetNameInput, ref targetName)) return;
        da.GetDataList(NamesInput, featureNames);
        da.GetDataList(GroupsInput, groups);
        Learner? learner = MethodWire.ReadLearner(da, LearnerInput);
        if (!da.GetData(HoldoutInput, ref holdout)) return;
        if (!da.GetData(ModelInput, ref output)) return;
        if (!da.GetData(RunInput, ref run)) return;

        bool rising = run && !_lastRun;
        bool falling = !run && _lastRun;
        _lastRun = run;

        if (rising)
            Start(inputs, target, targetName, featureNames, groups, learner ?? new BoostedTreesLearner(), holdout, output);
        else if (falling && _run is { IsRunning: true })
            Stop("Cancelled.");

        if (_remark is not null)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, _remark);

        InstallState? install = _install;
        if (install?.Error is not null)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, install.Error);

        if (_run is not null)
        {
            var progress = _run.Poll();
            if (!progress.Finished)
            {
                Message = progress.Stage ?? "running";
                da.SetData(2, WithInstall(progress.Message ?? progress.Stage, install));
                OnPingDocument()?.ScheduleSolution(PollMilliseconds, _ => ExpireSolution(false));
                return;
            }

            _result = progress;
            _run.Dispose();
            _run = null;
        }

        if (_result is null)
        {
            Message = install?.Running == true ? "installing" : "idle";
            da.SetData(2, WithInstall(TrainerRuntime.Find() is null ? NotInstalled : "Switch Run on to train.", install));
            return;
        }

        if (_result.Error is not null)
        {
            Message = "failed";
            da.SetData(2, WithInstall("Failed.", install));
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _result.Error);
            return;
        }

        Message = "done";
        da.SetData(0, _result.Model);
        da.SetDataList(1, _result.Report ?? Array.Empty<string>());
        da.SetData(2, WithInstall("Done.", install));
    }

    /// <summary>Status with the install's latest line after it, while there is one to show.</summary>
    private static string? WithInstall(string? status, InstallState? install)
        => install is null || install.Error is not null ? status : $"{status}\n{install.Text}";

    private void Start(
        GH_Structure<GH_Number> inputs, GH_Structure<IGH_Goo> target, string targetName,
        List<string> featureNames, List<string> groups, Learner learner, double holdout, string output)
    {
        Stop(null);
        _result = null;
        _remark = null;

        if (!TrainingData.TryRead(inputs, out double[,] features, out string? problem))
        {
            Fail("Inputs: " + problem);
            return;
        }

        int samples = features.GetLength(0);
        int columns = features.GetLength(1);

        if (!TryReadTarget(target, samples, out string[,] targets, out bool isNumber, out problem))
        {
            Fail("Target: " + problem);
            return;
        }

        if (featureNames.Count == 0)
            featureNames = Enumerable.Range(0, columns).Select(j => $"Feature {j}").ToList();
        else if (featureNames.Count != columns)
        {
            Fail($"Feature Names has {featureNames.Count} name(s) and each sample holds {columns} value(s). "
                + "Give one per value, or none.");
            return;
        }

        string[]? groupOf = null;
        if (groups.Count > 0)
        {
            if (groups.Count != samples)
            {
                Fail($"Groups has {groups.Count} value(s) for {samples} samples. Give one per sample, or none.");
                return;
            }

            if (!TextData.TryReadList(groups, "Groups", out groupOf, out problem))
            {
                Fail(problem);
                return;
            }
        }

        string? python = TrainerRuntime.Find();
        if (python is null)
        {
            Fail(NotInstalled + " " + TrainerRuntime.Describe());
            return;
        }

        try
        {
            var dataset = Dataset.FromColumns(
                featureNames.Select(n => (n ?? string.Empty).Trim()).ToArray(), features,
                new[] { targetName.Trim() }, targets, new[] { isNumber });

            _run = TrainerProcess.StartOnSamples(dataset, groupOf, learner, holdout, output, python);

            // Said here rather than left to the Report, because the Report arrives
            // minutes later and the person is deciding now whether to wire Groups.
            _remark = groupOf is null
                ? "No Groups wired, so rows are held back at random to score on. Samples from one model are "
                  + "near-copies of each other, so the score may be optimistic. Wire Groups for an honest one."
                : isNumber
                    ? $"Target is a number wire, so the model learns a quantity called '{targetName}'."
                    : $"Target is read as class names, so the model learns to choose a '{targetName}'.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException
                                       or UnauthorizedAccessException)
        {
            Fail(ex.Message);
        }
    }

    /// <summary>
    /// One answer per sample off a generic wire, in either of the two shapes a
    /// person will wire: a flat list, or one branch per sample matching Inputs.
    /// <para>
    /// Numbers only when every item is a number. A single piece of text among
    /// numbers makes the whole column classes, because a class column that happens
    /// to contain "3" is far more common than a number column that happens to
    /// contain a word — and a wrong guess the other way trains a model that answers
    /// 0.37 for a yes-or-no question.
    /// </para>
    /// </summary>
    private static bool TryReadTarget(
        GH_Structure<IGH_Goo> tree, int samples, out string[,] values, out bool isNumber, out string? problem)
    {
        values = new string[0, 0];
        isNumber = false;
        problem = null;

        var branches = tree.Branches;
        IList<IGH_Goo?> items;
        if (branches.Count == 1 && branches[0].Count == samples)
            items = branches[0].Cast<IGH_Goo?>().ToList();
        else if (branches.Count == samples && branches.All(b => b.Count == 1))
            items = branches.Select(b => (IGH_Goo?)b[0]).ToList();
        else
        {
            problem = $"{tree.DataCount} value(s) in {branches.Count} branch(es) for {samples} samples. Give one "
                + "answer per sample: a flat list, or one branch per sample in the same order as Inputs.";
            return false;
        }

        isNumber = items.All(item => item is GH_Number or GH_Integer);
        values = new string[samples, 1];

        for (int i = 0; i < samples; i++)
        {
            string? text = items[i] switch
            {
                null => null,
                GH_Number number => number.Value.ToString("R", CultureInfo.InvariantCulture),
                GH_Integer integer => integer.Value.ToString(CultureInfo.InvariantCulture),
                GH_String label => label.Value,
                var other => other.ToString(),
            };

            if (string.IsNullOrWhiteSpace(text))
            {
                problem = $"Blank at position {i}. Every sample needs an answer, and skipping it would pair "
                    + "every later one with the wrong sample.";
                return false;
            }

            values[i, 0] = text.Trim();
        }

        return true;
    }

    /// <summary>A failure to start, shown the way a failed run is: an Error bubble and a "Failed." Status.</summary>
    private void Fail(string? message) => _result = new TrainerProgress { Error = message ?? "Failed." };

    private void Stop(string? status)
    {
        if (_run is null) return;
        _run.Cancel();
        if (status is not null)
            _result = new TrainerProgress { Error = status };
        _run.Dispose();
        _run = null;
    }

    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalMenuItems(menu);
        Menu_AppendSeparator(menu);

        bool installing = _install?.Running == true;
        Menu_AppendItem(menu, "Install training runtime…", (_, _) => Install(null), !installing);
        Menu_AppendItem(menu, "Install training runtime from file…", (_, _) => InstallFromFile(), !installing);
        if (installing)
            Menu_AppendItem(menu, "Cancel the install", (_, _) => _installCancel?.Cancel());
        Menu_AppendItem(menu, "Where the runtime is looked for…", (_, _) => ShowRuntime());
    }

    /// <summary>Picks a bundle zip and installs it — for a machine that cannot reach GitHub.</summary>
    private void InstallFromFile()
    {
        // global:: because inside Components.MachineLearning a bare Rhino.X would
        // look for OtterLogic.Rhino first, which is the namespace trap this repo
        // has been caught by before.
        var dialog = new global::Rhino.UI.OpenFileDialog
        {
            Title = "Training runtime bundle",
            Filter = "Runtime bundle (*.zip)|*.zip|All files (*.*)|*.*",
        };

        if (dialog.ShowOpenDialog() && !string.IsNullOrWhiteSpace(dialog.FileName))
            Install(dialog.FileName);
    }

    /// <summary>
    /// Installs the runtime on a background task, reporting through Status.
    /// <para>
    /// Never on the UI thread: the download is hundreds of megabytes, and Rhino
    /// frozen for that long reads as a crash. Progress lands as re-solves, which is
    /// the only way an output can change, and the finish is marshalled back to the
    /// UI thread because a document is not to be touched from any other.
    /// </para>
    /// </summary>
    /// <param name="zip">A bundle already on disk, or null to download the release's.</param>
    private void Install(string? zip)
    {
        _installCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _installCancel = cancel;

        var state = new InstallState { Running = true, Text = "Starting the install." };
        _install = state;

        var progress = new Progress<string>(text =>
        {
            state.Text = text;
            Resolve();
        });

        Task.Run(async () =>
        {
            try
            {
                string python = zip is null
                    ? await TrainerRuntime.InstallAsync(progress, cancel.Token).ConfigureAwait(false)
                    : TrainerRuntime.InstallFromFile(zip, progress, cancel.Token);
                state.Text = $"Training runtime installed: '{python}'.";
            }
            catch (OperationCanceledException)
            {
                state.Text = "The install was cancelled.";
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException
                                           or UnauthorizedAccessException)
            {
                state.Error = "The training runtime could not be installed. " + ex.Message;
            }
            finally
            {
                state.Running = false;
                if (ReferenceEquals(_installCancel, cancel))
                    _installCancel = null;
                cancel.Dispose();
                Resolve();
            }
        });

        Resolve();
    }

    /// <summary>Re-solves this component from whichever thread asks, so Status catches up.</summary>
    private void Resolve()
    {
        global::Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
            OnPingDocument()?.ScheduleSolution(1, _ => ExpireSolution(false))));
    }

    private static void ShowRuntime()
    {
        string text = TrainerRuntime.Describe() + "\n\n"
            + "Install training runtime downloads the current bundle from the MachineLearning release and "
            + $"unpacks it under '{TrainerRuntime.InstallRoot}'. To train against a checkout instead, set "
            + $"the environment variable {TrainerRuntime.EnvironmentVariable} to a Python virtual "
            + "environment with the otterlogic-trainer package installed, then restart Rhino.";

        global::Rhino.UI.Dialogs.ShowMessage(text, "Training runtime");
    }

    public override void RemovedFromDocument(GH_Document document)
    {
        Stop(null);
        _installCancel?.Cancel();
        base.RemovedFromDocument(document);
    }

    /// <summary>
    /// What the install has to say, shared between the background task that writes
    /// it and the solve that reads it. Each field is a reference or a bool, so a
    /// read sees a whole value; the class itself is swapped in atomically.
    /// </summary>
    private sealed class InstallState
    {
        public volatile bool Running;
        public volatile string Text = string.Empty;
        public volatile string? Error;
    }
}
