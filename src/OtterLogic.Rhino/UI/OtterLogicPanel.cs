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
/// by adding one line to that list rather than by editing layout.
/// </para>
/// <para>
/// Sized for a docked side panel, which is narrow. The one thing that has to be
/// handled by hand is the description text: an Eto label reports its unwrapped
/// single-line width as its preferred width, and a <see cref="Scrollable"/>
/// happily grows to fit that, so the panel ends up demanding a couple of hundred
/// pixels more than it needs. Giving every wrapping label an explicit width on
/// each resize pins the layout to the panel instead of the other way round.
/// </para>
/// </summary>
[Guid("18c73a4a-1e84-4ccd-8a22-aeee81e10a15")]
public class OtterLogicPanel : Eto.Forms.Panel
{
    /// <summary>Narrowest text column worth rendering, in pixels.</summary>
    private const int MinimumTextWidth = 110;

    /// <summary>Room left for the vertical scrollbar so text never sits under it.</summary>
    private const int ScrollbarAllowance = 20;

    private readonly List<Label> _descriptions = new();

    /// <summary>Rhino constructs the panel with the document it belongs to.</summary>
    public OtterLogicPanel(uint documentSerialNumber)
    {
        Padding = new Padding(8, 6);
        Content = BuildContent();

        SizeChanged += (_, _) => FitDescriptionsToPanel();
    }

    /// <summary>Identifies the panel to <c>Rhino.UI.Panels</c>.</summary>
    public static Guid PanelId => typeof(OtterLogicPanel).GUID;

    private Control BuildContent()
    {
        float baseSize = SystemFonts.Default().Size;
        Font sectionFont = SystemFonts.Bold(baseSize - 1.5f);
        Font summaryFont = SystemFonts.Default(baseSize - 2f);

        var stack = new StackLayout
        {
            Orientation = Orientation.Vertical,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Spacing = 3,
        };

        bool firstSection = true;

        foreach (var section in ToolCatalog.Tools.GroupBy(tool => tool.Section))
        {
            if (!firstSection)
                stack.Items.Add(new Eto.Forms.Panel { Height = 10 });

            firstSection = false;

            stack.Items.Add(new Label
            {
                Text = section.Key.ToUpperInvariant(),
                Font = sectionFont,
                TextColor = Colors.Gray,
            });

            foreach (OtterTool tool in section)
            {
                stack.Items.Add(CreateToolButton(tool));

                var summary = new Label
                {
                    Text = tool.Summary,
                    Font = summaryFont,
                    TextColor = Colors.Gray,
                    Wrap = WrapMode.Word,

                    // A starting width small enough that the panel can be docked
                    // narrow. FitDescriptionsToPanel corrects it on first layout.
                    Width = MinimumTextWidth,
                };

                _descriptions.Add(summary);
                stack.Items.Add(summary);
                stack.Items.Add(new Eto.Forms.Panel { Height = 6 });
            }
        }

        stack.Items.Add(new StackLayoutItem(null, expand: true));   // hold everything at the top

        return new Scrollable
        {
            Content = stack,
            Border = BorderType.None,
            ExpandContentWidth = true,
        };
    }

    private static Button CreateToolButton(OtterTool tool)
    {
        var button = new Button
        {
            Text = tool.Name,
            ToolTip = $"{tool.Summary}\n\nCommand: _{tool.Command}",
            MinimumSize = Size.Empty,   // let it shrink with the panel
        };

        // "!" cancels whatever command is already running, the same thing a Rhino
        // toolbar button does. Without it, clicking mid-command feeds the command
        // name to that command as input instead of starting this one.
        button.Click += (_, _) => RhinoApp.RunScript($"!_{tool.Command}", echo: false);

        return button;
    }

    private void FitDescriptionsToPanel()
    {
        if (Width <= 0) return;

        int available = Width - Padding.Left - Padding.Right - ScrollbarAllowance;
        available = Math.Max(available, MinimumTextWidth);

        foreach (Label description in _descriptions)
        {
            if (description.Width == available) continue;
            description.Width = available;
        }
    }
}
