# ReactorCompositionSpike

验证 **microsoft-ui-reactor 作为 native UI 能透明合成在实时 GPU 内容（D3D11 SwapChain）之上** ——
这正是 Blazor/WebView2 做不到的死穴（WebView 在 XAML 里是个不透明的 airspace 矩形，没法透明、没法叠 SwapChain）。
对一个 3D 游戏编辑器（UI 浮在 3D 视口上）这是决定性能力。

PerfLab 专注纯框架性能对比；合成/透明相关的试验都放这里（带 D3D/Vortice 依赖，不污染 PerfLab）。

## 怎么跑

```pwsh
dotnet build .\ReactorCompositionSpike.csproj -c Debug -p:Platform=x64
.\bin\x64\Debug\net10.0-windows10.0.22621.0\ReactorCompositionSpike.exe
```

## 看什么

- **底层**：`SwapChainPanel` + D3D11（Vortice.Windows）每帧清屏为循环变化的颜色 —— 模拟"实时 3D 视口"。
  通过 `ISwapChainPanelNative.SetSwapChain` 绑定（`Composition/SwapChainPanelInterop.cs`）。
- **上层**：透明的 `ReactorHostControl`，挂一套 Reactor 原生 UI（半透明面板 + 留白）。
  留白处与半透明面板背后应能看到底层 D3D 动画 → 证明原生透明合成。
- **左上状态栏**：`swapchain present FPS`，以及 **重叠加: 开/关** 开关 —— 开启后在 swapchain 上叠 200 行
  半透明原生行（合成器 alpha overdraw 压力），观察 present FPS 是否被拖低 = **合成器争用**程度。
  这回答了"真实 3D 编辑器里 重 UI 与实时 SwapChain 共存时性能是否扛得住"，而这是 PerfLab 纯 UI 对比测不到的。

## 方块自绘 (Win2D) — "快方块"天花板

主窗口的 **"方块自绘 (Win2D)"** 按钮打开 `BlockCanvasWindow`：一个 Win2D `CanvasControl` **即时绘制**
成千上万个圆角彩色 token 行（kind 徽章 + 片段 pill，带文字、按缩进嵌套），**只画可见行**（自绘虚拟化）。
滚轮 / 自动滚动 + FPS 表。要点：把"方块数"拉到 5万、20万，FPS 仍接近满帧（每帧只画 ~视口那几十行）——
即游戏引擎画布式效率，但它是原生栈里的 XAML 元素（可原生合成）。用来对照"原生 UIElement 块（重，扛不住）
vs 原生自绘块（这个，快）vs DOM 块（Blazor）"。

## 关键文件

- `Composition/SwapChainRenderer.cs` — D3D11 设备/交换链/渲染循环 + present FPS。
- `Composition/SwapChainPanelInterop.cs` — `ISwapChainPanelNative` 互操作（Marshal QI 绑定交换链）。
- `MainWindow.xaml(.cs)` — SwapChainPanel + 透明 Reactor 叠加 + 状态栏/开关；`Overlay(heavy)` 构建轻/重 UI。
