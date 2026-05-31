using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;

namespace ReactorCompositionSpike.Composition;

/// <summary>
/// Minimal D3D11 swap-chain renderer bound to a XAML <see cref="SwapChainPanel"/>. It clears the
/// back buffer to an animated colour each frame and presents — a stand-in for "live GPU content"
/// (a 3D viewport). It also tracks present FPS so we can watch how a heavy native UI overlay
/// contends with the live swap chain (and vice versa).
/// </summary>
public sealed class SwapChainRenderer : IDisposable
{
    private readonly SwapChainPanel _panel;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDXGISwapChain1 _swapChain = null!;
    private ID3D11RenderTargetView _rtv = null!;

    private int _width, _height;
    private bool _disposed;

    // Present FPS (frames presented in the last ~1s window).
    private int _frames;
    private double _fpsWindowStart;
    public double PresentFps { get; private set; }

    // Animation clock that the overlay's 播放/暂停/重置 buttons drive (proves Reactor UI → D3D control).
    private double _animTime;
    private double _lastTick;
    public bool Paused { get; set; }
    public void ResetAnimation() => _animTime = 0;

    public SwapChainRenderer(SwapChainPanel panel)
    {
        _panel = panel;
        _width = Math.Max(1, (int)Math.Round(panel.ActualWidth));
        _height = Math.Max(1, (int)Math.Round(panel.ActualHeight));

        CreateDeviceAndSwapChain();
        SwapChainPanelInterop.SetSwapChain(panel, _swapChain.NativePointer);
        CreateRenderTarget();

        panel.SizeChanged += OnSizeChanged;
        _lastTick = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
    }

    private void CreateDeviceAndSwapChain()
    {
        var flags = DeviceCreationFlags.BgraSupport;
        var levels = new[]
        {
            FeatureLevel.Level_11_1, FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1, FeatureLevel.Level_10_0,
        };

        var result = D3D11CreateDevice(null, DriverType.Hardware, flags, levels, out _device!, out _context!);
        if (result.Failure)
            D3D11CreateDevice(null, DriverType.Warp, flags, levels, out _device!, out _context!).CheckError();

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();

        var desc = new SwapChainDescription1
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = AlphaMode.Ignore,
            Flags = SwapChainFlags.None,
        };

        _swapChain = factory.CreateSwapChainForComposition(_device, desc);
    }

    private void CreateRenderTarget()
    {
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _rtv = _device.CreateRenderTargetView(backBuffer);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        int w = Math.Max(1, (int)Math.Round(_panel.ActualWidth));
        int h = Math.Max(1, (int)Math.Round(_panel.ActualHeight));
        if (w == _width && h == _height) return;

        _width = w;
        _height = h;
        _rtv?.Dispose();
        _rtv = null!;
        _swapChain.ResizeBuffers(2, (uint)w, (uint)h, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
        CreateRenderTarget();
    }

    private void OnRendering(object? sender, object e)
    {
        if (_disposed || _rtv == null) return;

        double now = _clock.Elapsed.TotalSeconds;
        if (!Paused) _animTime += now - _lastTick;
        _lastTick = now;

        float t = (float)_animTime;
        var color = new Color4(
            0.5f + 0.5f * MathF.Sin(t * 0.7f),
            0.5f + 0.5f * MathF.Sin(t * 0.9f + 2.0f),
            0.5f + 0.5f * MathF.Sin(t * 1.3f + 4.0f),
            1.0f);

        _context.ClearRenderTargetView(_rtv, color);
        _swapChain.Present(1, PresentFlags.None);

        _frames++;
        now = _clock.Elapsed.TotalSeconds;
        if (now - _fpsWindowStart >= 1.0)
        {
            PresentFps = _frames / (now - _fpsWindowStart);
            _frames = 0;
            _fpsWindowStart = now;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CompositionTarget.Rendering -= OnRendering;
        _panel.SizeChanged -= OnSizeChanged;
        _rtv?.Dispose();
        _swapChain?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }
}
