using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino;

namespace OtterLogic.Rhino.UI;

/// <summary>
/// The OtterLogic tab: a dockable list of every tool, grouped the same way the
/// Grasshopper ribbon groups them.
/// <para>
/// Built entirely from <see cref="ToolCatalog"/>, so a new command appears here
/// by adding one line to that list rather than by editing layout. The point of
/// the panel over a toolbar is the descriptions — three months from now the
/// button label alone will not be enough to remember what a tool does.
/// </para>
/// </summary>
[Guid("18c73a4a-1e84-4ccd-8a22-aeee81e10a15")]
public class OtterLogicPanel : Eto.Forms.Panel
{
    /// <summary>Rhino constructs the panel with the document it belongs to.</summary>
    public OtterLogicPanel(uint documentSerialNumber)
    {
        Padding = new Padding(10, 8);
        Content = BuildContent();
    }

    /// <summary>Identifies the panel to <c>Rhino.UI.Panels</c>.</summary>
    public static Guid PanelId => typeof(OtterLogicPanel).GUID;

    private static Control BuildContent()
    {
        Font sectionFont = SystemFonts.Bold(SystemFonts.Default().Size - 1f);
        Font summaryFont = SystemFonts.Default(SystemFonts.Default().Size - 1.5f);

        var layout = new DynamicLayout { DefaultSpacing = new Size(0, 3) };

        foreach (var section in ToolCatalog.Tools.GroupBy(tool => tool.Section))
        {
            layout.AddRow(new Label
            {
                Text = section.Key.ToUpperInvariant(),
                Font = sectionFont,
                TextColor = Colors.Gray,
            });

            foreach (OtterTool tool in section)
            {
                layout.AddRow(CreateToolButton(tool));

                layout.AddRow(new Label
                {
                    Text = tool.Summary,
                    Font = summaryFont,
                    TextColor = Colors.Gray,
                    Wrap = WrapMode.Word,
                });

                layout.AddRow(new Eto.Forms.Panel { Height = 6 });
            }

            layout.AddRow(new Eto.Forms.Panel { Height = 6 });
        }

        layout.Add(null);   // soak up the slack so everything sits at the top

        return new Scrollable
        {
            Content = layout,
            Border = BorderType.None,
            ExpandContentWidth = true,
        };
    }

    private static Button CreateToolButton(OtterTool tool)
    {
        var button = new Button
        {
            Text = tool.Name,
            ToolTip = $"_{tool.Command}",
        };

        // "!" cancels whatever command is already running, the same thing a Rhino
        // toolbar button does. Without it, clicking mid-command feeds the command
        // name to that command as input instead of starting this one.
        button.Click += (_, _) => RhinoApp.RunScript($"!_{tool.Command}", echo: false);

        return button;
    }
}
