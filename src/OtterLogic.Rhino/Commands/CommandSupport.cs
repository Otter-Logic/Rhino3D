using System.Drawing;
using OtterLogic.Core;
using OtterLogic.StructuralForm;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace OtterLogic.Rhino.Commands;

/// <summary>
/// Picking, shared by the commands that start from curves in the document.
/// <para>
/// Lifted out of OtterFlatTruss when a second command needed the same picker:
/// the pre-select handling below is the kind of thing that gets fixed in one
/// copy and not the other.
/// </para>
/// </summary>
internal static class Pick
{
    internal static Result Curves(RhinoDoc doc, string prompt, out Curve[] curves, int maximum = 0)
    {
        curves = Array.Empty<Curve>();

        // GetObject honours the current selection by default, so without this a
        // second call would silently return the curves just picked instead of
        // prompting for more. Clearing the selection as well keeps a
        // walkthrough readable: exactly one set is highlighted at a time.
        doc.Objects.UnselectAll();
        doc.Views.Redraw();

        using var picker = new GetObject();
        picker.SetCommandPrompt(prompt);
        picker.GeometryFilter = ObjectType.Curve;
        picker.SubObjectSelect = false;
        picker.EnablePreSelect(false, true);
        picker.DeselectAllBeforePostSelect = true;

        if (picker.GetMultiple(1, maximum) != GetResult.Object)
            return picker.CommandResult();

        // OfType rather than a cast: the geometry filter already guarantees
        // curves, so this only sweeps up anything whose geometry failed to load.
        curves = picker.Objects().Select(o => o.Curve()).OfType<Curve>().ToArray();

        return Result.Success;
    }

    /// <summary>
    /// Offers an enum's members as clickable command-line options.
    /// <para>
    /// Generic because the commands ask several of these, and a second
    /// hand-rolled copy is how the two come to disagree about how a choice is
    /// spelled. The readable names come from Core, so the Grasshopper
    /// right-click menu reads identically.
    /// </para>
    /// </summary>
    internal static Result Enum<T>(string label, ref T value) where T : struct, System.Enum
    {
        var values = System.Enum.GetValues<T>();

        using var getter = new GetOption();

        // The prompt reads the name the way Grasshopper's menu does; the options
        // themselves keep the bare enum name, because a command-line option
        // cannot contain a space.
        getter.SetCommandPrompt($"{label} <{Naming.Humanise(value)}>");

        var indices = new int[values.Length];
        for (int i = 0; i < values.Length; i++)
            indices[i] = getter.AddOption(values[i].ToString());

        getter.AcceptNothing(true);   // Enter keeps the remembered value

        GetResult result = getter.Get();

        if (result == GetResult.Nothing)
            return Result.Success;

        if (result != GetResult.Option)
            return getter.CommandResult();

        int chosen = getter.Option().Index;
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] != chosen) continue;
            value = values[i];
            break;
        }

        return Result.Success;
    }

    internal static Result SnapPoints(out Point3d[] points)
    {
        points = Array.Empty<Point3d>();

        using var picker = new GetObject();
        picker.SetCommandPrompt("Select additional snap points, or press Enter for none");
        picker.GeometryFilter = ObjectType.Point;
        picker.SubObjectSelect = false;
        picker.AcceptNothing(true);
        picker.EnablePreSelect(false, true);

        GetResult result = picker.GetMultiple(0, 0);

        if (result == GetResult.Nothing)
            return Result.Success;

        if (result != GetResult.Object)
            return picker.CommandResult();

        points = picker.Objects()
            .Select(o => o.Point()?.Location)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .ToArray();

        return Result.Success;
    }
}

/// <summary>
/// Typed answers, shared by the commands that start from numbers rather than
/// from geometry in the document.
/// <para>
/// Lifted out when the second grid command needed the same two questions as
/// the first. The spacing prompt in particular re-asks on a typo instead of
/// failing the command, and that is the kind of thing that gets fixed in one
/// copy and not the other.
/// </para>
/// </summary>
internal static class Ask
{
    /// <summary>
    /// A point, with Enter taking <paramref name="fallback"/>. For an origin
    /// or a centre, where the construction plane's origin is a fine answer and
    /// making the user click it would be a click for nothing.
    /// </summary>
    internal static Result Point(string prompt, Point3d fallback, out Point3d point)
    {
        point = fallback;

        using var getter = new GetPoint();
        getter.SetCommandPrompt($"{prompt}, or press Enter for the construction plane origin");
        getter.AcceptNothing(true);

        GetResult result = getter.Get();

        if (result == GetResult.Nothing)
            return Result.Success;

        if (result != GetResult.Point)
            return getter.CommandResult();

        point = getter.Point();
        return Result.Success;
    }

    /// <summary>
    /// A run of bay spacings typed as text, in the notation
    /// <see cref="Spacings"/> reads: <c>6000</c> or <c>3x6000, 8000</c>. The
    /// remembered value is the default, shown in the collapsed form it was
    /// typed in; a string that does not parse is explained and asked again
    /// rather than ending the command, because a typo in a list is the
    /// commonest mistake here and Esc is still one key away.
    /// </summary>
    internal static Result Bays(string prompt, ref IReadOnlyList<double> value)
    {
        while (true)
        {
            string text = Spacings.Describe(value);

            Result step = RhinoGet.GetString($"{prompt} (6000, or 3x6000, 8000)", true, ref text);
            if (step != Result.Success) return step;

            if (Spacings.TryParse(text, out IReadOnlyList<double> parsed, out string? error))
            {
                value = parsed;
                return Result.Success;
            }

            RhinoApp.WriteLine(error);
        }
    }
}

/// <summary>
/// The layer tree a command bakes one run onto: a numbered root, and a
/// sub-layer per kind of thing under it.
/// </summary>
internal static class RunLayers
{
    /// <summary>
    /// The name for this run's root layer: the prefix with 1 appended the first
    /// time, then 2, and so on.
    /// <para>
    /// One name per run. Numbering follows the highest number already in the
    /// document rather than a count, so deleting run 2 does not make the next
    /// run reuse that name and merge into what is left of it.
    /// </para>
    /// </summary>
    internal static string NextName(RhinoDoc doc, string prefix)
    {
        int highest = 0;

        foreach (Layer layer in doc.Layers)
        {
            // Root layers only: a sub-layer with a matching name inside someone
            // else's tree is their business, not a run of this command.
            if (layer.IsDeleted || layer.ParentLayerId != Guid.Empty) continue;
            if (!layer.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            if (int.TryParse(layer.Name[prefix.Length..], out int number))
                highest = Math.Max(highest, number);
        }

        return $"{prefix}{highest + 1}";
    }

    /// <summary>Adds one layer, under <paramref name="parent"/> when given. -1 if it could not be created.</summary>
    internal static int Add(RhinoDoc doc, string name, Guid parent, Color colour)
        => doc.Layers.Add(new Layer { Name = name, Color = colour, ParentLayerId = parent });

    /// <summary>
    /// A sub-layer of the run's layer, falling back to the run's layer itself.
    /// <para>
    /// The fallback should never fire — the parent was created moments ago, so
    /// every name under it is free — but the alternative to checking is baking
    /// with <c>LayerIndex = -1</c>, which quietly puts geometry on a layer
    /// nobody chose. Landing one level up is findable; landing anywhere is not.
    /// </para>
    /// </summary>
    internal static int Sub(RhinoDoc doc, string command, string name, Guid parent, Color colour, int fallback)
    {
        int index = Add(doc, name, parent, colour);
        if (index >= 0) return index;

        RhinoApp.WriteLine($"{command}: could not create the {name} sub-layer, so those objects went one level up.");
        return fallback;
    }

    internal static ObjectAttributes Attributes(string name, int layerIndex)
        => new() { Name = name, LayerIndex = layerIndex };
}
