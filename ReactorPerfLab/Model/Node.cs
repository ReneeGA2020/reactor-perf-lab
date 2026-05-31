using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ReactorPerfLab.Model;

/// <summary>
/// Three heterogeneous node kinds, mirroring the SCE test's TypeA/TypeB/TypeC.
/// In milestone 1 every kind renders through ONE shared (homogeneous) template, so
/// the native XAML TreeView never touches an ItemTemplateSelector and never hits the
/// virtualization-recycling crash. The 3-kind data still produces visually distinct
/// rows (icon glyph / badge / accent) so the template is non-trivial.
/// </summary>
public enum NodeKind
{
    Folder,   // ~ TypeA
    Document, // ~ TypeB
    Metric,   // ~ TypeC
}

/// <summary>
/// A single tree node. Plain object with OneTime-bindable getters — the data is static
/// after generation, so x:Bind defaults (OneTime) are the fastest fair binding mode.
/// </summary>
public sealed class Node
{
    public required string Name { get; init; }
    public required NodeKind Kind { get; init; }
    public string Detail { get; init; } = string.Empty;
    public bool IsExpanded { get; init; }
    public IReadOnlyList<Node> Children { get; init; } = System.Array.Empty<Node>();

    // Segoe Fluent Icons / Segoe MDL2 glyph code points. Built from ints so the source
    // file never embeds private-use-area characters.
    public string Glyph => ((char)(Kind switch
    {
        NodeKind.Folder => 0xE8B7,   // Folder
        NodeKind.Document => 0xE7C3, // Page
        _ => 0xE9D9,                 // Diagnostic / metric
    })).ToString();

    public string KindLabel => Kind switch
    {
        NodeKind.Folder => "A",
        NodeKind.Document => "B",
        _ => "C",
    };

    /// <summary>Hex accent used by the Reactor view-builder (Background/Foreground take a hex string).</summary>
    public string AccentHex => Kind switch
    {
        NodeKind.Folder => "#0078D4",
        NodeKind.Document => "#107C10",
        _ => "#C29008",
    };

    /// <summary>Cached brush used by the XAML template's x:Bind. Shared per kind across all rows.</summary>
    public Brush AccentBrush => Kind switch
    {
        NodeKind.Folder => _folderBrush ??= new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4)),
        NodeKind.Document => _documentBrush ??= new SolidColorBrush(Color.FromArgb(0xFF, 0x10, 0x7C, 0x10)),
        _ => _metricBrush ??= new SolidColorBrush(Color.FromArgb(0xFF, 0xC2, 0x90, 0x08)),
    };

    private static SolidColorBrush? _folderBrush;
    private static SolidColorBrush? _documentBrush;
    private static SolidColorBrush? _metricBrush;
}

/// <summary>
/// Builds a tree of <paramref name="rootCount"/> roots, each fanning out to
/// <c>childrenPerNode</c> children down to <c>depth</c>. Node kind cycles A/B/C by a
/// global index so siblings are heterogeneous. Returns the roots and the total count.
/// Total nodes per root = (c^(d+1) - 1) / (c - 1); grows fast — start conservative.
/// </summary>
public static class TreeData
{
    public static (IReadOnlyList<Node> Roots, int Total) Build(int rootCount, int childrenPerNode, int depth)
    {
        int counter = 0;
        var roots = new List<Node>(rootCount);
        for (int i = 0; i < rootCount; i++)
            roots.Add(CreateNode(ref counter, childrenPerNode, depth, 0));
        return (roots, counter);
    }

    private static Node CreateNode(ref int index, int childrenPerNode, int maxDepth, int currentDepth)
    {
        int id = index++;
        var kind = (NodeKind)(id % 3);

        IReadOnlyList<Node> children;
        if (currentDepth < maxDepth)
        {
            var list = new List<Node>(childrenPerNode);
            for (int i = 0; i < childrenPerNode; i++)
                list.Add(CreateNode(ref index, childrenPerNode, maxDepth, currentDepth + 1));
            children = list;
        }
        else
        {
            children = System.Array.Empty<Node>();
        }

        return new Node
        {
            Name = $"{kind}_{id}",
            Kind = kind,
            Detail = $"id={id} - depth={currentDepth} - {children.Count} children",
            IsExpanded = currentDepth < 2, // default-expand the first two levels (matches the SCE test)
            Children = children,
        };
    }
}
