using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace ReactorPerfLab.Perf;

/// <summary>
/// One metric record. All numeric fields are nullable — only the ones relevant to a given
/// event are filled; the CSV writer emits a fixed column order with blanks for N/A.
/// </summary>
public sealed class MetricRow
{
    public string Event = "";       // build | scroll | sweep | memory
    public string Scenario = "";    // List | Tree
    public string Framework = "";   // Xaml | Reactor
    public int? Rows;
    public int? Frags;
    public int? Depth;
    public int? Items;
    public int? FragTotal;
    public double? GenMs;
    public double? LayoutMs;
    public double? WarmupWorstMs;
    public double? RBuildMs;
    public double? RReconcileMs;
    public double? REffectsMs;
    public double? RtAvgFps;
    public double? RtMinFps;
    public double? RtWorstMs;
    public double? ManagedMB;
    public double? WorkingSetMB;
    public string Note = "";
}

/// <summary>
/// Lightweight file logger for the perf lab. Writes a human-readable <c>perflab.log</c> and a
/// machine-readable <c>perflab-metrics.csv</c> into a <c>logs/</c> folder next to the project
/// (resolved by walking up from the exe to the .csproj), so results can be analysed offline.
/// All calls happen on the UI thread; a lock guards against any incidental reentrancy.
/// </summary>
public static class PerfLog
{
    private static readonly object Gate = new();
    public static string Dir { get; } = ResolveLogDir();
    private static readonly string LogPath = Path.Combine(Dir, "perflab.log");
    private static readonly string CsvPath = Path.Combine(Dir, "perflab-metrics.csv");

    private const string CsvHeader =
        "time,event,scenario,framework,rows,frags,depth,items,fragTotal," +
        "genMs,layoutMs,warmupWorstMs,rBuildMs,rReconcileMs,rEffectsMs," +
        "rtAvgFps,rtMinFps,rtWorstMs,managedMB,workingSetMB,note";

    static PerfLog()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (!File.Exists(CsvPath))
                File.AppendAllText(CsvPath, CsvHeader + Environment.NewLine);
        }
        catch { /* logging must never crash the app */ }
    }

    public static void Session()
    {
#if DEBUG
        const string cfg = "Debug";
#else
        const string cfg = "Release";
#endif
        Line("──────────────────────────────────────────────");
        Line($"session start | {cfg} | {RuntimeInformation.ProcessArchitecture} | " +
             $".NET {Environment.Version} | {RuntimeInformation.OSDescription} | {Environment.MachineName}");
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

    public static void Metric(MetricRow r)
    {
        try
        {
            string line = string.Join(',',
                Stamp(), r.Event, r.Scenario, r.Framework,
                I(r.Rows), I(r.Frags), I(r.Depth), I(r.Items), I(r.FragTotal),
                F(r.GenMs), F(r.LayoutMs), F(r.WarmupWorstMs),
                F(r.RBuildMs), F(r.RReconcileMs), F(r.REffectsMs),
                F(r.RtAvgFps, 0), F(r.RtMinFps, 0), F(r.RtWorstMs),
                F(r.ManagedMB), F(r.WorkingSetMB), Csv(r.Note));
            lock (Gate)
                File.AppendAllText(CsvPath, line + Environment.NewLine);
        }
        catch { }
    }

    private static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    private static string I(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
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
                if (d.GetFiles("ReactorPerfLab.csproj").Length > 0)
                    return Path.Combine(d.FullName, "logs");
                d = d.Parent;
            }
        }
        catch { }
        return Path.Combine(AppContext.BaseDirectory, "logs");
    }
}
