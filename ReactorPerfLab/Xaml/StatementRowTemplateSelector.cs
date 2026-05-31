using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReactorPerfLab.Model;

namespace ReactorPerfLab.Xaml;

/// <summary>
/// Heterogeneous row-template selector for the ListView statement scenario.
///
/// CRITICAL: this overrides ONLY the TWO-arg <see cref="SelectTemplateCore(object, DependencyObject)"/>.
/// The one-arg overload is the one that crashes under WinUI virtualization container recycling
/// (it mismatches the recycled container's data type with the template). The two-arg overload
/// is the crash-free escape hatch for ListView — which is exactly why this benchmark uses a
/// flat ListView for the heterogeneous case instead of a native TreeView (TreeView cannot use
/// this escape; see the project notes / TreeViewEx).
/// </summary>
public sealed class StatementRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Control { get; set; }
    public DataTemplate? Call { get; set; }
    public DataTemplate? Comment { get; set; }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        if (item is StatementRow row)
        {
            return row.Category switch
            {
                "Comment" => Comment ?? Control!,
                "Call" => Call ?? Control!,
                _ => Control!,
            };
        }
        return Control!;
    }
}
