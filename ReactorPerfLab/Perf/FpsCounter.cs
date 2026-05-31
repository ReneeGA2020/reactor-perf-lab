using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ReactorPerfLab.Perf;

/// <summary>
/// Framework-agnostic FPS meter on <see cref="CompositionTarget.Rendering"/> (same approach as
/// the test), but split into two phases so the one-time mount/first-layout hitch does NOT
/// pollute steady-state numbers:
///
///   • 首次布局 (warm-up): right after a view is mounted. We report the WORST single frame time
///     (the hitch) here — this is where Reactor's known longer-first-layout cost shows up.
///   • 运行时 (runtime): steady state (e.g. while scrolling). cur/avg/min/max accumulate ONLY here.
///
/// The warm-up→runtime transition is adaptive: it stays in warm-up until a calm 500 ms window
/// appears (worst frame &lt; <see cref="SettleFrameMs"/>), so a large/slow first layout (big data
/// sets, Reactor) is fully attributed to warm-up rather than leaking into runtime min.
/// </summary>
public sealed class FpsCounter : IDisposable
{
    public readonly record struct Snapshot(
        double Cur, double Avg, double Min, double Max, double WarmupWorstFrameMs, bool InWarmup);

    private const double WarmupMinMs = 400;   // minimum warm-up before runtime can begin
    private const double SettleFrameMs = 50;  // a window whose worst frame is under this = "settled"

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _windowSw = new(); // wall time of the current ~500 ms window
    private readonly Stopwatch _phaseSw = new();  // time since warm-up began
    private readonly Stopwatch _frameSw = new();  // since last Rendering tick (per-frame dt)

    private bool _running;
    private bool _disposed;
    private bool _inWarmup;

    private int _frames;                // frames counted in the current window
    private double _windowWorstFrameMs; // max per-frame dt in the current window
    private double _warmupWorstFrameMs; // max per-frame dt across the whole warm-up

    // Runtime aggregate (only accumulated after warm-up ends).
    private long _rtFrames;
    private double _rtSeconds;
    private double _rtMin;
    private double _rtMax;
    private double _rtCur;
    private double _rtWorst; // worst single frame (ms) seen during runtime (scroll jank peak)

    public event Action<Snapshot>? Updated;

    // ── Accessors for scripted measurement (auto-scroll, sweep) ──
    public bool InWarmup => _inWarmup;
    public double WarmupWorstFrameMs => _warmupWorstFrameMs;
    public double RuntimeAvg => _rtSeconds > 0 ? _rtFrames / _rtSeconds : 0;
    public double RuntimeMin => _rtMin == double.MaxValue ? 0 : _rtMin;
    public double RuntimeMax => _rtMax;
    public double RuntimeWorstFrameMs => _rtWorst;

    /// <summary>Force runtime phase and reset the runtime aggregate — for a clean scripted measurement window.</summary>
    public void BeginMeasure()
    {
        _inWarmup = false;
        ResetRuntime();
        _frames = 0;
        _windowWorstFrameMs = 0;
        _windowSw.Restart();
        _frameSw.Restart();
    }

    public FpsCounter()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        BeginWarmup();
        CompositionTarget.Rendering += OnRendering;
        _timer.Start();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _timer.Stop();
        CompositionTarget.Rendering -= OnRendering;
    }

    /// <summary>Call when a new view is mounted to re-enter the first-layout (warm-up) phase.</summary>
    public void BeginWarmup()
    {
        _inWarmup = true;
        _frames = 0;
        _windowWorstFrameMs = 0;
        _warmupWorstFrameMs = 0;
        ResetRuntime();
        _phaseSw.Restart();
        _windowSw.Restart();
        _frameSw.Restart();
    }

    private void ResetRuntime()
    {
        _rtFrames = 0;
        _rtSeconds = 0;
        _rtMin = double.MaxValue;
        _rtMax = 0;
        _rtCur = 0;
        _rtWorst = 0;
    }

    private void OnRendering(object? sender, object e)
    {
        double dt = _frameSw.Elapsed.TotalMilliseconds;
        _frameSw.Restart();
        _frames++;
        if (dt > _windowWorstFrameMs) _windowWorstFrameMs = dt;
    }

    private void OnTick(object? sender, object e)
    {
        double secs = _windowSw.Elapsed.TotalSeconds;
        double cur = secs > 0 ? _frames / secs : 0;

        if (_inWarmup)
        {
            if (_windowWorstFrameMs > _warmupWorstFrameMs) _warmupWorstFrameMs = _windowWorstFrameMs;

            bool settled = _phaseSw.Elapsed.TotalMilliseconds >= WarmupMinMs
                           && _frames > 0
                           && _windowWorstFrameMs < SettleFrameMs;
            if (settled)
            {
                _inWarmup = false;
                ResetRuntime(); // runtime min/max start fresh, clean of the hitch
            }
        }
        else
        {
            _rtCur = cur;
            _rtFrames += _frames;
            _rtSeconds += secs;
            if (cur > 0 && cur < _rtMin) _rtMin = cur;
            if (cur > _rtMax) _rtMax = cur;
            if (_windowWorstFrameMs > _rtWorst) _rtWorst = _windowWorstFrameMs;
        }

        double avg = _rtSeconds > 0 ? _rtFrames / _rtSeconds : 0;
        double min = _rtMin == double.MaxValue ? 0 : _rtMin;
        Updated?.Invoke(new Snapshot(_rtCur, avg, min, _rtMax, _warmupWorstFrameMs, _inWarmup));

        _frames = 0;
        _windowWorstFrameMs = 0;
        _windowSw.Restart();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
