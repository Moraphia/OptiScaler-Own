# OptiScaler Own

[![稳定版](https://img.shields.io/github/v/release/Moraphia/OptiScaler-Own?label=%E7%A8%B3%E5%AE%9A%E7%89%88)](https://github.com/Moraphia/OptiScaler-Own/releases/latest)
![平台](https://img.shields.io/badge/%E5%B9%B3%E5%8F%B0-Windows%20x64-2563A6)
![管理器](https://img.shields.io/badge/%E7%AE%A1%E7%90%86%E5%99%A8-.NET%208-512BD4)
![实验性功能](https://img.shields.io/badge/MFG%20%2F%20Neural%20Rendering-%E5%AE%9E%E9%AA%8C%E6%80%A7-B57925)

这是基于 [OptiScaler 官方项目](https://github.com/optiscaler/OptiScaler)维护的独立分支。项目将游戏中的超分辨率或帧生成调用接入 OptiScaler，由它选择实际使用的输出技术；在官方代码基础上，独立维护 RTX 40 系列相关的 MFG 扩展、实验性 DLSS 5 Neural Rendering 桥接，以及 Windows 图形管理器。**本项目不是 NVIDIA 或 OptiScaler 官方发行版。**

原官方 README 保留在 [README_UPSTREAM.md](README_UPSTREAM.md)；通用兼容性说明仍以[官方 Wiki](https://github.com/optiscaler/OptiScaler/wiki)为参考。本分支的功能、构建和发布由本仓库独立维护。

## 下载与安装

从[最新稳定版](https://github.com/Moraphia/OptiScaler-Own/releases/latest)下载两个 ZIP；普通用户**不需要编译源码**，但仍需按游戏完成安装，不是双击 ZIP 就自动生效：

1. `OptiScalerManager-win-x64.zip`：解压后运行 `OptiScalerManager.exe`。需要 Windows 10/11 x64 和 .NET 8 Desktop Runtime。管理器启动时会请求管理员权限，以便处理受保护的游戏目录。
2. `OptiScaler-Package-win-x64.zip`：将整个 ZIP 解压到管理器程序旁的 `Package/` 文件夹。最终结构应为 `Package/OptiScaler.dll`、`Package/OptiScaler.ini` 和 `Package/OptiScaler/`，不要多套一层同名目录。
3. 在管理器中扫描 Steam 或添加游戏主程序 `.exe`，选择游戏，检查安装预览，再点击“安装 / 修复”。安装前退出游戏，并备份游戏目录中原有的同名代理 DLL 和配置。

也可按[官方安装文档](https://github.com/optiscaler/OptiScaler/wiki)手动部署游戏包。代理 DLL 名称（例如 `dxgi.dll`、`winmm.dll`）取决于游戏，不能保证所有游戏都能正常加载。联网且有反作弊保护的游戏可能拦截注入文件；不要尝试绕过反作弊。首次试用建议使用单机游戏。

管理器的“更新中心”只读取本仓库最新**稳定版** Release，分别下载管理器和游戏包，并用配套 SHA-256 清单校验。更新管理器时需先退出程序，再手动替换已解压的管理器文件；保留原 `Package/`。更新游戏包前仍会显示预览，不会静默覆盖游戏文件。

> [!IMPORTANT]
> 发布包可供普通用户直接下载使用，但只保证文件齐全、可构建和校验清单一致，**不保证每款游戏都兼容**。管理器依赖 .NET 8 Desktop Runtime；游戏还需要可接入的图形 API/超分或帧生成管线。反作弊拦截、缺少游戏原生 FG、旧版 Streamline、显卡/驱动限制都可能使某些选项无法生效。

## 本分支的重点

- **OptiScaler 核心**：替换游戏已有的 DLSS、FSR、XeSS 等超分输入，提供图像设置、帧生成输入与输出配置。具体支持范围取决于游戏 API、显卡和运行库。
- **MFG 扩展**：为部分 RTX 40 系列环境提供实验性的多帧生成解锁。是否达到 3×—6×，取决于游戏是否有可接入的帧生成管线、实际加载的 Streamline/DLSS-G 版本及显卡；不能只凭菜单选项保证生效。
- **DLSS 5 Neural Rendering**：实验性桥接模块。游戏包包含 Aurora 社区发布的 `nvngx_dlssnr.dll` 310.8 和本分支编译的 `nvngx.dll_dlssnr.dll` 转发器；前者签名校验为 `HashMismatch`，不能视为 NVIDIA 官方签名原版。来源及哈希见下文。高画质设置可能显著增加显存、功耗和帧时间。
- **OptiScaler Manager**：游戏发现、安装预览、备份与修复、状态检查、中文常用/专家配置以及稳定版更新。游戏内 `Insert` 菜单仍由 OptiScaler 提供，管理器并不取代它。

这些扩展并非所有游戏或硬件都能使用。若遇到启动崩溃、Output/FG 加载失败或缺失 DLL，请先检查实际加载的代理文件、游戏自带运行库、游戏日志与 `OptiScaler.ini`，不要把游戏菜单里出现的选项视为功能已成功启用。

## 更新与维护范围

| 层级 | 当前机制 | 维护时要做什么 |
| --- | --- | --- |
| 本分支稳定版 | 管理器打开“更新中心”时读取本仓库 `releases/latest`；分别下载、校验管理器包和游戏包 | 发布新版本、提供两份 ZIP 及对应 SHA-256 清单；管理器更新需退出后手动替换 |
| 核心源码及 SDK | [`upstreams.json`](upstreams.json) 列有官方 OptiScaler、AMD FidelityFX、Intel XeSS、NVIDIA NVAPI、Streamline、DLSS、DLSS-Enabler、dlssg-to-fsr3；[`Check-Upstreams.ps1`](tools/Check-Upstreams.ps1) 可手动比对 GitHub commit/Release | 审阅上游变更，按需更新子模块、移植补丁、重新构建和游戏测试；**目前没有自动同步、自动 PR 或六小时定时检查** |
| 随包二进制 | 当前未逐个自动检查文件版本、签名和兼容性；`upstreams.lock.json` 也不是完整运行库锁文件 | 分别记录 DLSS、DLSS-G、DLSSD、Streamline 插件、Neural Rendering DLL 的来源、文件版本、哈希与许可证，再决定是否换版 |

截至 2026-09-24，游戏包的 `nvngx_dlss.dll` / `nvngx_dlssg.dll` / `nvngx_dlssd.dll` 文件版本均为 **310.9.0.0**；NVIDIA 已发布 [DLSS SDK 310.9.1](https://github.com/NVIDIA/DLSS/releases/tag/v310.9.1)。包内 `sl.*.dll` 则混有 **Streamline 2.12、2.13、2.14**；官方最新 Release 为 [2.14.1](https://github.com/NVIDIA-RTX/Streamline/releases/tag/v2.14.1)。这些是待评估的更新，**尚未自动集成到本版**。尤其 Streamline 应作为兼容性组合测试，不能仅凭版本号覆盖游戏原有 DLL；管理器的运行库同步功能也应先查看预览与备份。Neural Rendering 的 310.8 社区 DLL 独立于 NVIDIA 公开 DLSS SDK，不能拿 `nvngx_dlssd.dll`（Ray Reconstruction）代替。

## 自行构建

需要 Visual Studio 2022 C++ 工具链、Windows SDK、.NET 8 SDK，以及本仓库所引用的外部依赖。建议先使用已固定版本的子模块/依赖；更新第三方 SDK 前核对兼容性。

```powershell
git clone --recurse-submodules https://github.com/Moraphia/OptiScaler-Own.git
cd OptiScaler-Own
MSBuild OptiScaler.sln /p:Configuration=Release /p:Platform=x64 /m
dotnet test OptiScalerManager/OptiScalerManager.sln -c Release
dotnet publish OptiScalerManager/OptiScalerManager.App/OptiScalerManager.App.csproj -c Release -r win-x64 --self-contained false
```

游戏端输出位于 `x64/Release/a/`；管理器固定输出到 `OptiScalerManager/publish/`。不要直接把整个构建目录当成 Release：其中可能含有调试符号、链接文件或本机残留文件。`upstreams.lock.json` 目前只锁定部分源码依赖，不能视为完整发布物清单。

## 来源与许可

OptiScaler 核心来自 [optiscaler/OptiScaler](https://github.com/optiscaler/OptiScaler)，MFG 与 Neural Rendering 移植参考了 [OptiScaler-Aurora](https://github.com/abc354402600/OptiScaler-Aurora) 及相关项目。第三方组件的著作权和许可仍归各自权利人；游戏包中的许可证随文件提供。本仓库主许可证见 [LICENSE](LICENSE)。如需向官方反馈问题，请先用官方原版复现；本分支特有问题请在此仓库提交 Issue。

游戏包中的 `nvngx_dlssnr.dll` 与 [Aurora 运行库发布资产](https://github.com/abc354402600/OptiScaler-Aurora/releases/tag/aurora-runtime-dlssnr-310.8)逐字节哈希一致：SHA-256 `E67DEE209320CDAFE0E93E45675D7AA34323A53ACC57A72B2E40A181581C989A`。它是社区提供的兼容版本；签名校验不通过不代表它一定有恶意，但也不能据此保证来源与安全性。使用前请自行评估风险。
