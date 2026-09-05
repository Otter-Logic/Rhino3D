using System.Drawing;
using System.Drawing.Drawing2D;

namespace OtterLogic.Rhino.UI;

/// <summary>
/// The tab icon, drawn rather than shipped.
/// <para>
/// Rhino identifies docked panels by icon alone — the caption is only a tooltip —
/// so a panel without one gets a blank tab nobody can find. Drawing a small truss
/// glyph at runtime avoids carrying a binary asset around for the sake of 32
/// pixels, and it is a mid-tone accent colour so it stays legible against both
/// the light and dark Rhino themes.
/// </para>
/// </summary>
internal static class PanelIcon
{
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

        using var bitmap = new Bitmap(32, 32);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
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

            graphics.DrawLine(pen, left, top, right, top);
            graphics.DrawLine(pen, left, bottom, right, bottom);

            // A Warren zigzag across three bays, which is what the glyph is for.
            float bay = (right - left) / 3f;
            graphics.DrawLine(pen, left, top, left, bottom);
            graphics.DrawLine(pen, left, bottom, left + bay, top);
            graphics.DrawLine(pen, left + bay, top, left + 2 * bay, bottom);
            graphics.DrawLine(pen, left + 2 * bay, bottom, right, top);
            graphics.DrawLine(pen, right, top, right, bottom);
        }

        _cached = Icon.FromHandle(bitmap.GetHicon());
        return _cached;
    }
}
