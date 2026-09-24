# OptiScaler Manager

Independent Windows 10/11 x64 desktop manager for OptiScaler. The manager does not replace the in-game ImGui overlay; it handles game discovery, installation, verification, repair, configuration snapshots and release updates.

## Build

```powershell
dotnet restore OptiScalerManager/OptiScalerManager.sln
dotnet build OptiScalerManager/OptiScalerManager.sln -c Release
dotnet test OptiScalerManager/OptiScalerManager.sln -c Release
```

The application requests administrator privileges because it can replace files in protected game libraries. It does not download on startup. Release checking is explicit from the Update Center.

In this independent branch the Update Center displays official release metadata only. Automatic downloading is disabled until a release feed for this branch is configured, to prevent replacing the experimental feature build with an incompatible official package.

## Package layout

For local installation testing, place a validated OptiScaler package under `Package/` beside the published manager executable. The package must include `OptiScaler.dll`, `OptiScaler.ini`, and the `OptiScaler/` runtime folder.

## Upstream maintenance

`upstreams.json` is the maintainer-owned dependency list. `tools/Check-Upstreams.ps1` compares commit or release references with `upstreams.lock.json` and writes `upstream-report.md`. GitHub Actions runs this check every six hours, opens a tracking PR, and publishes a reviewable nightly build.
