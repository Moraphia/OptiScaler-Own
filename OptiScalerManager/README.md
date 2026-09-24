# OptiScaler Manager

当前稳定版管理器：`0.2.2`。管理器 ZIP 不包含游戏端运行库；游戏安装包作为独立 ZIP 在同一稳定版 Release 发布。

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

- `OptiScalerManager-win-x64.zip` and `OptiScalerManager-win-x64.zip.manifest.json`: manager update. ZIP must contain `OptiScalerManager.exe` and `OptiScalerManager.Core.dll` at its root. It is staged beside other downloads; the running manager is not overwritten.
- `OptiScaler-Package-win-x64.zip` and `OptiScaler-Package-win-x64.zip.manifest.json`: game installation package. ZIP must contain `OptiScaler.dll`, `OptiScaler.ini`, and the `OptiScaler/` runtime folder at its root. Installation still requires per-game preview and confirmation.

Each manifest must contain `{"releaseChannel":"own","packageType":"manager"|"game","packageSha256":"<64-character SHA-256 of matching ZIP>"}`. A manifest and ZIP are paired by exact asset name and SHA-256. This validates integrity against the configured repository, not publisher identity; review binary redistribution rights before publishing a game package.

## Package layout

For local installation testing, place a validated OptiScaler package under `Package/` beside the published manager executable. The package must include `OptiScaler.dll`, `OptiScaler.ini`, and the `OptiScaler/` runtime folder.

## Upstream maintenance

`upstreams.json` is the maintainer-owned dependency list. `tools/Check-Upstreams.ps1` compares commit or release references with `upstreams.lock.json` and writes `upstream-report.md`. GitHub Actions runs this check every six hours, opens a tracking PR, and publishes a reviewable nightly build.
