# 评估小结：microsoft-ui-reactor vs WinUI XAML（vs Blazor）

> 场景：SCE 触发器编辑器式的**重模板列表**（每行=变长异构片段：可编辑值/类型标签/运算符/括号/按钮/复选框），
> 以及"UI 需叠在实时 3D 视口（SwapChain）之上"。问题：Reactor 能否同时解决 WinUI XAML 的性能墙与 Blazor 的非原生痛点。
> 数据来自 `ReactorPerfLab`（Release，x64，本机 144Hz）与 `ReactorCompositionSpike`。日志见 `ReactorPerfLab/logs/`。

## 一句话结论

对"重模板列表 + 叠在 3D 视口上"的场景，**Reactor 同时拿下了 XAML 的性能墙和 Blazor 的非原生/不能合成两个痛点**，是目前看最合适的方向。需权衡的是内存占用与 fork 成熟度。

> **重要校正（纯列表滚动）**：实测 **Blazor 的列表滚动数据很可能是四者最好**（Chromium GPU 合成滚动 + DOM 节点远比 WinUI UIElement 轻）。但这从来不是 Blazor 的痛点（用户确认其触发器不卡）。**决策不取决于列表性能，而取决于原生合成（透明 / SwapChain 嵌入）**——这才是 Reactor 胜出、Blazor 出局的原因。即便纯列表 Reactor 未必赢 Blazor。
> 两点口径保留：① Blazor 的 rAF FPS 同样可能"骗人"（快速跳变时合成器丝滑但 Virtualize 滞后＝空白；未测其空白率）；② 尚未在 500×50 上四者同跑做苹果对苹果。

## 1. 异构模板正确性

- WinUI 原生 **TreeView + `ItemTemplateSelector`** 在虚拟化回收时崩溃；ListView 可用双参数 `SelectTemplateCore` 绕过，但 TreeView 不行（会丢子树 `ItemsSource`，退化为列表）→ 当年只能自写 `TreeViewEx`。
- **Reactor `TreeView<T>` / `ListView<T>` 天然免疫**：单 ContentControl 模板 + 按容器命令式挂载，回收容器不会错配类型（issue #447）。

## 2. 重列表性能（ReactorPerfLab，500 行）

**首帧（首次布局 / 最差帧 ms；Reactor 异步挂载使"首次布局"偏小，看最差帧）**

| 片段/行 | XAML 最差帧 | Reactor 最差帧 |
|--:|--:|--:|
| 8 | 112 | 63 |
| 16 | 131 | 87 |
| 24 | 169 | 130 |
| 32 | 234 | 125 |
| 48 | 349 | 317 |
| 64 | 400 | 289 |

- XAML 首帧随复杂度**陡增**；Reactor 平缓且每档更低。
- Reactor 冷启动首帧略慢（首次 ~225ms vs XAML ~203ms，**一次性**：冷 reconcile ~20ms → 热重建 ~0.4ms）。

**滚动（平滑，6s）**：XAML avg 84 / min 66 / 最差帧 94ms；Reactor avg 101 / min 86 / 最差帧 82ms。Reactor 全面更稳。

**极限（500 行 × 50 片段 ≈ 32k 片段）**：XAML **掉到 1 fps**、首次布局 645ms、滚动出现**大量空白行**（占位符策略：FPS 虚高但内容跟不上＝你们生产里的真实痛点）。Reactor 同负载保持可用、无空白行。

> 注：FPS 在快速滚动下会"骗人"——XAML 画空白占位很便宜→FPS 高，但内容延迟＝体感差。真实差距在"空白行/内容延迟"，不在 FPS 数字。

## 2b. 轻量行（RichTextBlock + 带色 Run）—— 拆掉原生"元素数量墙"

把每行的几十个片段 UIElement 折叠成**一个 RichTextBlock + 带色 Run**（轻量文本 inline，非 UIElement）后，原生侧数量级提升（500 行，Release）：

| 指标 | 重 (多元素) | 轻 (RichText) |
|---|--:|--:|
| XAML 首帧 500×14 | ~215ms | 26–95ms |
| XAML 首帧 500×50 | 336–645ms | **46ms** |
| XAML 跳变 500×14 | avg 6 / 最差 650ms | avg **71** / 最差 85ms |
| XAML 跳变 500×50 | avg 1–2 / 最差 ~1700ms | avg **24** / 最差 178ms |
| Reactor 跳变 500×14 | avg ~6 | avg **68** |
| Reactor 跳变 500×50 | avg 1–2 | avg **28** |
| Reactor 首帧 500×50 | 314–376ms | **41ms** (reconcile 0.4ms) |

**首帧最差帧扫描（500 行，三档 × 两框架，单元格 ms，越低越好）**

| 片段/行 | XAML重 | XAML轻 | XAML交 | R重 | R轻 | R交 |
|--:|--:|--:|--:|--:|--:|--:|
| 8 | 90 | 41 | 66 | 190* | 26 | 55 |
| 16 | 122 | 67 | 70 | 75 | 32 | 61 |
| 24 | 186 | 80 | 132 | 103 | 62 | 78 |
| 32 | 190 | 127 | 109 | 124 | 46 | 93 |
| 48 | 285 | 162 | 114 | 165 | 65 | 109 |
| 64 | 333 | 193 | 168 | 247 | 83 | 163 |

\* R重@8 是整轮首测的冷启动离群。读出：轻量≪重且越复杂差距越大；**Reactor 轻量首帧最低（26–83ms 近平线）**；可交互比纯轻量略高但仍远低于重（交互成本可控）；轻/可交互档 XAML≈Reactor。

结论：
- **元素数量是原生列表性能的决定性杠杆**（重→轻 ~10–24×；首帧最重档 333→83–193ms）。500×50 滚动从 1–2fps（冻死）→ 24–28fps（可用）。
- **轻量模式下 XAML ≈ Reactor**；而且在激进跳变压测里**重模式 XAML 与 Reactor 都崩到 ~6fps**——"Reactor 2× XAML"只在平滑滚动成立。原生列表性能的杠杆是元素数量，不是框架选择。
- 因此**用 RichText/Inline 渲染就能让全原生方案的列表够快**，"为列表性能内嵌 BlazorWebView"的必要性大幅下降。
- 代价：逐片段交互退化为文本；实战用"Inlines 显示 + 少数交互片段 `InlineUIContainer`/`Hyperlink`/overlay"的混合。

## 3. 透明 / SwapChain 原生合成（ReactorCompositionSpike）

- ✅ **透明的 Reactor 原生 UI 能正确合成在实时 D3D11 `SwapChainPanel` 之上**（Vortice + `ISwapChainPanelNative`）；Reactor 按钮还能驱动底层 D3D（暂停/播放/重置动画）。
- 这正是 **Blazor/WebView2 的死穴**：WebView 在 XAML 里是不透明 airspace 矩形，无法透明、无法叠 SwapChain——对 3D 游戏编辑器是硬伤。

## 4. 合成器争用

- 轻/重 UI 叠加在实时 SwapChain 上，present FPS 都顶满 **144（vsync 上限）** → 当前负载下**无争用**，Reactor 半透明 UI 叠加近乎零成本。
- （present 受 vsync 封顶，若要测余量需改测帧时间/CPU；当前结论为"扛得住"。）

## 5. 代价与注意

- **内存**：Reactor 更高（保留式元素树 + Component 模型）。注：现有日志里 XAML(11360 片段) vs Reactor(40380 片段) 非同口径，需同参数干净重测才有定量值；方向上 Reactor 偏高。
- **成熟度**：Reactor 是实验性 fork，API 可能变动。

## 6. Blazor 对比

`BlazorTriggerBench`（**MAUI Blazor Hybrid，Windows**，WebView2 内进程内 Blazor）渲染**同一份语句行模型**
（`<Virtualize>`），JS rAF 量：live FPS / 首屏渲染 ms / DOM 节点数 / 跳变压测(avg/min fps + 最差帧 ms)。

- **宿主代理说明**：列表渲染/滚动由 WebView2+进程内 Blazor 决定，与宿主是 MAUI/WinUI 无关；宿主只影响合成/透明
  （Blazor 已知短板，见 §3）。这是 Blazor 的**乐观基线**（不含你们 WinUI 版 composition 捕获的额外开销）；用 Hybrid 而非 WASM 以保证原生 .NET 速度、不冤枉 Blazor。
- **比较口径**：rAF FPS 与 `CompositionTarget.Rendering` 不同口径且都被 vsync 封顶（144）——**别只看 FPS**，
  看 **最差帧 ms / 首屏渲染 / DOM(元素)数 / 滚动空白与体感**。

**500×50 跳变压测（极端档，本机 144Hz；FPS 口径不同仅供横向参考）**

| 500×50 | avg fps | min fps | 最差帧 | 首屏 ms | DOM/元素 |
|---|--:|--:|--:|--:|--:|
| **Blazor (Hybrid)** | **95** | 18 | **56** | 25 | 3167 |
| Reactor 轻量(RichText) | 28 | 23 | 189 | 41 | — |
| XAML 轻量(RichText) | 24 | 22 | 178 | 46 | — |
| 原生 重(多元素) | 1–2 | — | ~1700 | 336–645 | — |

| 维度 | WinUI XAML | Reactor | Blazor(Hybrid) |
|---|---|---|---|
| 纯列表滚动吞吐 | 重:崩 / 轻:可用 | 重:崩 / 轻:可用 | **最强**（越重越领先） |
| 透明/SwapChain 合成 | 原生可 | **原生可** | ✗（airspace 不透明矩形） |
| 异构模板 selector | TreeView 崩(需 TreeViewEx) | **原生免疫(#447)** | N/A(自绘) |

读法：
- 纯滚动吞吐 **Blazor > 轻量原生 > 重原生**，极端档差距最大（500×50 Blazor 95 vs 轻量原生 24–28）；越重 Blazor 越领先。
- 但 **500×50 远超真实负载**（真实最大触发器 ~50 行/每行几十片段，非 500×50=3.2万片段）；真实规模下轻量原生应接近 vsync，差距大幅缩小。
- 跳变压测是"每帧瞬移随机位置"的极端合成，native 的 ~180ms 单帧来自 ListView 整屏重实化；Blazor 高 FPS 部分来自"先空白后补"（实测有空白但恢复极快）。
- **决策仍取决于原生合成**：主体 Reactor + 轻量行（拿到合成 + 够用列表）；仅当某面板是"超重纯列表且无需合成"才值得为滚动吞吐内嵌 BlazorWebView。

> 跑法：四个对照同参数（500×14、500×50）各跑首屏 + 跳变压测，把数字填进上表。
> - WinUI XAML / Reactor：`ReactorPerfLab`（日志 `ReactorPerfLab/logs/`）。
> - Blazor(Hybrid) / MAUI 原生：`BlazorTriggerBench` 的两个 tab（日志 `BlazorTriggerBench/logs/` 的 `blazor-*` 与 `maui-*`）。MAUI 页切到该 tab 才激活。
>
> 已抓到的 Blazor 数据点：500×14 跳变压测 avg 139 / min 48 fps / 最差帧 20.8ms / DOM ~1190。
> 第 4 列 MAUI 原生（Windows 上是 WinUI handlers 之上的抽象，预期 ≈ WinUI 或略差，不在决策路径，作完整性参考）。
> 口径提醒：Blazor 的 rAF 与 MAUI/WinUI 的 CompositionTarget.Rendering 不同口径且都被 144Hz 封顶——重点看最差帧/首屏/元素数/空白与体感。
