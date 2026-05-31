using Microsoft.UI.Reactor;       // fluent element extensions
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting; // XamlHostElement
using ReactorPerfLab.Model;
using ReactorPerfLab.Xaml;          // RichTextRow
using static Microsoft.UI.Reactor.Factories;

namespace ReactorPerfLab.ReactorViews;

/// <summary>
/// Reactor side of the statement benchmark: a flat <c>ListView&lt;StatementRow&gt;</c> whose
/// view-builder emits, per row, the same heavy structure the XAML templates do — chevron + kind
/// chip + a horizontal run of inline fragment elements (styled text / chips / real Buttons /
/// CheckBoxes) + a trailing add button. Reactor mounts a fresh element per realized container
/// (no recycling type-mismatch) and the ListView is data-virtualized.
/// </summary>
public sealed class ReactorStatementBench : Component
{
    private const string IconFont = "Segoe Fluent Icons";
    private static readonly string Chevron = ((char)0xE76C).ToString();

    private readonly IReadOnlyList<StatementRow> _rows;
    private readonly string _mode; // "Heavy" | "Light" | "Interactive"

    public ReactorStatementBench(IReadOnlyList<StatementRow> rows, string mode = "Heavy")
    {
        _rows = rows;
        _mode = mode;
    }

    public override Element Render() =>
        ListView<StatementRow>(_rows, r => r.Id, (r, _) => _mode switch
        {
            // Pure-light: declarative RichTextBlock + colored Runs (cheapest, no per-fragment interaction).
            "Light" => RowLight(r),
            // Interactive via XamlHost: native RichTextBlock (Hyperlinks + InlineUIContainer) hosted in Reactor.
            "Interactive" => new XamlHostElement(() => RichTextRow.Build(r, interactive: true)),
            // Interactive, fully DECLARATIVE: uses the fork's new Hyperlink(text, onClick) clickable inline — no XamlHost.
            "InteractiveDecl" => RowInteractiveDeclarative(r),
            _ => Row(r),
        });

    // Declarative interactive row: static frags = Run, interactive frags = clickable Hyperlink (the fork's
    // Hyperlink(text, onClick)). No InlineUIContainer/XamlHost — button/checkbox become clickable text. Stays in
    // Reactor's reconciled model (click → setState in a real app); recycling-safe (rich text rebuilds wholesale).
    private static Element RowInteractiveDeclarative(StatementRow r)
    {
        var inlines = new List<RichTextInline>(r.Fragments.Count + 1)
        {
            Run(r.KindLabel + "  ") with { Foreground = r.KindBrush, IsBold = true },
        };
        foreach (var f in r.Fragments)
        {
            if (f.Kind is FragKind.Editable or FragKind.Type or FragKind.Operator or FragKind.Button or FragKind.Checkbox)
            {
                inlines.Add(Hyperlink(f.Text, static () => { /* real app: inline edit / type-select / operator menu / toggle */ })
                    with { Foreground = f.Foreground, IsBold = f.Bold });
                inlines.Add(Run(" "));
            }
            else
            {
                inlines.Add(Run(f.Text + " ") with { Foreground = f.Foreground, IsBold = f.Bold });
            }
        }
        return RichTextBlock(new[] { Paragraph(inlines.ToArray()) }).Margin(r.Depth * 16, 0, 0, 0);
    }

    // Lightweight variant: ONE RichTextBlock per row, fragments as colored Runs (lightweight text
    // inlines, not UIElements). Collapses ~N elements/row → 1 element + N inlines. Loses per-fragment
    // controls (button/checkbox become styled text) — the point is the element-count win.
    private static Element RowLight(StatementRow r)
    {
        var inlines = new List<RichTextInline>(r.Fragments.Count + 1)
        {
            Run(r.KindLabel + "  ") with { Foreground = r.KindBrush, IsBold = true },
        };
        foreach (var f in r.Fragments)
            inlines.Add(Run(f.Text + " ") with { Foreground = f.Foreground, IsBold = f.Bold, FontSize = 12 });

        return RichTextBlock(new[] { Paragraph(inlines.ToArray()) }).Margin(r.Depth * 16, 0, 0, 0);
    }

    private static Element Row(StatementRow r)
    {
        var children = new List<Element>(r.Fragments.Count + 3)
        {
            TextBlock(Chevron).FontFamily(IconFont).FontSize(10).Foreground("#808080"),
            Border(TextBlock(r.KindLabel).FontSize(11).Foreground("#FFFFFF").Bold())
                .Background(r.KindHex).CornerRadius(3).Padding(6, 1),
        };
        foreach (var f in r.Fragments)
            children.Add(Frag(f));
        children.Add(Button("+").FontSize(11).Padding(6, 0).Opacity(0.5));

        return HStack(6, children.ToArray()).Margin(r.Depth * 16, 0, 0, 0);
    }

    private static Element Frag(Fragment f) => f.Kind switch
    {
        FragKind.Button => Button(f.Text).FontSize(11).Padding(6, 0),
        FragKind.Checkbox => CheckBox(false, label: f.Text),
        _ => f.IsTextChip
            ? Border(Text(f)).Background(f.ChipBackgroundHex).CornerRadius(3).Padding(6, 1)
            : Text(f),
    };

    private static Element Text(Fragment f)
    {
        var t = TextBlock(f.Text).FontSize(12).Foreground(f.ForegroundHex);
        return f.Bold ? t.Bold() : t;
    }
}
