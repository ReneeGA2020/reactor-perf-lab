# BlazorTriggerBench

Blazor 侧的对照：用 **Blazor Hybrid（MAUI 宿主，WebView2 内进程渲染）** 渲染与 `ReactorPerfLab`
**同一份语句行负载**（变长异构片段，含真实 `<button>`/`<input>`），量化 Blazor 的渲染/滚动表现。

## 为什么用 MAUI 宿主（而你们真实是 WinUI 宿主）

我们要量的"列表渲染 / 滚动 / FPS"由 **WebView2(Chromium) + 进程内 Blazor** 决定，与外层宿主是 MAUI 还是
WinUI **无关**（同一个 Chromium、同样进程内 .NET）。宿主差异只影响合成/透明/airspace —— 那是 Blazor 的已知短板，
已在 `ReactorCompositionSpike` 单独覆盖。

注意两点口径：
1. 这是 Blazor 的**乐观基线**：MAUI 标准 BlazorWebView 不走你们 WinUI 版为透明加的 **composition 捕获**那条路径，
   后者可能还有额外开销。
2. 用 **Hybrid 而非 WASM** 是为了**公平**——Hybrid 里 Blazor 跑原生 .NET 速度；WASM 会用更慢的 wasm .NET 把 Blazor 冤枉。

## 怎么跑

```pwsh
dotnet build .\BlazorTriggerBench.csproj -c Debug -f net10.0-windows10.0.19041.0 -p:Platform=x64
# exe: bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\BlazorTriggerBench.exe
```

## 指标（JS rAF）

- **live FPS**：rAF 实时帧率（手动滚动时看）。
- **首屏渲染 ms**：从"生成"到首帧渲染完（Blazor Stopwatch；Virtualize 只渲视口，和 Reactor/XAML 的虚拟化首帧可比）。
- **DOM 节点数**：列表内 DOM 元素数（Virtualize 应只保留视口附近）。
- **跳变压测**：JS 用 van der Corput 在整列范围内每帧大跳 6s（对应 ReactorPerfLab 的跳变压测），报告 avg/min fps + 最差帧 ms。

## 跨框架比较口径

rAF FPS 与 WinUI 的 `CompositionTarget.Rendering` 不同口径，且都被 vsync 封顶（本机 144Hz）——所以**别只看 FPS 数字**。
更有意义的跨框架信号：**最差帧 ms（卡顿）/ 首屏渲染 ms / DOM(元素)数 / 滚动时的空白与体感**。
结果填进 `../ReactorPerfLab/EVALUATION.md` 的 Blazor 一节。

## 关键文件

- `Bench/StatementModel.cs` — 与 ReactorPerfLab 一致的语句行模型与数据工厂（CSS 配色）。
- `Components/Pages/Home.razor` — 控件 + `<Virtualize>` 行渲染。
- `wwwroot/bench.js` — rAF FPS / 跳变压测 / DOM 计数。
