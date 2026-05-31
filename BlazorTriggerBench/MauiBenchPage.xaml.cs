using System.Diagnostics;
using BlazorTriggerBench.Bench;
using WinComposition = Microsoft.UI.Xaml.Media.CompositionTarget;

namespace BlazorTriggerBench;

/// <summary>
/// MAUI-native bench: the SAME statement-row workload rendered with a MAUI <c>CollectionView</c> +
/// <c>BindableLayout</c> (one native view per fragment). On Windows, MAUI maps to WinUI handlers, so
/// this measures MAUI's abstraction over WinUI. FPS via the Windows compositor frame callback
/// (CompositionTarget.Rendering) to stay comparable with ReactorPerfLab.
/// </summary>
public partial class MauiBenchPage : ContentPage
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private List<StatementRow> _rows = new();
    private int _frags;
    private double _genMs;

    private bool _hooked;

    // live fps
    private int _frames;
    private double _fpsWinStart;
    private double _fps;

    // first-render probe
    private bool _awaitingRender;
    private double _renderStartMs;

    // jump-scroll measurement
    private bool _measuring;
    private int _i, _samples;
    private double _sumFps, _minFps, _worstMs, _lastFrameMs, _scrollStartMs;

    public MauiBenchPage()
    {
        InitializeComponent();
        MauiLog.Session();
        Loaded += (_, _) => { Hook(); Generate(); };
        Unloaded += (_, _) => Unhook();
    }

    private void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        _fpsWinStart = _clock.Elapsed.TotalMilliseconds;
        WinComposition.Rendering += OnFrame;
    }

    private void Unhook()
    {
        if (!_hooked) return;
        _hooked = false;
        WinComposition.Rendering -= OnFrame;
    }

    private void OnGenerate(object? sender, EventArgs e) => Generate();

    private void Generate()
    {
        int rows = int.TryParse(RowsEntry.Text, out var r) ? r : 500;
        _frags = int.TryParse(FragEntry.Text, out var f) ? f : 14;

        var sw = Stopwatch.StartNew();
        var (list, total) = StatementData.Build(rows, _frags);
        sw.Stop();
        _genMs = sw.Elapsed.TotalMilliseconds;
        _rows = list;

        MetricLabel.Text = $"行 {rows} | 片段 {total} | 生成 {_genMs:F1}ms | 渲染中…";
        List.ItemsSource = _rows;

        _renderStartMs = _clock.Elapsed.TotalMilliseconds;
        _awaitingRender = true;
    }

    private void OnAutoScroll(object? sender, EventArgs e)
    {
        if (_measuring || _rows.Count == 0) return;
        _measuring = true;
        _i = 0; _samples = 0; _sumFps = 0; _minFps = 1e9; _worstMs = 0;
        _lastFrameMs = _clock.Elapsed.TotalMilliseconds;
        _scrollStartMs = _lastFrameMs;
        MetricLabel.Text = "跳变压测中…(6s)";
    }

    private void OnFrame(object? sender, object e)
    {
        double now = _clock.Elapsed.TotalMilliseconds;

        // live fps
        _frames++;
        if (now - _fpsWinStart >= 500)
        {
            _fps = _frames * 1000.0 / (now - _fpsWinStart);
            _frames = 0;
            _fpsWinStart = now;
            if (!_measuring && !_awaitingRender) UpdateLiveLabel();
        }

        // first-render (first frame after ItemsSource set)
        if (_awaitingRender)
        {
            _awaitingRender = false;
            double renderMs = now - _renderStartMs;
            MetricLabel.Text = $"行 {_rows.Count} | 生成 {_genMs:F1}ms | 首屏 {renderMs:F1}ms | live {_fps:F0} fps";
            MauiLog.Build(_rows.Count, _frags, _genMs, renderMs);
        }

        // jump-scroll measurement
        if (_measuring)
        {
            double dt = now - _lastFrameMs;
            _lastFrameMs = now;
            if (dt > 0)
            {
                double f = 1000.0 / dt;
                _samples++;
                _sumFps += f;
                if (f < _minFps) _minFps = f;
                if (dt > _worstMs) _worstMs = dt;
            }

            int idx = (int)(Vdc(++_i) * Math.Max(1, _rows.Count - 1));
            try { List.ScrollTo(idx, position: ScrollToPosition.Start, animate: false); }
            catch { /* ignore transient scroll errors */ }

            if (now - _scrollStartMs >= 6000)
            {
                _measuring = false;
                double avg = _samples > 0 ? _sumFps / _samples : 0;
                double min = _minFps == 1e9 ? 0 : _minFps;
                MetricLabel.Text = $"跳变压测: avg {avg:F0} | min {min:F0} fps | 最差帧 {_worstMs:F0}ms";
                MauiLog.Scroll(_rows.Count, _frags, avg, min, _worstMs, _samples);
            }
        }
    }

    private void UpdateLiveLabel() =>
        MetricLabel.Text = $"行 {_rows.Count} | 生成 {_genMs:F1}ms | live {_fps:F0} fps";

    private static double Vdc(int n)
    {
        double q = 0, b = 0.5;
        while (n > 0) { q += (n & 1) * b; n >>= 1; b *= 0.5; }
        return q;
    }
}
