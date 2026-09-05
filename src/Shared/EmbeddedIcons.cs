using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace OtterLogic.Shared;

/// <summary>
/// Loads the icons embedded in whichever adaptor assembly this is compiled into.
/// <para>
/// The masters live once in <c>assets/icons</c> and are embedded by both the
/// <c>.rhp</c> and the <c>.gha</c>. This file is linked into both projects rather
/// than living in Core, because Core and the domains are not allowed to reference
/// <c>System.Drawing</c> — icons are adaptor concerns. Linking the source keeps
/// one copy of the logic without breaking that rule.
/// </para>
/// </summary>
internal static class EmbeddedIcons
{
    /// <summary>Matches the LogicalName the csproj files give the embedded PNGs.</summary>
    private const string ResourcePrefix = "OtterLogic.Icons.";

    private static readonly Dictionary<string, Bitmap?> Cache = new();

    /// <summary>
    /// The named icon, scaled to a square of <paramref name="size"/> pixels.
    /// Returns null if the resource is missing, which every caller treats as
    /// "no icon" rather than an error — a missing icon should never take a
    /// component or a panel down with it.
    /// <para>
    /// The returned bitmap is owned by this cache and shared between callers.
    /// <b>Do not dispose it.</b> Disposing would leave every later caller
    /// holding a dead handle, and the failure would surface far from the cause.
    /// </para>
    /// </summary>
    public static Bitmap? Load(string name, int size)
    {
        string key = $"{name}@{size}";

        lock (Cache)
        {
            if (Cache.TryGetValue(key, out Bitmap? cached))
                return cached;

            Bitmap? scaled = Render(name, size);
            Cache[key] = scaled;
            return scaled;
        }
    }

    private static Bitmap? Render(string name, int size)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        using Stream? stream = assembly.GetManifestResourceStream(ResourcePrefix + name + ".png");
        if (stream is null) return null;

        using var source = new Bitmap(stream);

        var scaled = new Bitmap(size, size);
        using (Graphics graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source, 0, 0, size, size);
        }

        return scaled;
    }
}
