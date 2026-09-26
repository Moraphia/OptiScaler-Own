# OptiScaler Manager

当前稳定版管理器与游戏包：`0.2.4`。管理器 ZIP 不包含游戏端运行库；游戏包在同一 Release 单独发布。法环 ERSS 等付费文件不包含在任何发布包中。

Independent Windows 10/11 x64 desktop manager for OptiScaler. The manager does not replace the in-game ImGui overlay; it handles game discovery, installation, verification, repair, configuration snapshots and release updates.

## 多管线与法环共存方案

游戏详情的“渲染管线”页面同时展示 OptiScaler、ReShade＋RenoDX／DLSS5 Bridge 和 SAOG Magpie；侧边栏“窗口／视频”可在未选择游戏时下载并启动 Magpie。三个后端保持独立，**不会**把 Magpie 或 ReShade 加载到 OptiScaler 的 Insert 菜单里。ReShade 的快捷键由 ReShade 负责，Magpie 在自己的窗口操作。

- Steam 扫描会定位《艾尔登法环》及《黑夜君临》`Game/` 下的实际 EXE，并自动显示专属方案。法环采用 **ERSS `DXGI.dll`＋Opti `winmm.dll`＋RTXMFG `dinput8.dll`**：安装 Opti 不覆盖 ERSS DLL，全部前置满足时才写入 `AdaMfgWrapperOnly=true`、`AdaMfgUnlock=false`、FG 输入/输出 `auto`。渲染管线页的“应用 3–6× 共存配置”还会备份配置并把 ERSS 设为 3×起点；“关闭”退回 2×但保留玩家 DLL。需完全退出游戏并在重启后核对实际倍率。只狼仍须用户自己的 SekiroTSR。
- 玩家自行从 [ERSS 作者页面](https://www.patreon.com/huutaiii/posts/114748623)取得付费或免费版本及可选慢动作附加组件；管理器不下载或捆绑 ERSS 文件。[RTXMFG 作者 Release](https://github.com/dashdogy/RTX40MFG-Unlock/releases)提供其 DLL，玩家可自行下载后在管理器中导入。该组合只在一套 RTX 4080 SUPER／ERSS 5.1.0／Streamline 2.14.1／RTXMFG 1.3.3 Hotfix 2 上人工验证了 3×、6×实际呈现；6×可能显著增加操作延迟。仅在无反作弊的离线环境使用。
- ReShade 桥接使用 `BridgeComponents/` 中的 `dlss5-bridge.addon64`、`renodx-dlss5.addon64`、`nvngx_dlss.dll`、`nvngx_dlssnr.dll`。管理器“获取官方 ReShade”按钮会在用户本机从 reshade.me 下载 6.8.0 full add-on 安装器、校验固定 SHA-256、只提取 `ReShade64.dll`，不运行安装器，也不随公开包分发 ReShade 二进制。桥接部署拒绝覆盖已有不同版本的代理 DLL／插件，记录 SHA-256 并仅卸载自己部署且未被修改的文件。若与现有 OptiScaler 共存，请先在 `OptiScaler.ini` 的 `[Plugins]` 设 `LoadReshade=true`。
- SAOG Magpie 从其 GitHub Release 下载实验版 ZIP，若发布资产提供 SHA-256 digest 则核对，再解压到管理器用户数据目录并启动。它是独立窗口捕获工具，不在游戏目录安装 DLL。实验版可能耗费数百 MB 下载流量；视频增强和窗口插帧不等同原生 DLSS-G。
- 《黑夜君临》默认建议 ReShade 桥接实验路线。它没有已验证的通用 OptiScaler 输入／6×方案；只在确认无反作弊的离线环境测试，不提供反作弊绕过。

组件来源：[ReShade](https://reshade.me/)、[DLSS5 Bridge](https://github.com/NIGos/dlss5-bridge/releases)、[RenoDX](https://github.com/clshortfuse/renodx)、[SAOG Magpie](https://github.com/SAOG0721/Magpie/releases)。RenoDX DLSS5 插件的发布来源与许可需由用户自行确认；本仓库不捆绑该插件。

## Build

```powershell
dotnet restore OptiScalerManager/OptiScalerManager.sln
dotnet build OptiScalerManager/OptiScalerManager.sln -c Release
dotnet test OptiScalerManager/OptiScalerManager.sln -c Release
dotnet publish OptiScalerManager/OptiScalerManager.App/OptiScalerManager.App.csproj -c Release -r win-x64 --self-contained false
```

The manager's publish output is fixed at `OptiScalerManager/publish/` even when the build configuration changes. Close a running copy from that folder before publishing again because Windows locks loaded DLLs. `Package/` beside the executable remains a separate, user-supplied game installation package.

更新中心的 DLL 矩阵同时展示 v0.2.4 正式包快照与本机 `Package/` 的逐文件校验状态、文件版本。两者不同并不自动意味着本机文件过旧；需核对来源与兼容性。ERSS、RTXMFG、SekiroTSR 等额外前置在单独的“渲染管线前置”页列出，不计入正式包 DLL 数量。

The Windows interface uses [WPF UI](https://github.com/lepoco/wpfui) 4.3.0 on .NET 8/WPF for Fluent window chrome and icons. The six windows share a high-contrast light theme. The main window separates the game library from the selected-game workspace; installation, diagnostics, and operation history are separate tabs rather than stacked panels. The supplied [zhuzichu520/FluentUI](https://github.com/zhuzichu520/FluentUI) project is Qt/QML and cannot be inserted into this WPF application without replacing its front end. WPF UI's MIT license and third-party notices are included under `OptiScalerManager.App/Licenses/` and copied beside the published executable.

The application requests administrator privileges because it can replace files in protected game libraries. It does not download on startup. Release checking is explicit from the Update Center.

## Configuration

Select a game in the library, then use **Common settings** or **Expert settings · all INI** in the game details pane. Expert settings opens the complete `OptiScaler.ini` editor directly; its search filters by section, key, or value without removing other entries when saving. Configuration is available after the selected game has an `OptiScaler.ini` file.
Expert settings groups existing entries by feature, shows guidance for known keys, and offers editable value dropdowns. Unknown keys remain editable as raw values. The common MFG selector offers 2× through 6× (`InterpolationCount` values 1–5). The engine parser also accepts value 6, but that requests 7× and may be clamped by the runtime; it is therefore not a common preset.
Each expert value is now displayed in a dedicated text field with a separate preset menu. Chinese summaries and a detailed panel distinguish documented behavior from key-name-based inference; the branch's original English `OptiScaler.ini` comments remain available below the explanation.

The Update Center defaults to `Moraphia/OptiScaler-Own` as the independent **stable** channel. You may change the repository (`owner/repo`) in the Update Center; clearing the field switches to read-only official metadata. It reads GitHub's latest full Release, not prereleases. Publish these assets with exact names:

更新中心分别显示当前管理器文件版本、管理器已校验的本地游戏包版本、最新稳定版和每项状态（需要更新、已是最新、本地版本未知等）。新游戏包内的 `package-version.json` 提供版本与核心 SHA-256 标记；旧包若没有标记或校验不通过，界面会诚实显示“未知”。这不是逐个游戏安装目录的版本检测。

“依赖矩阵”分三页：v0.2.4 正式包的 **28 个 DLL** 逐文件路径、版本、归属/推定来源、签名状态和 SHA-256；源码的 **9 个 Git 子模块**及其构建所用提交；**8 项外部上游追踪**。另有 9 项渲染管线前置（含 ERSS、RTXMFG 和可选附加组件），与正式包 DLL 分开，不代表本机已经安装。重新发布游戏包时，用 `tools/Generate-DependencyInventory.ps1` 从待发布 ZIP 重新生成并校验清单，再构建管理器。

- `OptiScalerManager-win-x64.zip` and `OptiScalerManager-win-x64.zip.manifest.json`: manager update. ZIP must contain `OptiScalerManager.exe` and `OptiScalerManager.Core.dll` at its root. It is staged beside other downloads; the running manager is not overwritten.
- `OptiScaler-Package-win-x64.zip` and `OptiScaler-Package-win-x64.zip.manifest.json`: game installation package. ZIP must contain `OptiScaler.dll`, `OptiScaler.ini`, and the `OptiScaler/` runtime folder at its root. Installation still requires per-game preview and confirmation.

Each manifest must contain `{"releaseChannel":"own","packageType":"manager"|"game","packageSha256":"<64-character SHA-256 of matching ZIP>"}`. A manifest and ZIP are paired by exact asset name and SHA-256. This validates integrity against the configured repository, not publisher identity; review binary redistribution rights before publishing a game package.

## Package layout

For local installation testing, place a validated OptiScaler package under `Package/` beside the published manager executable. The package must include `OptiScaler.dll`, `OptiScaler.ini`, and the `OptiScaler/` runtime folder.

### ReShade / RenoDX DLSS5 local staging

`publish/BridgeComponents/` is a local component cache, not a ready-to-install game package. It contains RenoDX DLSS5 6.5.3 (`renodx-dlss5.addon64`, from RankFTW's RHI community mirror), DLSS 5 Bridge v1.4.12 (from NIGos's release), and the already packaged NVIDIA DLSS/NR DLLs (310.9/310.8). The RenoDX archive SHA-256 is `553B1619B9E5DDFBCB4EBC7F2F3BFFFF9256A48A25B988F4817F5C63F4CAA1DE`; the extracted add-on is `342341F669F1D64E0C70C8593A07A2FAB5075E073DFAE97C331C9A6776260A0A`; the bridge add-on is `4F2ACECC1026AE89AC0B92767BE66CEEA2662AD0EF88710B89C7DA7840D548D4`. The RenoDX mirror is not the upstream author's canonical release channel; review provenance before redistributing it.

ReShade with full add-on support is **not** included in the public package. The manager downloads the pinned official 6.8.0 full add-on installer on demand and extracts only `ReShade64.dll` to the local `BridgeComponents/` cache after two SHA-256 checks. The installer is never executed. This direct DLL route is intended for the manager's supported DX11/DX12 bridge chain; it does not replace the official installer for Vulkan/OpenGL, shader package selection, or unusual proxy conflicts. The manager detects existing ReShade by file metadata, installs only absent files, refuses different-version collisions, and uninstalls only files it added. For a game without native DLSS, the bridge's substitute path additionally needs `synth=1` and usable depth/motion inputs; merely copying these files cannot create DLSS FG or 6× frame generation. The update center's separate pipeline-prerequisite matrix records ReShade, Bridge, RenoDX, ERSS-FG, SekiroTSR and Magpie without conflating them with the 28 DLLs in the formal game package.

## Upstream maintenance

`upstreams.json` is the maintainer-owned external dependency list. `tools/Check-Upstreams.ps1` can be run manually to compare commit or release references with `upstreams.lock.json` and write `upstream-report.md`. A saved upstream reference can become stale and does not mean the matching component has been integrated or tested. The inherited scheduled build workflow is separate from dependency monitoring and is not the stable release channel. `dependency-inventory.json` describes every DLL in the v0.2.4 release package; `pipeline-dependencies.json` separately records user-supplied prerequisites.
