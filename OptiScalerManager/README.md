# OptiScaler Manager

当前稳定版管理器：`0.2.3`。管理器 ZIP 不包含游戏端运行库；游戏安装包作为独立 ZIP 在同一稳定版 Release 发布。

当前源码的管理器标识为 `0.2.4-local`，用于区分已发布的 v0.2.3；依赖矩阵改造尚未发布为新的稳定版。游戏包仍为 v0.2.3，DLL 未更换。

Independent Windows 10/11 x64 desktop manager for OptiScaler. The manager does not replace the in-game ImGui overlay; it handles game discovery, installation, verification, repair, configuration snapshots and release updates.

## Build

```powershell
dotnet restore OptiScalerManager/OptiScalerManager.sln
dotnet build OptiScalerManager/OptiScalerManager.sln -c Release
dotnet test OptiScalerManager/OptiScalerManager.sln -c Release
dotnet publish OptiScalerManager/OptiScalerManager.App/OptiScalerManager.App.csproj -c Release -r win-x64 --self-contained false
```

The manager's publish output is fixed at `OptiScalerManager/publish/` even when the build configuration changes. Close a running copy from that folder before publishing again because Windows locks loaded DLLs. `Package/` beside the executable remains a separate, user-supplied game installation package.

The application requests administrator privileges because it can replace files in protected game libraries. It does not download on startup. Release checking is explicit from the Update Center.

## Configuration

Select a game in the library, then use **Common settings** or **Expert settings · all INI** in the game details pane. Expert settings opens the complete `OptiScaler.ini` editor directly; its search filters by section, key, or value without removing other entries when saving. Configuration is available after the selected game has an `OptiScaler.ini` file.
Expert settings groups existing entries by feature, shows guidance for known keys, and offers editable value dropdowns. Unknown keys remain editable as raw values. The common MFG selector offers 2× through 6× (`InterpolationCount` values 1–5). The engine parser also accepts value 6, but that requests 7× and may be clamped by the runtime; it is therefore not a common preset.
Each expert value is now displayed in a dedicated text field with a separate preset menu. Chinese summaries and a detailed panel distinguish documented behavior from key-name-based inference; the branch's original English `OptiScaler.ini` comments remain available below the explanation.

The Update Center defaults to `Moraphia/OptiScaler-Own` as the independent **stable** channel. You may change the repository (`owner/repo`) in the Update Center; clearing the field switches to read-only official metadata. It reads GitHub's latest full Release, not prereleases. Publish these assets with exact names:

更新中心分别显示当前管理器文件版本、管理器已校验的本地游戏包版本、最新稳定版和每项状态（需要更新、已是最新、本地版本未知等）。新游戏包内的 `package-version.json` 提供版本与核心 SHA-256 标记；旧包若没有标记或校验不通过，界面会诚实显示“未知”。这不是逐个游戏安装目录的版本检测。

“依赖矩阵”分三页：v0.2.3 正式包的 **28 个 DLL** 逐文件路径、版本、归属/推定来源、签名状态和 SHA-256；源码的 **9 个 Git 子模块**及其构建所用提交；**8 项外部上游追踪**。这是发布包快照，不是当前游戏文件的实时扫描，也不自动判断各上游是否有新版。重新发布游戏包时，先用 `tools/Generate-DependencyInventory.ps1` 从已校验 ZIP 重新生成清单，再构建管理器。

- `OptiScalerManager-win-x64.zip` and `OptiScalerManager-win-x64.zip.manifest.json`: manager update. ZIP must contain `OptiScalerManager.exe` and `OptiScalerManager.Core.dll` at its root. It is staged beside other downloads; the running manager is not overwritten.
- `OptiScaler-Package-win-x64.zip` and `OptiScaler-Package-win-x64.zip.manifest.json`: game installation package. ZIP must contain `OptiScaler.dll`, `OptiScaler.ini`, and the `OptiScaler/` runtime folder at its root. Installation still requires per-game preview and confirmation.

Each manifest must contain `{"releaseChannel":"own","packageType":"manager"|"game","packageSha256":"<64-character SHA-256 of matching ZIP>"}`. A manifest and ZIP are paired by exact asset name and SHA-256. This validates integrity against the configured repository, not publisher identity; review binary redistribution rights before publishing a game package.

## Package layout

For local installation testing, place a validated OptiScaler package under `Package/` beside the published manager executable. The package must include `OptiScaler.dll`, `OptiScaler.ini`, and the `OptiScaler/` runtime folder.

## Upstream maintenance

`upstreams.json` is the maintainer-owned external dependency list. `tools/Check-Upstreams.ps1` can be run manually to compare commit or release references with `upstreams.lock.json` and write `upstream-report.md`. The eight references were checked on 2026-09-24, but there is currently no scheduled upstream-check job or automatic tracking PR. A saved upstream reference can become stale and does not mean the matching component has been integrated or tested. The inherited scheduled build workflow is separate from dependency monitoring and is not the stable release channel. `dependency-inventory.json` instead describes every DLL in the v0.2.3 release package.
