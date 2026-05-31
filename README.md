# Reactor Perf Lab

复杂模板**列表/树**性能对比试验台：在同一台机器上对比
**WinUI XAML / microsoft-ui-reactor / Blazor (Hybrid) / MAUI 原生**，
并验证 Reactor 作为原生 UI 的**透明 / SwapChain 合成**能力（Blazor 做不到）。

起因与完整结论见 [`ReactorPerfLab/EVALUATION.md`](ReactorPerfLab/EVALUATION.md)。

## 子项目

| 目录 | 说明 |
|------|------|
| [`ReactorPerfLab/`](ReactorPerfLab/) | 主对比台：WinUI XAML vs Reactor，三/四档渲染（多元素 / RichText 轻量 / 可交互 XamlHost / 可交互 声明式），跳变压测 / 空白采样 / 片段扫描 / 内存，全套日志。 |
| [`ReactorCompositionSpike/`](ReactorCompositionSpike/) | 透明 / 合成验证：透明 Reactor UI 叠在实时 D3D11 `SwapChainPanel` 上（Vortice），present FPS + 轻/重叠加争用。 |
| [`BlazorTriggerBench/`](BlazorTriggerBench/) | Blazor 对照：MAUI Blazor Hybrid（WebView2）渲染同一份语句行；含 MAUI 原生 tab。JS rAF 计量。 |
| [`TreeViewHeteroVerify/`](TreeViewHeteroVerify/) | 最初的 #447 正确性 repro（异构 TreeView）。 |
| `microsoft-ui-reactor/` | 被引用的 **Reactor fork**（见下）。 |

## 依赖与构建

各项目通过相对 `ProjectReference`（`..\microsoft-ui-reactor\src\Reactor\Reactor.csproj`）引用 fork 源码。
**lab 依赖 fork 上的自定义改动**（`RichTextHyperlink.OnClick` 等），位于 fork 的 **`perf-lab`** 分支。

```pwsh
# 性能测量用 Release，直接跑 exe（别挂调试器）
dotnet build .\ReactorPerfLab\ReactorPerfLab.csproj -c Release -p:Platform=x64
dotnet build .\ReactorCompositionSpike\ReactorCompositionSpike.csproj -c Debug -p:Platform=x64
dotnet build .\BlazorTriggerBench\BlazorTriggerBench.csproj -c Debug -f net10.0-windows10.0.19041.0 -p:Platform=x64
```

要求：.NET 10 SDK、WindowsAppSDK 2.0.1、`wasm-tools` + `maui-windows` 工作负载（仅 BlazorTriggerBench 需要）。

## 发布到 GitHub（fork 用 submodule）

当前是本地源码引用；fork 暂以 sibling 形式放在 `microsoft-ui-reactor/`（已在 `.gitignore` 中忽略）。
发布时把它正式化为 submodule（钉在 `perf-lab` 分支的 commit）：

```pwsh
# 1) 先把 fork 的 perf-lab 分支推到你的 fork 远端
git -C microsoft-ui-reactor push -u origin perf-lab

# 2) 在本 lab 仓库里：从 .gitignore 删掉 /microsoft-ui-reactor/ 那行，然后
git submodule add -b perf-lab https://github.com/ReneeGA2020/microsoft-ui-reactor microsoft-ui-reactor
git commit -am "Add microsoft-ui-reactor fork as submodule (perf-lab)"
git push

# 克隆方：git clone --recursive <lab-repo>   （或 git submodule update --init）
```

> 若 `git submodule add` 因目录已存在而报错：把现有 `microsoft-ui-reactor/` 暂移走再执行 add，让它重新 clone。
> 将来 fork 稳定后，可改为发布版本化 NuGet 包、lab 用 `PackageReference` 钉版本，彻底去掉 submodule。

## 维护：同步 upstream

让 fork（`microsoft-ui-reactor`）跟上 upstream（microsoft/microsoft-ui-reactor），并让 lab 跟上 fork：

```pwsh
# 1) fork: main 快进到 upstream（= GitHub 的 "Sync fork"）
git -C microsoft-ui-reactor fetch upstream
git -C microsoft-ui-reactor checkout main
git -C microsoft-ui-reactor merge --ff-only upstream/main
git -C microsoft-ui-reactor push origin main

# 2) fork: 把 perf-lab（= main + 本仓自定义改动，如 RichTextHyperlink.OnClick）rebase 到新 main
git -C microsoft-ui-reactor checkout perf-lab
git -C microsoft-ui-reactor rebase main          # 有冲突就解（多在 reconciler 的 mount/update 处）

# 3) 重建 + 冒烟，确认对新版 Reactor 仍能编译/渲染（务必在 bump pin 前做）
dotnet build ReactorPerfLab\ReactorPerfLab.csproj -c Release -p:Platform=x64

# 4) force-push perf-lab（步骤 2 改写了历史）+ 更新 lab 的 submodule pin
git -C microsoft-ui-reactor push --force-with-lease origin perf-lab
git add microsoft-ui-reactor
git commit -m "Bump fork submodule to latest upstream"
git push
```

> 步骤 1 若不是快进（fork main 有独有提交而分叉），改用 `git rebase upstream/main` 或合并并解冲突。
> 自定义改动建议都只放在 `perf-lab`（保持 `main` == upstream，方便随时快进 + 把单个改动 cherry-pick 成干净分支去提 PR）。
