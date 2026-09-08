using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using OtterLogic.Document;

namespace OtterLogic.Grasshopper.Components.Document;

/// <summary>
/// Draws <see cref="LayerPickerComponent"/> as the layer tree itself: a header
/// strip with the icon and name, and under it one row per layer with a tick box,
/// indented the way the Rhino layer panel indents it.
/// <para>
/// The list is on the component rather than behind a right-click menu because
/// the ticks <em>are</em> the definition. Anyone opening the file has to be able
/// to see which layers feed the sections without clicking anything, the same way
/// a number slider shows its number.
/// </para>
/// <para>
/// The component grows to fit the whole list rather than scrolling inside a
/// fixed frame. Grasshopper gives attributes no mouse wheel of its own — the
/// canvas takes it for zoom — so a scrolling list would be one that can only be
/// dragged, which is worse than a tall component on a canvas that already
/// scrolls. Folding a branch away is the answer to a long list, and it is a
/// click on the arrow.
/// </para>
/// </summary>
internal sealed class LayerPickerAttributes : GH_ComponentAttributes
{
    private const int RowHeight = 18;
    private const int Indent = 12;
    private const int TickSize = 11;
    private const int ArrowSlot = 11;
    private const int SwatchSize = 8;
    private const int Gap = 4;
    private const int Pad = 4;

    private const int MinPanelWidth = 130;
    private const int MaxPanelWidth = 300;
    private const int MinHeaderHeight = 24;

    /// <summary>Below this the text is unreadable anyway, so only the capsule is drawn.</summary>
    private const float ReadableZoom = 0.5f;

    private Rectangle _header;
    private Rectangle _panel;

    /// <summary>
    /// How much of the header's right-hand side the output port's name strip
    /// takes up, so the component's own name never runs underneath it.
    /// </summary>
    private int _portInset;

    /// <summary>One rectangle per visible row, in the same order, for hit testing.</summary>
    private readonly List<Rectangle> _rowBounds = new();

    public LayerPickerAttributes(LayerPickerComponent owner) : base(owner)
    {
    }

    private LayerPickerComponent Picker => (LayerPickerComponent)Owner;

    // ----------------------------------------------------------------- layout

    protected override void Layout()
    {
        // The natural box first, so the component is never narrower or shorter
        // than its own output nubbin needs.
        base.Layout();

        // Two rectangles come out of that, and both are needed. The base lays
        // the ports out on either side of the icon box and then takes Bounds as
        // the union of the three, so the port names live in strips that sit
        // outside the icon box but inside the capsule. Handing the whole
        // widened box back as if it were the icon box is what pushes a port
        // name off the end of the capsule, wire grip and all.
        Rectangle natural = GH_Convert.ToRectangle(Bounds);
        Rectangle core = GH_Convert.ToRectangle(LayoutComponentBox(Owner));

        int leftStrip = Math.Max(core.Left - natural.Left, 0);
        int rightStrip = Math.Max(natural.Right - core.Right, 0);

        IReadOnlyList<LayerNode> rows = Picker.VisibleRows;

        int header = Math.Max(natural.Height, MinHeaderHeight);
        int width = Math.Max(natural.Width, PanelWidth(rows));
        int panel = Math.Max(rows.Count, 1) * RowHeight + 2 * Pad;

        var box = new Rectangle(natural.X, natural.Y, width, header + panel);
        Bounds = box;

        // Widened by the same amount the capsule was, and only across the
        // header, so the ports keep their strips against the capsule edge
        // instead of being centred down the side of a twenty-row list.
        var iconBox = new RectangleF(
            box.X + leftStrip, box.Y, box.Width - leftStrip - rightStrip, header);

        LayoutInputParams(Owner, iconBox);
        LayoutOutputParams(Owner, iconBox);

        _portInset = rightStrip;

        _header = new Rectangle(box.X, box.Y, box.Width, header);
        _panel = new Rectangle(box.X, box.Y + header, box.Width, panel);

        _rowBounds.Clear();
        for (int i = 0; i < rows.Count; i++)
            _rowBounds.Add(new Rectangle(
                _panel.X + Pad, _panel.Y + Pad + i * RowHeight, _panel.Width - 2 * Pad, RowHeight));
    }

    private static int PanelWidth(IReadOnlyList<LayerNode> rows)
    {
        int widest = 0;

        foreach (LayerNode row in rows)
            widest = Math.Max(widest, TextLeft(row.Depth) + TextWidth(row.Name));

        return Math.Clamp(widest + 2 * Pad + Gap, MinPanelWidth, MaxPanelWidth);
    }

    /// <summary>Where a row's text starts, measured from the left of the row.</summary>
    private static int TextLeft(int depth)
        => depth * Indent + ArrowSlot + TickSize + Gap + SwatchSize + Gap;

    private static int TextWidth(string text)
        => GH_FontServer.StringWidth(text, GH_FontServer.Standard);

    // ---------------------------------------------------------------- drawing

    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
    {
        if (channel != GH_CanvasChannel.Objects)
        {
            base.Render(canvas, graphics, channel);
            return;
        }

        // The capsule is drawn here rather than by the base class because the
        // base centres the icon in the whole box, which for this component is
        // the middle of the list. Taking the palette from the same place the
        // base does keeps every other cue — selected, locked, warning, error —
        // exactly as Grasshopper draws it elsewhere.
        GH_Palette palette = GH_CapsuleRenderEngine.GetImpliedPalette(Owner);
        GH_PaletteStyle style =
            GH_CapsuleRenderEngine.GetImpliedStyle(palette, Selected, Owner.Locked, Owner.Hidden);

        using (GH_Capsule capsule = GH_Capsule.CreateCapsule(Bounds, palette))
        {
            foreach (IGH_Param param in Owner.Params.Output)
                capsule.AddOutputGrip(param.Attributes.InputGrip.Y);

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
            _header.X + Pad, _header.Y, _header.Width - 2 * Pad - _portInset, _header.Height);

        Bitmap? icon = Owner.Icon_24x24;
        if (icon is not null)
        {
            var box = new Rectangle(content.X, content.Y + (content.Height - 16) / 2, 16, 16);
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
        IReadOnlyList<LayerNode> rows = Picker.VisibleRows;

        if (rows.Count == 0)
        {
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            using var faded = new SolidBrush(Color.FromArgb(140, style.Text));
            graphics.DrawString("No document open", GH_FontServer.Standard, faded, _panel, format);
            return;
        }

        using var label = new SolidBrush(style.Text);
        using var highlight = new SolidBrush(Color.FromArgb(38, 0, 0, 0));
        using var format2 = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        SmoothingMode smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        for (int i = 0; i < rows.Count; i++)
        {
            LayerNode row = rows[i];
            Rectangle bounds = _rowBounds[i];
            bool ticked = Picker.IsTicked(row);

            if (ticked)
                graphics.FillRectangle(highlight, bounds);

            int left = bounds.X + row.Depth * Indent;
            int middle = bounds.Y + bounds.Height / 2;

            if (row.HasChildren)
                RenderArrow(graphics, new Rectangle(left, middle - 4, 8, 8),
                            Picker.IsCollapsed(row), Picker.HidesTicks(row), style);

            RenderTick(graphics, new Rectangle(left + ArrowSlot, middle - TickSize / 2, TickSize, TickSize),
                       ticked, style);

            var swatch = new Rectangle(
                left + ArrowSlot + TickSize + Gap, middle - SwatchSize / 2, SwatchSize, SwatchSize);
            RenderSwatch(graphics, swatch, Color.FromArgb(row.Argb));

            var text = new Rectangle(
                swatch.Right + Gap, bounds.Y, bounds.Right - swatch.Right - Gap, bounds.Height);

            graphics.DrawString(row.Name, GH_FontServer.Standard, label, text, format2);
        }

        graphics.SmoothingMode = smoothing;
    }

    /// <summary>
    /// The fold arrow: pointing down when the branch is open, right when it is
    /// folded. A folded branch with ticks inside it draws the arrow filled, so a
    /// tidied-away layer never disappears from the output unannounced.
    /// </summary>
    private static void RenderArrow(
        Graphics graphics, Rectangle bounds, bool collapsed, bool hidesTicks, GH_PaletteStyle style)
    {
        PointF[] arrow = collapsed
            ? new PointF[]
            {
                new(bounds.Left + 1, bounds.Top),
                new(bounds.Right - 2, bounds.Top + bounds.Height / 2f),
                new(bounds.Left + 1, bounds.Bottom),
            }
            : new PointF[]
            {
                new(bounds.Left, bounds.Top + 1),
                new(bounds.Right, bounds.Top + 1),
                new(bounds.Left + bounds.Width / 2f, bounds.Bottom - 2),
            };

        if (hidesTicks)
        {
            using var fill = new SolidBrush(style.Text);
            graphics.FillPolygon(fill, arrow);
        }
        else
        {
            using var pen = new Pen(Color.FromArgb(170, style.Text));
            graphics.DrawPolygon(pen, arrow);
        }
    }

    private static void RenderTick(Graphics graphics, Rectangle box, bool ticked, GH_PaletteStyle style)
    {
        using (var fill = new SolidBrush(ticked ? Color.White : Color.FromArgb(70, Color.White)))
            graphics.FillRectangle(fill, box);

        using (var edge = new Pen(Color.FromArgb(190, style.Edge)))
            graphics.DrawRectangle(edge, box);

        if (!ticked) return;

        // Drawn rather than glyphed: a font tick lands differently on every
        // machine, and this one is eleven pixels across.
        using var pen = new Pen(style.Edge, 1.7f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        graphics.DrawLines(pen, new PointF[]
        {
            new(box.Left + 2.5f, box.Top + box.Height * 0.55f),
            new(box.Left + box.Width * 0.42f, box.Bottom - 2.5f),
            new(box.Right - 2f, box.Top + 2.5f),
        });
    }

    private static void RenderSwatch(Graphics graphics, Rectangle box, Color colour)
    {
        using var fill = new SolidBrush(Color.FromArgb(255, colour));
        graphics.FillRectangle(fill, box);

        using var edge = new Pen(Color.FromArgb(90, 0, 0, 0));
        graphics.DrawRectangle(edge, box);
    }

    // ------------------------------------------------------------------ mouse

    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        => Click(sender, e) ?? base.RespondToMouseDown(sender, e);

    /// <summary>
    /// A double click inside the list is two ticks, not one tick and a swallowed
    /// event — clicking a box twice quickly should leave it where it started,
    /// the way a real tick box does.
    /// </summary>
    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        => Click(sender, e) ?? base.RespondToMouseDoubleClick(sender, e);

    /// <summary>
    /// Acts on a click in the list, or returns null to let the base class have
    /// it — which is what keeps the header draggable and the nubbin wireable.
    /// </summary>
    private GH_ObjectResponse? Click(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button != MouseButtons.Left) return null;
        if (!_panel.Contains(GH_Convert.ToPoint(e.CanvasLocation))) return null;

        IReadOnlyList<LayerNode> rows = Picker.VisibleRows;

        for (int i = 0; i < rows.Count && i < _rowBounds.Count; i++)
        {
            if (!_rowBounds[i].Contains(GH_Convert.ToPoint(e.CanvasLocation))) continue;

            LayerNode row = rows[i];

            // The arrow is the only thing in a row that is not a tick. Everything
            // else — box, swatch, name, the empty space after it — toggles, so
            // the target is the whole row rather than eleven pixels of it.
            int arrow = _rowBounds[i].X + row.Depth * Indent;

            if (row.HasChildren
                && e.CanvasLocation.X >= arrow
                && e.CanvasLocation.X < arrow + ArrowSlot)
                Picker.ToggleCollapse(row);
            else
                Picker.Toggle(row, wholeBranch: (Control.ModifierKeys & Keys.Shift) == Keys.Shift);

            sender.Refresh();
            return GH_ObjectResponse.Handled;
        }

        // Inside the list but not on a row: still ours, so the click does not
        // start dragging the component out from under the pointer.
        return GH_ObjectResponse.Handled;
    }
}
