using Microsoft.UI.Reactor;       // fluent element extensions
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ReactorCompositionSpike.Composition;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCompositionSpike;

public sealed partial class MainWindow : Window
{
    private SwapChainRenderer? _renderer;
    private ReactorHostControl? _host;
    private DispatcherTimer? _fpsTimer;
    private bool _heavy;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1120, 780));

        // Start the live swap chain once the panel has a size.
        ScPanel.Loaded += (_, _) => _renderer ??= new SwapChainRenderer(ScPanel);

        // Transparent Reactor host on top — the whole point: native UI compositing over live GPU.
        _host = new ReactorHostControl
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        _host.Mount(_ => Overlay(_heavy));
        OverlayHost.Child = _host;

        _fpsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _fpsTimer.Tick += (_, _) =>
            FpsText.Text = $"swapchain present FPS: {(_renderer?.PresentFps ?? 0):F0}" +
                           (_heavy ? "   （重 UI 叠加中）" : "");
        _fpsTimer.Start();

        Closed += (_, _) =>
        {
            _fpsTimer?.Stop();
            _renderer?.Dispose();
            _host?.Dispose();
        };
    }

    private Window? _blockWindow;
    private void OnOpenBlocks(object sender, RoutedEventArgs e)
    {
        _blockWindow = new BlockCanvasWindow();
        _blockWindow.Activate();
    }

    private void OnToggleHeavy(object sender, RoutedEventArgs e)
    {
        _heavy = !_heavy;
        HeavyBtn.Content = _heavy ? "重叠加: 开" : "重叠加: 关";
        _host?.Mount(_ => Overlay(_heavy));
    }

    private Element Overlay(bool heavy)
    {
        if (!heavy)
        {
            return VStack(18,
                Border(VStack(8,
                    Heading("Reactor 原生 UI"),
                    TextBlock("叠加在 D3D11 SwapChainPanel 之上 — 原生透明合成（Blazor/WebView2 做不到）")
                        .Foreground("#FFFFFF"),
                    TextBlock("下面三个按钮控制底层 D3D 动画（Reactor UI 驱动 D3D 层）：")
                        .Foreground("#FFFFFF").FontSize(12).Opacity(0.85),
                    HStack(8,
                        Button("播放", () => { if (_renderer != null) _renderer.Paused = false; }),
                        Button("暂停", () => { if (_renderer != null) _renderer.Paused = true; }),
                        Button("重置", () => _renderer?.ResetAnimation())
                    )
                ).Padding(16)).Background("#101820").Opacity(0.72).CornerRadius(10),
                TextBlock("↓ 下方留白处是透明的，背后是实时 D3D 动画（清屏色循环）↓").Foreground("#FFFFFF"),
                Border(TextBlock("半透明面板（Opacity 0.4）：背后的 GPU 动画应透过来")
                    .Foreground("#FFFFFF").Padding(14)).Background("#101820").Opacity(0.4).CornerRadius(10)
            ).Padding(28).VAlign(VerticalAlignment.Top);
        }

        // Heavy: many semi-transparent native rows composited over the live swap chain — stresses the
        // compositor (alpha overdraw) so we can watch the swap chain's present FPS for contention.
        const int n = 200;
        var rows = new Element[n];
        for (int i = 0; i < n; i++)
        {
            var children = new List<Element> { TextBlock($"行 {i}").Foreground("#FFFFFF").Bold() };
            for (int j = 0; j < 8; j++)
                children.Add(Border(TextBlock($"f{j}").Foreground("#FFFFFF").FontSize(11))
                    .Background("#2A3A4A").CornerRadius(3).Padding(5, 1));

            rows[i] = (Border(HStack(6, children.ToArray()).Padding(6, 3))
                .Background("#101820").Opacity(0.6).CornerRadius(4)) with { Key = $"r{i}" };
        }
        return ScrollViewer(VStack(3, rows)).Padding(12);
    }
}
