using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;

// Repro for the concern raised on issue #447: does Reactor's typed, data-driven
// TreeView<T> inherit the WinUI XAML ItemTemplateSelector failure mode?
//
// The XAML bug (F:\WinUIItemTemplateSelectorBug): under fast scroll / container
// recycling, XAML reused a realized container without re-matching the selected
// template to the current item type. With x:Bind the generated code cast the
// item to the WRONG type and crashed; with {Binding} it rendered the wrong /
// blank content. The trigger is heterogeneous item visuals + container recycling.
//
// Here we recreate the equivalent shape in Reactor and stress it the same way.
ReactorApp.Run<VerifyApp>("TreeView Hetero Verify (#447)", width: 900, height: 720);

// ── Two heterogeneous node shapes — the Reactor analogue of TestVM1 / TestVM2 ──
abstract record Node(string Id)
{
    public IReadOnlyList<Node> Children { get; init; } = [];
}
sealed record NodeA(string Id) : Node(Id);   // ~ TestVM1 ("Content1")
sealed record NodeB(string Id) : Node(Id);   // ~ TestVM2 ("Content2")

static class Data
{
    // 1000 alternating roots, each with two heterogeneous children — mirrors the
    // XAML repro's: CollectionViewModel.Add(new TestVM1 { Items = [TestVM1, TestVM2] }).
    public static readonly IReadOnlyList<Node> Roots =
        Enumerable.Range(0, 1000).Select(i => i % 2 == 0
            ? (Node)new NodeA($"n{i}") { Children = [new NodeA($"n{i}.a"), new NodeB($"n{i}.b")] }
            : new NodeB($"n{i}") { Children = [new NodeB($"n{i}.a"), new NodeA($"n{i}.b")] }
        ).ToList();
}

class VerifyApp : Component
{
    public override Element Render()
    {
        // viewBuilder == the ItemTemplateSelector equivalent: a switch on the
        // RUNTIME node type. This is exactly the spot where XAML's x:Bind
        // generated code would cast a recycled container's item to the wrong
        // type and crash. In Reactor the reconciler mounts the element this
        // returns imperatively into the realized container (fresh on realize,
        // unmount on recycle), so a recycled container should never carry a
        // stale element bound to the wrong type.
        static Element View(Node n) => n switch
        {
            NodeA a => TextBlock($"A : {a.Id}").Bold(),
            NodeB b => TextBlock($"B : {b.Id}").Opacity(0.7),
            _ => TextBlock("? unknown node"),
        };

        return VStack(12,
            Heading("Heterogeneous recycling — TreeView<T> vs ListView<T>"),
            TextBlock("Fast-scroll both columns, and expand/collapse tree rows, "
                    + "to stress container recycling. Watch for crashes or blank/"
                    + "mismatched rows (A showing where B should be).").Opacity(0.7),
            HStack(16,
                VStack(6,
                    SubHeading("TreeView<T>  (the #447 fix)"),
                    TreeView<Node>(
                        Data.Roots,
                        n => n.Id,            // keySelector
                        n => n.Children,      // childrenSelector (hierarchy)
                        View                  // viewBuilder (type switch)
                    ).Width(400).Height(560)
                ),
                VStack(6,
                    SubHeading("ListView<T>  (flat baseline)"),
                    ListView<Node>(
                        Data.Roots,
                        n => n.Id,
                        (n, _) => View(n)
                    ).Width(400).Height(560)
                )
            )
        ).Padding(20);
    }
}
