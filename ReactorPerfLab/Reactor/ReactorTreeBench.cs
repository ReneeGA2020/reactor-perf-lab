using Microsoft.UI.Reactor;       // fluent element extensions: .Bold(), .FontSize(), .FontFamily(), ...
using Microsoft.UI.Reactor.Core;
using ReactorPerfLab.Model;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorPerfLab.ReactorViews;

/// <summary>
/// The Reactor side of the benchmark: a typed, data-driven <c>TreeView&lt;Node&gt;</c>.
/// The view-builder is a single function (no ItemTemplateSelector) that mirrors the
/// XAML DataTemplate: glyph icon + name/detail stack + a colored kind badge. The kind
/// is read at build time, so the rendered row is just as "complex" as the XAML one but
/// Reactor mounts a fresh element per realized container — no recycling type-mismatch.
/// </summary>
public sealed class ReactorTreeBench : Component
{
    private readonly IReadOnlyList<Node> _roots;

    public ReactorTreeBench(IReadOnlyList<Node> roots) => _roots = roots;

    public override Element Render()
    {
        static Element View(Node n) => HStack(8,
            TextBlock(n.Glyph)
                .FontFamily("Segoe Fluent Icons")
                .FontSize(14)
                .Foreground(n.AccentHex),
            VStack(
                TextBlock(n.Name).Bold(),
                TextBlock(n.Detail).FontSize(11).Opacity(0.7)
            ),
            Border(
                TextBlock(n.KindLabel).FontSize(10).Foreground("#FFFFFF")
            ).Background(n.AccentHex).CornerRadius(3).Padding(6, 1)
        );

        return TreeView<Node>(
            _roots,
            n => n.Name,        // keySelector (Name is globally unique)
            n => n.Children,    // childrenSelector
            View                // viewBuilder
        ) with
        {
            IsExpanded = node => node.IsExpanded, // initial expansion, matches XAML
        };
    }
}
