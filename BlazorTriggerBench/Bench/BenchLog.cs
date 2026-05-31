using System.Globalization;
using System.IO;

namespace BlazorTriggerBench.Bench;

/// <summary>
/// File logger for the Blazor bench — mirrors ReactorPerfLab's PerfLog so results can be analysed
/// offline alongside the WinUI/Reactor numbers. Writes a human-readable <c>blazor.log</c> and a
/// machine-readable <c>blazor-metrics.csv</c> into a <c>logs/</c> folder next to the project (resolved
/// by walking up from the exe to the .csproj). The MAUI Windows host is a normal .NET process, so
/// disk access works. Logging must never crash the bench.
/// </summary>
public static class BenchLog
{
    private static readonly object Gate = new();
    public static string Dir { get; } = ResolveLogDir();
    private static readonly string LogPath = Path.Combine(Dir, "blazor.log");
    private static readonly string CsvPath = Path.Combine(Dir, "blazor-metrics.csv");

    private const string CsvHeader =
        "time,event,framework,rows,frags,genMs,renderMs,domNodes,avgFps,minFps,worstMs,samples,note";

    static BenchLog()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (!File.Exists(CsvPath))
                File.AppendAllText(CsvPath, CsvHeader + Environment.NewLine);
        }
        catch { }
    }

    public static void Session()
    {
        Line("──────────────────────────────────────────────");
        Line($"session start | Blazor Hybrid (MAUI/WebView2) | .NET {Environment.Version} | {Environment.MachineName}");
    }

    public static void Build(int rows, int frags, double genMs, double renderMs, int dom, string mode = "flat")
    {
        Metric("build", rows, frags, genMs, renderMs, dom, null, null, null, null, $"render={mode}");
        Line($"build [Blazor/{mode}] rows={rows} frags/行={frags} | 生成 {genMs:F1}ms | 首屏渲染 {renderMs:F1}ms | DOM {dom}");
    }

    public static void Scroll(int rows, int frags, double avgFps, double minFps, double worstMs, int dom, int samples, string mode = "flat")
    {
        Metric("scroll", rows, frags, null, null, dom, avgFps, minFps, worstMs, samples,
            $"{samples} jumps, render={mode}");
        Line($"jump-stress [Blazor/{mode}] rows={rows} frags/行={frags} | avg {avgFps:F0} | min {minFps:F0} fps | 最差帧 {worstMs:F0}ms | DOM {dom}");
    }

    private static void Metric(string evt, int rows, int frags, double? genMs, double? renderMs, int dom,
        double? avgFps, double? minFps, double? worstMs, int? samples, string note)
    {
        try
        {
            string line = string.Join(',',
                Stamp(), evt, "Blazor(Hybrid)",
                rows.ToString(CultureInfo.InvariantCulture),
                frags.ToString(CultureInfo.InvariantCulture),
                F(genMs), F(renderMs), dom.ToString(CultureInfo.InvariantCulture),
                F(avgFps, 0), F(minFps, 0), F(worstMs),
                samples?.ToString(CultureInfo.InvariantCulture) ?? "", Csv(note));
            lock (Gate)
                File.AppendAllText(CsvPath, line + Environment.NewLine);
        }
        catch { }
    }

    public static void Line(string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(LogPath, $"{Stamp()}  {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    private static string F(double? v, int dp = 1) =>
        v.HasValue && !double.IsNaN(v.Value) ? v.Value.ToString("F" + dp, CultureInfo.InvariantCulture) : "";
    private static string Csv(string s) => s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    private static string ResolveLogDir()
    {
        try
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null)
            {
                if (d.GetFiles("BlazorTriggerBench.csproj").Length > 0)
                    return Path.Combine(d.FullName, "logs");
                d = d.Parent;
            }
        }
        catch { }
        return Path.Combine(AppContext.BaseDirectory, "logs");
    }
}
