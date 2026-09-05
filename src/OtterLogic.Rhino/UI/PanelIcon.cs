using System.Drawing;
using System.Drawing.Drawing2D;
using OtterLogic.Shared;

namespace OtterLogic.Rhino.UI;

/// <summary>
/// The panel tab icon.
/// <para>
/// Rhino identifies docked panels by icon alone — the caption is only a
/// tooltip — so a panel registered without one gets a blank tab nobody can
/// find. This uses the shared OtterLogic mark, falling back to a drawn glyph if
/// the embedded resource is ever missing, because a blank tab is worse than an
/// ugly one.
/// </para>
/// </summary>
internal static class PanelIcon
{
    private const int Size = 32;

    private static Icon? _cached;

    /// <summary>
    /// The panel icon, created once per session.
    /// <para>
    /// <see cref="Icon.FromHandle"/> hands back an icon wrapping an unmanaged
    /// handle that it will not free. Caching one for the life of the process is
    /// the simple correct answer; creating them per call would leak.
    /// </para>
    /// </summary>
    public static Icon Create()
    {
        if (_cached is not null) return _cached;

        // Shared, so it must not be disposed. The fallback is ours alone, so it
        // must be. Keeping the two apart is the whole reason for the local.
        Bitmap? shared = EmbeddedIcons.Load("otterlogic", Size);
        Bitmap? owned = shared is null ? DrawFallback() : null;

        try
        {
            _cached = Icon.FromHandle((shared ?? owned)!.GetHicon());
            return _cached;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>A truss glyph, used only if the embedded mark cannot be loaded.</summary>
    private static Bitmap DrawFallback()
    {
        var bitmap = new Bitmap(Size, Size);

        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var pen = new Pen(Color.FromArgb(255, 82, 148, 200), 2.0f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        const float top = 9f;
        const float bottom = 23f;
        const float left = 4f;
        const float right = 28f;
        float bay = (right - left) / 3f;

        graphics.DrawLine(pen, left, top, right, top);
        graphics.DrawLine(pen, left, bottom, right, bottom);
        graphics.DrawLine(pen, left, top, left, bottom);
        graphics.DrawLine(pen, left, bottom, left + bay, top);
        graphics.DrawLine(pen, left + bay, top, left + 2 * bay, bottom);
        graphics.DrawLine(pen, left + 2 * bay, bottom, right, top);
        graphics.DrawLine(pen, right, top, right, bottom);

        return bitmap;
    }
}
