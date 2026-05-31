using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using ReactorPerfLab.Model;

namespace ReactorPerfLab.Xaml;

/// <summary>
/// Builds a native <see cref="RichTextBlock"/> for one statement row — the lightweight rendering
/// (one element + N inline runs) instead of N UIElements per fragment.
///
/// <para><c>interactive=false</c>: every fragment is a plain <see cref="Run"/> (cheapest; loses per-fragment interaction).</para>
/// <para><c>interactive=true</c>: the REALISTIC editor shape — static text as Runs, clickable fragments
/// (editable value / type label / operator) as lightweight <see cref="Hyperlink"/>s (an inline with a click,
/// NOT a UIElement), and the few embedded controls (button / checkbox) as <see cref="InlineUIContainer"/>.
/// This keeps element count near the pure-light case while restoring per-fragment interaction.</para>
///
/// Shared by both the XAML ListView (built in ContainerContentChanging) and the Reactor side
/// (hosted via XamlHostElement), so the rendered leaf is identical.
/// </summary>
public static class RichTextRow
{
    public static RichTextBlock Build(StatementRow row, bool interactive)
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = row.KindLabel + "  ", Foreground = row.KindBrush, FontWeight = FontWeights.SemiBold });

        foreach (var f in row.Fragments)
        {
            if (interactive && f.Kind is FragKind.Button)
            {
                paragraph.Inlines.Add(new InlineUIContainer
                {
                    Child = new Button { Content = f.Text, FontSize = 11, MinWidth = 0, Padding = new Thickness(6, 0, 6, 0) },
                });
            }
            else if (interactive && f.Kind is FragKind.Checkbox)
            {
                paragraph.Inlines.Add(new InlineUIContainer
                {
                    Child = new CheckBox { Content = f.Text, FontSize = 11, MinWidth = 0 },
                });
            }
            else if (interactive && f.Kind is FragKind.Editable or FragKind.Type or FragKind.Operator)
            {
                // Clickable inline — per-fragment interaction without a UIElement per fragment.
                var link = new Hyperlink { Foreground = f.Foreground, UnderlineStyle = UnderlineStyle.None };
                link.Inlines.Add(new Run { Text = f.Text, FontWeight = f.Weight });
                link.Click += static (_, _) => { /* real editor: open inline edit / type-select / operator menu */ };
                paragraph.Inlines.Add(link);
                paragraph.Inlines.Add(new Run { Text = " " });
            }
            else
            {
                paragraph.Inlines.Add(new Run { Text = f.Text + " ", Foreground = f.Foreground, FontWeight = f.Weight });
            }
        }

        var rtb = new RichTextBlock { TextWrapping = TextWrapping.NoWrap, Margin = row.Indent };
        rtb.Blocks.Add(paragraph);
        return rtb;
    }
}
