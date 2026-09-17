using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace OtterLogic.Grasshopper.Components.Document;

/// <summary>
/// Draws <see cref="BranchPickerComponent"/> as the branch list itself: a
/// header strip with the icon and name, and under it one row per branch,
/// labelled by its path, with a tick box and how many items it holds.
/// <para>
/// The same reasoning as <see cref="LayerPickerAttributes"/> — the list is on
/// the component because the ticks are the definition, and the component
/// grows to fit the whole list rather than scrolling, since attributes get no
/// mouse wheel of their own on the canvas.
/// </para>
/// </summary>
internal sealed class BranchPickerAttributes : GH_ComponentAttributes
{
    private const int RowHeight = 18;
    private const int TickSize = 11;
    private const int IconSize = 16;
    private const int Gap = 4;
    private const int Pad = 4;

    private const int MinPanelWidth = 140;
    private const int MaxPanelWidth = 320;
    private const int MinHeaderHeight = 24;

    /// <summary>Below this the text is unreadable anyway, so only the capsule is drawn.</summary>
    private const float ReadableZoom = 0.5f;

    private Rectangle _header;
    private Rectangle _panel;

    /// <summary>
    /// The strips either side of the capsule that Grasshopper lays the port
    /// names out in. The header shares its line with them, so the title has to
    /// be inset by both or it is drawn underneath "Data".
    /// </summary>
    private int _leftInset;
    private int _rightInset;

    /// <summary>One rectangle per row, in the same order, for hit testing.</summary>
    private readonly List<Rectangle> _rowBounds = new();

    public BranchPickerAttributes(BranchPickerComponent owner) : base(owner)
    {
    }

    private BranchPickerComponent Picker => (BranchPickerComponent)Owner;

    // ----------------------------------------------------------------- layout

    protected override void Layout()
    {
        base.Layout();

        Rectangle natural = GH_Convert.ToRectangle(Bounds);
        Rectangle core = GH_Convert.ToRectangle(LayoutComponentBox(Owner));

        int leftStrip = Math.Max(core.Left - natural.Left, 0);
        int rightStrip = Math.Max(natural.Right - core.Right, 0);

        IReadOnlyList<BranchRow> rows = Picker.Rows;

        int header = Math.Max(natural.Height, MinHeaderHeight);
        int width = Math.Max(natural.Width, RequiredWidth(rows, leftStrip, rightStrip));
        int panel = Math.Max(rows.Count, 1) * RowHeight + 2 * Pad;

        var box = new Rectangle(natural.X, natural.Y, width, header + panel);
        Bounds = box;

        var iconBox = new RectangleF(
            box.X + leftStrip, box.Y, box.Width - leftStrip - rightStrip, header);

        LayoutInputParams(Owner, iconBox);
        LayoutOutputParams(Owner, iconBox);

        _leftInset = leftStrip;
        _rightInset = rightStrip;

        _header = new Rectangle(box.X, box.Y, box.Width, header);
        _panel = new Rectangle(box.X, box.Y + header, box.Width, panel);

        _rowBounds.Clear();
        for (int i = 0; i < rows.Count; i++)
            _rowBounds.Add(new Rectangle(
                _panel.X + Pad, _panel.Y + Pad + i * RowHeight, _panel.Width - 2 * Pad, RowHeight));
    }

    /// <summary>
    /// How wide the component has to be for both of the things stacked inside
    /// it: the rows, which have the full width to themselves, and the header,
    /// which has only what the port names either side leave it. Sizing to the
    /// rows alone is what let the title run back under the input name — the
    /// header is the wider of the two whenever the paths are short, which for
    /// a one-level tree they always are.
    /// </summary>
    private int RequiredWidth(IReadOnlyList<BranchRow> rows, int leftStrip, int rightStrip)
    {
        int widest = 0;

        foreach (BranchRow row in rows)
            widest = Math.Max(widest, TextWidth($"{row.Key} ({row.Count})"));

        int forRows = widest + TickSize + Gap + 2 * Pad + Gap;

        int forHeader = leftStrip + rightStrip + 2 * Pad + IconSize + Gap
            + GH_FontServer.StringWidth(Owner.NickName, GH_FontServer.StandardBold) + Gap;

        return Math.Clamp(Math.Max(forRows, forHeader), MinPanelWidth, MaxPanelWidth);
    }

    private static int TextWidth(string text) => GH_FontServer.StringWidth(text, GH_FontServer.Standard);

    // ---------------------------------------------------------------- drawing

    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (channel != GH_CanvasChannel.Objects)
        {
            base.Render(canvas, graphics, channel);
            return;
        }

        GH_Palette palette = GH_CapsuleRenderEngine.GetImpliedPalette(Owner);
        GH_PaletteStyle style =
            GH_CapsuleRenderEngine.GetImpliedStyle(palette, Selected, Owner.Locked, Owner.Hidden);

        using (GH_Capsule capsule = GH_Capsule.CreateCapsule(Bounds, palette))
        {
            foreach (IGH_Param param in Owner.Params.Output)
                capsule.AddOutputGrip(param.Attributes.InputGrip.Y);
            foreach (IGH_Param param in Owner.Params.Input)
                capsule.AddInputGrip(param.Attributes.OutputGrip.Y);

            capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

            if (!string.IsNullOrEmpty(Owner.Message))
                capsule.RenderEngine.RenderMessage(graphics, Owner.Message, style);
        }

        RenderComponentParameters(canvas, graphics, Owner, style);

        if (canvas.Viewport.Zoom < ReadableZoom) return;

        RenderHeader(graphics, style);
        RenderRows(graphics, style);
    }

    private void RenderHeader(Graphics graphics, GH_PaletteStyle style)
    {
        var content = new Rectangle(
            _header.X + _leftInset + Pad, _header.Y,
            _header.Width - _leftInset - _rightInset - 2 * Pad, _header.Height);

        Bitmap? icon = Owner.Icon_24x24;
        if (icon is not null)
        {
            var box = new Rectangle(
                content.X, content.Y + (content.Height - IconSize) / 2, IconSize, IconSize);
            graphics.DrawImage(icon, box);
            content = new Rectangle(box.Right + Gap, content.Y, content.Right - box.Right - Gap, content.Height);
        }

        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        using var text = new SolidBrush(style.Text);
        graphics.DrawString(Owner.NickName, GH_FontServer.StandardBold, text, content, format);

        using var edge = new Pen(Color.FromArgb(60, style.Edge));
        graphics.DrawLine(edge, _header.X + Pad, _header.Bottom, _header.Right - Pad, _header.Bottom);
    }

    private void RenderRows(Graphics graphics, GH_PaletteStyle style)
    {
        IReadOnlyList<BranchRow> rows = Picker.Rows;

        if (rows.Count == 0)
        {
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var faded = new SolidBrush(Color.FromArgb(140, style.Text));
            graphics.DrawString("Nothing wired in", GH_FontServer.Standard, faded, _panel, format);
            return;
        }

        using var label = new SolidBrush(style.Text);
        using var faint = new SolidBrush(Color.FromArgb(130, style.Text));
        using var highlight = new SolidBrush(Color.FromArgb(38, 0, 0, 0));
        using var format2 = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        using var countFormat = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
        };

        SmoothingMode smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        for (int i = 0; i < rows.Count; i++)
        {
            BranchRow row = rows[i];
            Rectangle bounds = _rowBounds[i];
            bool ticked = Picker.IsTicked(row);

            if (ticked)
                graphics.FillRectangle(highlight, bounds);

            int middle = bounds.Y + bounds.Height / 2;

            RenderTick(graphics, new Rectangle(bounds.X, middle - TickSize / 2, TickSize, TickSize), ticked, style);

            string count = row.Count.ToString();
            var countBox = new Rectangle(bounds.Right - 28, bounds.Y, 28, bounds.Height);
            graphics.DrawString(count, GH_FontServer.Standard, faint, countBox, countFormat);

            var text = new Rectangle(
                bounds.X + TickSize + Gap, bounds.Y, countBox.Left - (bounds.X + TickSize + Gap) - Gap, bounds.Height);
            graphics.DrawString(row.Key, GH_FontServer.Standard, label, text, format2);
        }

        graphics.SmoothingMode = smoothing;
    }

    private static void RenderTick(Graphics graphics, Rectangle box, bool ticked, GH_PaletteStyle style)
    {
        using (var fill = new SolidBrush(ticked ? Color.White : Color.FromArgb(70, Color.White)))
            graphics.FillRectangle(fill, box);

        using (var edge = new Pen(Color.FromArgb(190, style.Edge)))
            graphics.DrawRectangle(edge, box);

        if (!ticked) return;

        using var pen = new Pen(style.Edge, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        graphics.DrawLines(pen, new PointF[]
        {
            new(box.Left + 2.5f, box.Top + box.Height * 0.55f),
            new(box.Left + box.Width * 0.42f, box.Bottom - 2.5f),
            new(box.Right - 2f, box.Top + 2.5f),
        });
    }

    // ------------------------------------------------------------------ mouse

    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        => Click(sender, e) ?? base.RespondToMouseDown(sender, e);

    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        => Click(sender, e) ?? base.RespondToMouseDoubleClick(sender, e);

    private GH_ObjectResponse? Click(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button != MouseButtons.Left) return null;
        if (!_panel.Contains(GH_Convert.ToPoint(e.CanvasLocation))) return null;

        IReadOnlyList<BranchRow> rows = Picker.Rows;

        for (int i = 0; i < rows.Count && i < _rowBounds.Count; i++)
        {
            if (!_rowBounds[i].Contains(GH_Convert.ToPoint(e.CanvasLocation))) continue;

            Picker.Toggle(rows[i]);
            sender.Refresh();
            return GH_ObjectResponse.Handled;
        }

        return GH_ObjectResponse.Handled;
    }
}
