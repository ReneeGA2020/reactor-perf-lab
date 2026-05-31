using System.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using ReactorPerfLab.Model;
using ReactorPerfLab.Perf;
using ReactorPerfLab.ReactorViews;
using ReactorPerfLab.Xaml;
using XamlTreeView = Microsoft.UI.Xaml.Controls.TreeView;

namespace ReactorPerfLab;

public sealed partial class MainWindow : Window
{
    private readonly FpsCounter _fps = new();

    // Data for whichever scenario is active.
    private IReadOnlyList<Node> _roots = System.Array.Empty<Node>();
    private IReadOnlyList<StatementRow> _rows = System.Array.Empty<StatementRow>();
    private int _itemCount;
    private int _fragTotal;

    private double _lastGenMs;
    private double _lastLayoutMs;
    private string _reactorInternal = string.Empty;
    private double? _rBuildMs, _rReconcileMs, _rEffectsMs; // Reactor per-phase first-render ms

    private DispatcherTimer? _logTimer; // debounced "build" metric logging

    private ReactorHostControl? _reactorHost;
    private bool _loaded;
    private bool _busy; // true during scripted auto-scroll / sweep

    private Stopwatch? _layoutSw;
    private bool _reactorTimingCaptured;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1280, 880));

        _fps.Updated += OnFps;
        PerfLog.Session();

        _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _logTimer.Tick += OnLogTimerTick;

        if (Content is FrameworkElement root)
            root.Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _fps.Dispose();
            _reactorHost?.Dispose();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        UpdateLabelsForScenario();
        _fps.Start();
        Generate();
        ResultText.Text = $"日志目录: {PerfLog.Dir}";
    }

    private string CurrentScenario =>
        (ScenarioCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "List";

    private string CurrentFramework =>
        (FrameworkCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Xaml";

    private bool IsList => CurrentScenario == "List";

    private string CurrentRenderMode =>
        (RenderModeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Heavy";

    private void OnScenarioChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || _busy) return;
        UpdateLabelsForScenario();
        Generate(); // shape differs per scenario → rebuild data
    }

    private void OnFrameworkChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || _busy) return;
        BuildView(); // reuse current data
    }

    private void OnRenderModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || _busy) return;
        BuildView(); // reuse current data
    }

    private void OnGenerate(object sender, RoutedEventArgs e) => Generate();

    private void UpdateLabelsForScenario()
    {
        if (IsList)
        {
            Lbl1.Text = "行数";
            Lbl2.Text = "片段/行";
            Lbl3.Text = "最大缩进";
            ExpandBtn.IsEnabled = false;
            CollapseBtn.IsEnabled = false;
        }
        else
        {
            Lbl1.Text = "根数";
            Lbl2.Text = "子数";
            Lbl3.Text = "深度";
            ExpandBtn.IsEnabled = true;
            CollapseBtn.IsEnabled = true;
        }
    }

    private void Generate()
    {
        int a = (int)RootsBox.Value;
        int b = (int)ChildrenBox.Value;
        int c = (int)DepthBox.Value;

        var sw = Stopwatch.StartNew();
        if (IsList)
        {
            (_rows, _fragTotal) = StatementData.Build(a, b, c);
            _itemCount = _rows.Count;
        }
        else
        {
            (_roots, _itemCount) = TreeData.Build(a, b, c);
            _fragTotal = 0;
        }
        sw.Stop();
        _lastGenMs = sw.Elapsed.TotalMilliseconds;

        BuildView();
    }

    private void BuildView()
    {
        DetachLayoutProbe();
        if (_reactorHost != null)
        {
            _reactorHost.Dispose();
            _reactorHost = null;
        }
        HostContainer.Child = null;

        _reactorInternal = string.Empty;
        _reactorTimingCaptured = false;
        _rBuildMs = _rReconcileMs = _rEffectsMs = null;
        _fps.BeginWarmup();

        _layoutSw = Stopwatch.StartNew();
        HostContainer.LayoutUpdated += OnFirstLayout;

        if (CurrentFramework == "Reactor")
        {
            var host = new ReactorHostControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            host.OnRenderComplete = (treeBuildMs, reconcileMs, effectsMs) =>
            {
                if (_reactorTimingCaptured) return;
                _reactorTimingCaptured = true;
                _rBuildMs = treeBuildMs;
                _rReconcileMs = reconcileMs;
                _rEffectsMs = effectsMs;
                _reactorInternal =
                    $" | Reactor内部: build {treeBuildMs:F1} / reconcile {reconcileMs:F1} / effects {effectsMs:F1} ms";
                DispatcherQueue.TryEnqueue(UpdateMetrics);
            };
            if (IsList)
            {
                // Interactive mode hosts a native RichTextBlock via XamlHostElement — ensure the handler is registered.
                if (CurrentRenderMode == "Interactive") XamlInterop.Register(host.Reconciler);
                host.Mount(new ReactorStatementBench(_rows, CurrentRenderMode));
            }
            else
            {
                host.Mount(new ReactorTreeBench(_roots));
            }
            _reactorHost = host;
            HostContainer.Child = host;
        }
        else if (IsList)
        {
            var list = new ListView
            {
                ItemsSource = _rows,
                SelectionMode = ListViewSelectionMode.Single,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            if (CurrentRenderMode == "Heavy")
            {
                list.ItemTemplateSelector = (DataTemplateSelector)RootGrid.Resources["StmtSelector"];
            }
            else
            {
                // Light / Interactive: one RichTextBlock per row, built in ContainerContentChanging.
                list.ItemTemplate = (DataTemplate)RootGrid.Resources["LightRowTemplate"];
                list.ContainerContentChanging += OnLightContainer;
            }
            HostContainer.Child = list;
        }
        else
        {
            var tree = new XamlTreeView
            {
                ItemTemplate = (DataTemplate)RootGrid.Resources["NodeTemplate"],
                ItemsSource = _roots,
                SelectionMode = TreeViewSelectionMode.Single,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            HostContainer.Child = tree;
        }

        UpdateMetrics();
        ScheduleBuildLog();
    }

    // Debounced: log a complete "build" metric ~1.5 s after the last interactive rebuild, by which
    // time first layout + Reactor's OnRenderComplete have landed. Suppressed during sweep/scroll
    // (those log their own rows).
    private void ScheduleBuildLog()
    {
        if (_busy || _logTimer == null) return;
        _logTimer.Stop();
        _logTimer.Start();
    }

    private void OnLogTimerTick(object? sender, object e)
    {
        _logTimer?.Stop();
        bool list = IsList;
        var row = new MetricRow
        {
            Event = "build",
            Scenario = CurrentScenario,
            Framework = CurrentFramework,
            Rows = (int)RootsBox.Value,
            Frags = list ? (int)ChildrenBox.Value : null,
            Depth = (int)DepthBox.Value,
            Items = _itemCount,
            FragTotal = list ? _fragTotal : null,
            GenMs = _lastGenMs,
            LayoutMs = _lastLayoutMs,
            WarmupWorstMs = _fps.WarmupWorstFrameMs,
            RBuildMs = _rBuildMs,
            RReconcileMs = _rReconcileMs,
            REffectsMs = _rEffectsMs,
            Note = list ? $"render={CurrentRenderMode}" : "",
        };
        PerfLog.Metric(row);
        PerfLog.Line($"build [{row.Scenario}/{row.Framework}{(list ? "/" + CurrentRenderMode : "")}] rows={row.Items} frags/行={(list ? ChildrenBox.Value : 0)} " +
                     $"fragTotal={_fragTotal} | gen {_lastGenMs:F1}ms | 首帧布局 {_lastLayoutMs:F1}ms | 首帧最差 {_fps.WarmupWorstFrameMs:F0}ms" +
                     (_rBuildMs is { } b ? $" | Reactor build/reconcile/effects {b:F1}/{_rReconcileMs:F1}/{_rEffectsMs:F1}ms" : ""));
    }

    private void OnFirstLayout(object? sender, object e)
    {
        DetachLayoutProbe();
        if (_layoutSw != null)
        {
            _layoutSw.Stop();
            _lastLayoutMs = _layoutSw.Elapsed.TotalMilliseconds;
            _layoutSw = null;
        }
        UpdateMetrics();
    }

    private void DetachLayoutProbe() => HostContainer.LayoutUpdated -= OnFirstLayout;

    // ── XAML lightweight row: one RichTextBlock per realized container (Light=Runs, Interactive=+Hyperlink/InlineUIContainer) ──
    private void OnLightContainer(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not StatementRow row) return;
        // XAML builds the native RichTextBlock directly; both interactive variants render the same (XAML has no
        // Reactor-declarative path). Light = plain Runs.
        if (args.ItemContainer.ContentTemplateRoot is Border border)
            border.Child = RichTextRow.Build(row, interactive: CurrentRenderMode != "Light");
        args.Handled = true;
    }

    private void OnFps(FpsCounter.Snapshot s)
    {
        FpsText.Text = s.InWarmup
            ? $"[首次布局…] 最差帧 {s.WarmupWorstFrameMs,5:F0} ms  (运行时 FPS 稳定后开始统计)"
            : $"运行时 FPS  cur {s.Cur,5:F0} | avg {s.Avg,5:F0} | min {s.Min,5:F0} | max {s.Max,5:F0}    首帧最差 {s.WarmupWorstFrameMs:F0} ms";
    }

    private void UpdateMetrics()
    {
        string scope = IsList
            ? $"行: {_itemCount:N0} | 片段总数: {_fragTotal:N0}"
            : $"节点: {_itemCount:N0}";
        string mode = IsList ? $"/{CurrentRenderMode}" : "";
        MetricsText.Text =
            $"{CurrentScenario}/{CurrentFramework}{mode} | {scope} | 数据生成: {_lastGenMs:F1} ms | 首次布局: {_lastLayoutMs:F1} ms{_reactorInternal}";
    }

    // ── Expand / collapse all (TreeView scenario only) ─────────────────────────────────
    private void OnExpandAll(object sender, RoutedEventArgs e) => SetExpansion(true);

    private void OnCollapseAll(object sender, RoutedEventArgs e) => SetExpansion(false);

    private void SetExpansion(bool expand)
    {
        var tree = FindDescendant<XamlTreeView>(HostContainer);
        if (tree == null)
        {
            MemoryText.Text = "展开/折叠: 未在宿主子树中找到原生 TreeView";
            return;
        }

        var setSw = Stopwatch.StartNew();
        foreach (var node in tree.RootNodes)
            SetExpandRecursive(node, expand);
        setSw.Stop();
        double setMs = setSw.Elapsed.TotalMilliseconds;

        var settleSw = Stopwatch.StartNew();
        void OnSettled(object? s, object args)
        {
            HostContainer.LayoutUpdated -= OnSettled;
            settleSw.Stop();
            MemoryText.Text =
                $"{(expand ? "展开" : "折叠")}全部: 设置 {setMs:F1} ms | 布局 {settleSw.Elapsed.TotalMilliseconds:F1} ms";
        }
        HostContainer.LayoutUpdated += OnSettled;
    }

    private static void SetExpandRecursive(TreeViewNode node, bool expand)
    {
        node.IsExpanded = expand;
        foreach (var child in node.Children)
            SetExpandRecursive(child, expand);
    }

    // ── Memory ───────────────────────────────────────────────────────────────────────
    private void OnMeasureMemory(object sender, RoutedEventArgs e)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        long managed = GC.GetTotalMemory(forceFullCollection: true);
        long workingSet = Process.GetCurrentProcess().WorkingSet64;

        double managedMB = managed / 1024.0 / 1024.0;
        double wsMB = workingSet / 1024.0 / 1024.0;
        MemoryText.Text =
            $"内存: 托管堆 {managedMB:F1} MB | 工作集 {wsMB:F1} MB " +
            $"| GC {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}";

        PerfLog.Metric(new MetricRow
        {
            Event = "memory",
            Scenario = CurrentScenario,
            Framework = CurrentFramework,
            Rows = (int)RootsBox.Value,
            Frags = IsList ? (int)ChildrenBox.Value : null,
            Items = _itemCount,
            FragTotal = IsList ? _fragTotal : null,
            ManagedMB = managedMB,
            WorkingSetMB = wsMB,
        });
        PerfLog.Line($"memory [{CurrentScenario}/{CurrentFramework}] items={_itemCount} | 托管堆 {managedMB:F1}MB | 工作集 {wsMB:F1}MB");
    }

    // ── Auto-scroll stress (reproducible runtime FPS) ──────────────────────────────────
    private async void OnAutoScroll(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var sv = FindBestScrollViewer(HostContainer);
        if (sv == null || sv.ScrollableHeight <= 1)
        {
            ResultText.Text = "自动滚动: 未找到可滚动区域（行数太少？先把行数调大并重建）";
            return;
        }

        SetBusy(true);
        ResultText.Text = $"跳变压测中… [{CurrentScenario}/{CurrentFramework}]";
        _fps.BeginMeasure();

        // Aggressive recycling stress: teleport the viewport across the whole range on every tick
        // (the real "scrollbar jump" case where the virtualizer must re-realize a full viewport at
        // once). van der Corput fractions are deterministic & well-spread, so XAML and Reactor runs
        // jump to identical positions → fair, reproducible comparison.
        const double durationSec = 5.0;
        int i = 0;
        var clock = Stopwatch.StartNew();
        var tcs = new TaskCompletionSource();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) =>
        {
            double max = sv.ScrollableHeight;
            sv.ChangeView(null, VanDerCorput(++i) * max, null, disableAnimation: true);
            if (clock.Elapsed.TotalSeconds >= durationSec)
            {
                timer.Stop();
                tcs.TrySetResult();
            }
        };
        timer.Start();
        await tcs.Task;

        ResultText.Text =
            $"跳变压测 {durationSec:F0}s [{CurrentScenario}/{CurrentFramework}]: {i} 次跳变 | " +
            $"avg {_fps.RuntimeAvg:F0} | min {_fps.RuntimeMin:F0} fps | 最差帧 {_fps.RuntimeWorstFrameMs:F0} ms";

        PerfLog.Metric(new MetricRow
        {
            Event = "scroll",
            Scenario = CurrentScenario,
            Framework = CurrentFramework,
            Rows = (int)RootsBox.Value,
            Frags = IsList ? (int)ChildrenBox.Value : null,
            Depth = (int)DepthBox.Value,
            Items = _itemCount,
            FragTotal = IsList ? _fragTotal : null,
            RtAvgFps = _fps.RuntimeAvg,
            RtMinFps = _fps.RuntimeMin,
            RtWorstMs = _fps.RuntimeWorstFrameMs,
            Note = $"{durationSec:F0}s jump-stress, {i} jumps",
        });
        PerfLog.Line($"jump-stress [{CurrentScenario}/{CurrentFramework}] rows={_itemCount} frags/行={(IsList ? ChildrenBox.Value : 0)} " +
                     $"jumps={i} | avg {_fps.RuntimeAvg:F0} | min {_fps.RuntimeMin:F0} fps | 最差帧 {_fps.RuntimeWorstFrameMs:F0}ms");
        SetBusy(false);
    }

    // ── Blank sampling DURING continuous fast jumping (the metric FPS can't show) ──────
    // WinUI's blank-placeholder strategy only kicks in under sustained high scroll velocity, so we
    // sample the viewport on EVERY tick of a continuous rapid-jump loop (not after a settle). A
    // container counts as "blank" if its realized TextBlock count is below threshold (a placeholder
    // row has 0; a realized heavy row has many — the kind-chip alone is 1, so we require ≥2).
    private async void OnBlankRecovery(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var list = FindDescendant<ListViewBase>(HostContainer);
        var sv = FindBestScrollViewer(HostContainer);
        if (list == null || sv == null || sv.ScrollableHeight <= 1)
        {
            ResultText.Text = "空白恢复: 未找到可滚动列表（行数太少？先调大并重建）";
            return;
        }

        SetBusy(true);
        ResultText.Text = $"空白采样中… [{CurrentScenario}/{CurrentFramework}]";
        _fps.BeginMeasure();

        // Sample on CompositionTarget.Rendering (fires per frame, AFTER layout → reflects what is
        // actually on screen this frame). Jump on a ~33ms cadence. Sampling here (not in the timer
        // tick right after ChangeView) is the fix: the tick read the pre-layout, still-populated old
        // viewport; the render frame reflects the new — possibly blank — viewport.
        const double durationSec = 6.0;
        int i = 0, samples = 0, blanks = 0;
        double lastJumpMs = -1000;
        var clock = Stopwatch.StartNew();
        var tcs = new TaskCompletionSource();

        void OnFrame(object? s, object e2)
        {
            double t = clock.Elapsed.TotalMilliseconds;
            if (i > 0) // start sampling after the first jump
            {
                samples++;
                if (!ViewportPopulated(list)) blanks++;
            }
            if (t - lastJumpMs >= 33)
            {
                sv.ChangeView(null, VanDerCorput(++i) * sv.ScrollableHeight, null, disableAnimation: true);
                lastJumpMs = t;
            }
            if (t >= durationSec * 1000)
            {
                CompositionTarget.Rendering -= OnFrame;
                tcs.TrySetResult();
            }
        }
        CompositionTarget.Rendering += OnFrame;
        await tcs.Task;

        double blankPct = samples > 0 ? 100.0 * blanks / samples : 0;
        ResultText.Text =
            $"空白采样 [{CurrentScenario}/{CurrentFramework}]: 空白帧占比 {blankPct:F0}% ({blanks}/{samples}) | " +
            $"同时 avg {_fps.RuntimeAvg:F0} fps | 最差帧 {_fps.RuntimeWorstFrameMs:F0} ms";

        PerfLog.Metric(new MetricRow
        {
            Event = "blank",
            Scenario = CurrentScenario,
            Framework = CurrentFramework,
            Rows = (int)RootsBox.Value,
            Frags = IsList ? (int)ChildrenBox.Value : null,
            Depth = (int)DepthBox.Value,
            Items = _itemCount,
            FragTotal = IsList ? _fragTotal : null,
            RtAvgFps = _fps.RuntimeAvg,
            RtWorstMs = _fps.RuntimeWorstFrameMs,
            Note = $"blank {blankPct:F0}% ({blanks}/{samples}) over {durationSec:F0}s jumps, fps {_fps.RuntimeAvg:F0}",
        });
        PerfLog.Line($"blank-sample [{CurrentScenario}/{CurrentFramework}] rows={_itemCount} frags/行={(IsList ? ChildrenBox.Value : 0)} " +
                     $"| 空白帧 {blankPct:F0}% ({blanks}/{samples}) | avg {_fps.RuntimeAvg:F0} fps | 最差帧 {_fps.RuntimeWorstFrameMs:F0}ms");
        SetBusy(false);
    }

    /// <summary>
    /// True if the visible item containers have realized content (≥2 TextBlocks), i.e. not blank
    /// placeholders. Samples 5 points across the viewport so blanks anywhere (not just the top) count.
    /// </summary>
    private static bool ViewportPopulated(ListViewBase list)
    {
        if (list.ItemsPanelRoot is not ItemsStackPanel panel) return true; // can't tell → don't count as blank
        int first = panel.FirstVisibleIndex, last = panel.LastVisibleIndex;
        if (first < 0 || last < first) return false; // viewport not realized yet → blank
        int span = last - first;
        foreach (int i in new[] { first, first + span / 4, first + span / 2, first + 3 * span / 4, last })
        {
            if (list.ContainerFromIndex(i) is not DependencyObject c) return false; // container not realized
            if (CountDescendants<TextBlock>(c, 2) < 2) return false;                 // realized but blank/placeholder
        }
        return true;
    }

    private static int CountDescendants<T>(DependencyObject root, int cap) where T : DependencyObject
    {
        int n = 0;
        Walk(root);
        return n;

        void Walk(DependencyObject node)
        {
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count && n < cap; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T) n++;
                if (n < cap) Walk(child);
            }
        }
    }

    /// <summary>van der Corput (base 2) — deterministic low-discrepancy fraction in [0,1) for jump targets.</summary>
    private static double VanDerCorput(int n)
    {
        double q = 0, bk = 0.5;
        while (n > 0)
        {
            q += (n & 1) * bk;
            n >>= 1;
            bk *= 0.5;
        }
        return q;
    }

    // ── Fragments/row sweep (first-frame cost vs complexity, both frameworks) ───────────
    private static readonly int[] SweepFrags = { 8, 16, 24, 32, 48, 64 };

    private async void OnSweep(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!IsList)
        {
            ResultText.Text = "片段扫描仅用于 ListView 语句行场景";
            return;
        }

        SetBusy(true);
        int rows = (int)RootsBox.Value;
        double savedFrag = ChildrenBox.Value;
        string savedMode = CurrentRenderMode;
        string savedFw = CurrentFramework;

        // Sweep every (framework × render-mode) combo across fragment counts → one comparison curve.
        // Reported value is the warm-up WORST FRAME (ms) — the trustworthy first-frame cost.
        var combos = new (string Fw, string Mode, string Label)[]
        {
            ("Xaml", "Heavy", "XAML重"), ("Xaml", "Light", "XAML轻"), ("Xaml", "Interactive", "XAML交"),
            ("Reactor", "Heavy", "R重"), ("Reactor", "Light", "R轻"),
            ("Reactor", "Interactive", "R交H"), ("Reactor", "InteractiveDecl", "R交声"),
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"片段/行 扫描   行数={rows}   (单元格 = 首帧最差 ms；越低越好)");
        sb.AppendLine("片段/行 | " + string.Join(" | ", combos.Select(c => $"{c.Label,6}")));
        sb.AppendLine(new string('-', 9 + combos.Length * 9));
        PerfLog.Line($"sweep start | rows={rows} | frags={string.Join('/', SweepFrags)} | combos=框架×重/轻/交");

        foreach (int v in SweepFrags)
        {
            ChildrenBox.Value = v;
            var cells = new double[combos.Length];
            for (int c = 0; c < combos.Length; c++)
            {
                ResultText.Text = $"扫描中… 片段/行={v} [{combos[c].Label}]";
                var (layout, worst) = await MeasureFirstFrameAsync(combos[c].Fw, combos[c].Mode);
                cells[c] = worst;
                PerfLog.Metric(new MetricRow
                {
                    Event = "sweep", Scenario = "List", Framework = combos[c].Fw,
                    Rows = rows, Frags = v, Items = _itemCount, FragTotal = _fragTotal,
                    GenMs = _lastGenMs, LayoutMs = layout, WarmupWorstMs = worst,
                    RBuildMs = _rBuildMs, RReconcileMs = _rReconcileMs, REffectsMs = _rEffectsMs,
                    Note = $"render={combos[c].Mode}",
                });
            }
            sb.AppendLine($"{v,6}  | " + string.Join(" | ", cells.Select(x => $"{x,6:F0}")));
            PerfLog.Line($"sweep frags={v,3} | " + string.Join(" ", combos.Select((c, i) => $"{c.Label}={cells[i]:F0}")));
        }

        // Restore the user's selections + a clean view (so a later 测内存 isn't on the last sweep build).
        ChildrenBox.Value = savedFrag;
        FrameworkCombo.SelectedIndex = savedFw == "Reactor" ? 1 : 0;
        RenderModeCombo.SelectedIndex = savedMode switch { "Light" => 1, "Interactive" => 2, "InteractiveDecl" => 3, _ => 0 };
        Generate();
        PerfLog.Line("sweep done");

        string text = sb.ToString();
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "sweep_results.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { /* ignore */ }

        ResultText.Text = $"片段扫描完成 → 已写入 {path}";
        SetBusy(false);

        var dialog = new ContentDialog
        {
            Title = "片段/行 扫描结果",
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = text,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    IsTextSelectionEnabled = true,
                },
            },
            CloseButtonText = "关闭",
            XamlRoot = RootGrid.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async Task<(double layoutMs, double worstFrameMs)> MeasureFirstFrameAsync(string framework, string mode)
    {
        // Combos are guarded by _busy → setting SelectedIndex won't trigger BuildView; Generate() rebuilds.
        FrameworkCombo.SelectedIndex = framework == "Reactor" ? 1 : 0;
        RenderModeCombo.SelectedIndex = mode switch { "Light" => 1, "Interactive" => 2, "InteractiveDecl" => 3, _ => 0 };
        Generate(); // rebuilds data + view, calls _fps.BeginWarmup()

        var clock = Stopwatch.StartNew();
        while (_fps.InWarmup && clock.Elapsed.TotalSeconds < 6.0)
            await Task.Delay(80);
        await Task.Delay(120); // small settle margin

        return (_lastLayoutMs, _fps.WarmupWorstFrameMs);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        GenerateBtn.IsEnabled = !busy;
        ScrollBtn.IsEnabled = !busy;
        BlankBtn.IsEnabled = !busy;
        SweepBtn.IsEnabled = !busy;
        ScenarioCombo.IsEnabled = !busy;
        FrameworkCombo.IsEnabled = !busy;
        RenderModeCombo.IsEnabled = !busy;
    }

    // ── Visual-tree helpers ────────────────────────────────────────────────────────────
    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    /// <summary>The scrollable region we should drive — the descendant ScrollViewer with the most content.</summary>
    private static ScrollViewer? FindBestScrollViewer(DependencyObject root)
    {
        ScrollViewer? best = null;
        Walk(root);
        return best;

        void Walk(DependencyObject node)
        {
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is ScrollViewer sv && (best == null || sv.ScrollableHeight > best.ScrollableHeight))
                    best = sv;
                Walk(child);
            }
        }
    }
}
