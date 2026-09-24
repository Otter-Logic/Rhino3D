using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace OtterLogic.Grasshopper.Components.Dataset;

/// <summary>
/// The stock component drawing, plus a double-click that opens the editor.
/// <para>
/// The table is deliberately not drawn on the canvas. A first version drew its
/// first rows the way the native Panel shows its first lines, and it made the
/// component as wide as the data and still showed too little of it to be read;
/// the message strip already says the size, and the whole table is one
/// double-click away in a window built for reading it.
/// </para>
/// </summary>
internal sealed class DataTableAttributes : GH_ComponentAttributes
{
    public DataTableAttributes(DataTableComponent owner) : base(owner)
    {
    }

    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
        if (e.Button == MouseButtons.Left && Bounds.Contains(e.CanvasLocation))
        {
            ((DataTableComponent)Owner).OpenEditor();
            sender.Refresh();
            return GH_ObjectResponse.Handled;
        }

        return base.RespondToMouseDoubleClick(sender, e);
    }
}
