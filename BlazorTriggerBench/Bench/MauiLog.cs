using System.Globalization;
using System.IO;

namespace BlazorTriggerBench.Bench;

/// <summary>File logger for the MAUI-native bench (parallels BenchLog). Writes maui.log + maui-metrics.csv.</summary>
public static class MauiLog
{
    private static readonly object Gate = new();
    public static string Dir { get; } = ResolveLogDir();
    private static readonly string LogPath = Path.Combine(Dir, "maui.log");
    private static readonly string CsvPath = Path.Combine(Dir, "maui-metrics.csv");

    private const string CsvHeader = "time,event,framework,rows,frags,genMs,renderMs,avgFps,minFps,worstMs,samples,note";

    static MauiLog()
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
        Line($"session start | MAUI native (Windows/WinUI handlers) | .NET {Environment.Version} | {Environment.MachineName}");
    }

    public static void Build(int rows, int frags, double genMs, double renderMs)
    {
        Metric("build", rows, frags, genMs, renderMs, null, null, null, null, "");
        Line($"build [MAUI] rows={rows} frags/行={frags} | 生成 {genMs:F1}ms | 首屏 {renderMs:F1}ms");
    }

    public static void Scroll(int rows, int frags, double avgFps, double minFps, double worstMs, int samples)
    {
        Metric("scroll", rows, frags, null, null, avgFps, minFps, worstMs, samples, $"{samples} samples jump-stress");
        Line($"jump-stress [MAUI] rows={rows} frags/行={frags} | avg {avgFps:F0} | min {minFps:F0} fps | 最差帧 {worstMs:F0}ms");
    }

    private static void Metric(string evt, int rows, int frags, double? genMs, double? renderMs,
        double? avgFps, double? minFps, double? worstMs, int? samples, string note)
    {
        try
        {
            string line = string.Join(',',
                Stamp(), evt, "MAUI(Win)",
                rows.ToString(CultureInfo.InvariantCulture), frags.ToString(CultureInfo.InvariantCulture),
                F(genMs), F(renderMs), F(avgFps, 0), F(minFps, 0), F(worstMs),
                samples?.ToString(CultureInfo.InvariantCulture) ?? "", note);
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
