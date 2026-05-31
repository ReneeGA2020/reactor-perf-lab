using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;
using Component = Microsoft.UI.Reactor.Core.Component;

// Minimal repro for: PropertyGrid does not render array / List<T> properties.
// microsoft/microsoft-ui-reactor @ main (de5c1351). TFM net10.0-windows10.0.22621.0.
ReactorApp.Run<ReproApp>("PropertyGrid array repro", width: 820, height: 340);

class Item
{
    public string Name { get; set; } = "";
    public int Qty { get; set; }
    public override string ToString() => $"{Name} x{Qty}";
}

class Model
{
    public List<string> Tags { get; set; } = new() { "a", "b" };
    public List<Item> Items { get; set; } = new() { new() { Name = "Sword", Qty = 1 }, new() { Name = "Potion", Qty = 3 } };
    public int[] Scores { get; set; } = { 10, 20, 30 };
}

class ReproApp : Component
{
    public override Element Render()
    {
        var registry = new TypeRegistry();
        return VStack(10,
            Heading("PropertyGrid — array / List<T> property"),
            TextBlock("BUG: array / List<T> rows render as ToString() instead of an editable list "
                    + "(no count, no add, no per-item reorder/remove).").Foreground(SecondaryText),
            PropertyGrid(new Model(), registry)
        ).Padding(16);
    }
}
