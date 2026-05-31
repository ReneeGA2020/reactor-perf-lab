# ReactorPerfLab

复杂模板 **TreeView** 性能对比试验台：在**同一个 WinUI 进程**内对比
**WinUI XAML 原生 TreeView**、**microsoft-ui-reactor `TreeView<T>`**，
后续再接入 **Blazor (Hybrid/WebView2)**。

目标：评估 microsoft-ui-reactor 能否绕开现有 WinUI XAML 在复杂/异构模板 TreeView
上的 bug 与性能困境（参考此前 SCE 的 `TreeViewVirtualizationTest`，其 XAML 表现"无法接受"）。

## 怎么跑

**性能测量一律用 Release**（Debug 关 JIT 优化、带额外检查；且 Reactor 的 per-reconcile 计数器是
DEBUG-only 的额外开销，对它不公平）。直接跑 exe，不要挂调试器。

```pwsh
# 测量用（Release）
dotnet build .\ReactorPerfLab.csproj -c Release -p:Platform=x64
.\bin\x64\Release\net10.0-windows10.0.22621.0\ReactorPerfLab.exe

# 开发迭代用（Debug）
dotnet build .\ReactorPerfLab.csproj -c Debug -p:Platform=x64
```

> 项目位于 reactor fork 仓库之外，不会继承其 `Directory.Build.props`，因此在 csproj 里
> 显式钉了 `WindowsAppSDK 2.0.1` + `WindowsAppSDKSelfContained=true`（与 `TreeViewHeteroVerify` 一致）。
> 通过 `ProjectReference` 直接引用 `..\microsoft-ui-reactor\src\Reactor\Reactor.csproj`，即对当前 fork 分支编译。

## 界面与指标

- **框架切换**：下拉选择 `WinUI XAML TreeView` / `Reactor TreeView<T>`，同一份数据即时切换。
- **渲染模式**（仅 ListView 语句行场景）三档：
  - `多元素 (重)` = 每片段一个 UIElement（TextBlock/Border/Button/CheckBox）。
  - `RichText 轻量` = 每行折叠成**一个 RichTextBlock**，片段全是带色 `Run`（轻量 inline，非 UIElement）。最便宜，但失去逐片段交互。
  - `可交互 (XamlHost)` = 静态 `Run` + 可点 `Hyperlink` + 按钮/复选框 `InlineUIContainer`。XAML 侧直接建原生 `RichTextBlock`；
    Reactor 侧用 `XamlHostElement` 挂同一个原生 RichTextBlock。
  - `可交互 (声明式)` = **Reactor 纯声明式**，用 fork 新增的 `Hyperlink(text, onClick)`（带 Click 的轻量 inline，无 XamlHost、无 InlineUIContainer——按钮/复选框降级为可点文本）。留在 Reactor 协调模型内、点击可接 setState。
  共享 `Xaml/RichTextRow.cs` 的 `Build(row, interactive)`（XAML 两个可交互档都走它）。实测可交互冷启动首帧 ~103–146ms（500×14），XamlHost 与声明式基本持平。
  > fork 改动：给 `RichTextHyperlink` 加了 `OnClick`/`Foreground`/`FontSize`/`IsBold` + `Hyperlink(text, Action)` 工厂；
  > 在 `Reconciler.Update.cs` 的 `MountInline` 里把 `Hyperlink.Click → OnClick` 接上（富文本整块重建 → 回收安全）。
- **规模**：根数 / 子数 / 深度可调，点"生成 / 重建"。节点总数 ≈ 根数 × (子数^(深度+1)−1)/(子数−1)。
- **滚动 FPS（分两段）**：基于 `CompositionTarget.Rendering`（合成器帧）—— 对 XAML 与 Reactor 公平。
  - **首次布局（warm-up）**：只报"最差帧耗时"（首帧卡顿），Reactor 已知首帧较慢就显示在这。
  - **运行时**：cur/avg/min/max 只在视图稳定后才累计，不被首帧卡顿污染。自适应切换：持续 warm-up
    直到出现一个最差帧 < 50ms 的平稳窗口（适配 Reactor 大数据集首帧很长）。
  （Blazor 在 WebView2 内合成，宿主侧量不到，需 JS 侧 rAF。）
- **数据生成 / 首次布局耗时**：分别用 `Stopwatch` 计数据工厂；首次布局用一次性 `LayoutUpdated` 近似。
  Reactor 额外显示其内部 `OnRenderComplete(build/reconcile/effects)` 三段耗时。
- **展开 / 折叠全部**：两个框架底层都是原生 `Microsoft.UI.Xaml.Controls.TreeView`，
  统一在宿主子树里找到它、驱动其生成的 `TreeViewNode`，计设置耗时与布局耗时。
- **内存**：触发完整 GC 后采样托管堆 + 进程工作集。
- **跳变压测**：用 van der Corput 序列在整个范围内快速大跳（对应"滚动条跳变"——视口瞬移、整屏回收，
  两框架跳到同样位置，公平可复现），输出 运行时 avg/min FPS + 最差帧 ms。
  注意：**FPS 在此会骗人**——WinUI ListView 快速跳变时先画空白占位（极便宜→FPS 高）再推迟实化内容，
  所以 XAML 可能 FPS 不低却满屏空白行（体感差）。用下面的"空白恢复"看真实差距。
- **空白恢复**（关键指标）：在**连续快跳（16ms/次，6s）过程中每帧采样**视口是否实化出内容
  （`ItemsStackPanel.FirstVisibleIndex/LastVisibleIndex`→在视口 5 个采样点 `ContainerFromIndex`→
  实化 `TextBlock` 数 ≥2 才算填充；占位行=0）。报告**空白帧占比%**（+同期 fps/最差帧）。
  注意必须在连续快跳过程中采样：WinUI 的空白占位只在持续高速滚动时触发；单次瞬跳后那一个布局 pass
  通常就同步实化了，等一会再看会漏判。这条才对应你滚动时看到空白行的体感。预期 XAML 高、Reactor ≈0。
- **片段扫描**：对一组 片段/行 值（8/16/24/32/48/64）× **6 个组合**（XAML/Reactor × 重/轻/可交互）自动重建，
  每格记首帧最差帧 ms，弹窗展示对照表（单元格=最差帧 ms）并写入 `sweep_results.txt`（exe 目录）；每格也进 CSV（`Note=render=...`）。
  一次出齐"重 vs 轻/可交互的数量级差、可交互的额外成本、XAML vs Reactor 在各档的相对位置"。约 1 分钟；跑完恢复原框架/模式。

## 日志（便于事后分析）

所有测量都落盘到项目下 `logs/`（从 exe 向上找到 .csproj 所在目录）：
- `perflab.log` — 人类可读，带时间戳：session 启动（构建配置/架构/.NET/OS）、每次构建、自动滚动、扫描每格、内存。
- `perflab-metrics.csv` — 机器可读，固定列（time,event,scenario,framework,rows,frags,depth,items,fragTotal,
  genMs,layoutMs,warmupWorstMs,rBuildMs,rReconcileMs,rEffectsMs,rtAvgFps,rtMinFps,rtWorstMs,managedMB,workingSetMB,note）。
  `event` ∈ build/scroll/sweep/memory，直接丢进 Excel / pandas 透视。
- 交互式构建在最后一次重建 ~1.5s 后去抖记一行（此时首帧布局与 Reactor 三段计时都已就绪）；扫描/滚动期间不重复记。

## 里程碑

1. **最小可跑（当前）**：XAML 原生 TreeView vs Reactor `TreeView<T>`，**单一同构复杂模板**
   （无 `ItemTemplateSelector` → 不触发原生 TreeView 的虚拟化回收崩溃）。FPS / 计时 / 内存 / 展开折叠齐活。
2. **Blazor Hybrid**：移植 SCE 的自定义 `BlazorWebView`（约 14 个必需文件，跳过 Composition 那套），
   引入 ASP.NET Core Components WebView 包；Blazor 的 FPS 在 JS 侧用 rAF 量。
3. **异构 3 模板压力测试**：XAML 侧必须改用自定义 `TreeViewEx`——
   原生 TreeView + `ItemTemplateSelector` 在虚拟化回收时会错配类型/模板而**崩溃**，
   且 ListView 那套"双参数 `SelectTemplateCore`"绕过手段对 TreeView 不适用（会丢掉子树 `ItemsSource`，退化成 ListView）。
   Reactor 的 `TreeView<T>` 无此问题（单 ContentControl 模板 + 按容器命令式挂载，issue #447）。

## 关键文件

- `Model/Node.cs` — 节点模型（A/B/C 三类）与数据工厂。
- `Reactor/ReactorTreeBench.cs` — Reactor 侧 `TreeView<Node>`，view-builder 对齐 XAML 模板。
- `MainWindow.xaml(.cs)` — 试验台：切换、规模、指标、宿主容器、展开/折叠、测内存。
- `Perf/FpsCounter.cs` — 合成器帧率计。
