using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReactorPerfLab.Model;

namespace ReactorPerfLab.Xaml;

/// <summary>
/// Fragment-level template selector: picks Text / Button / Checkbox per fragment, so a row can
/// mix plain styled spans with real interactive controls (the heavy, faithful case). Uses the
/// two-arg <see cref="SelectTemplateCore(object, DependencyObject)"/> for consistency. This runs
/// inside a non-virtualizing ItemsControl, so it is not exposed to the recycling crash, but using
/// the two-arg overload keeps the whole codebase on the safe path.
/// </summary>
public sealed class FragmentTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Text { get; set; }
    public DataTemplate? Button { get; set; }
    public DataTemplate? Checkbox { get; set; }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        if (item is Fragment f)
        {
            return f.Category switch
            {
                "Button" => Button ?? Text!,
                "Checkbox" => Checkbox ?? Text!,
                _ => Text!,
            };
        }
        return Text!;
    }
}
