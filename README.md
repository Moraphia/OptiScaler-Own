# OptiScaler Own

这是基于 [OptiScaler 官方项目](https://github.com/optiscaler/OptiScaler)维护的独立分支。项目将游戏中的超分辨率或帧生成调用接入 OptiScaler，由它选择实际使用的输出技术；在官方代码基础上，独立维护 RTX 40 系列相关的 MFG 扩展、实验性 DLSS 5 Neural Rendering 桥接，以及 Windows 图形管理器。**本项目不是 NVIDIA 或 OptiScaler 官方发行版。**

原官方 README 保留在 [README_UPSTREAM.md](README_UPSTREAM.md)；通用兼容性说明仍以[官方 Wiki](https://github.com/optiscaler/OptiScaler/wiki)为参考。本分支的功能、构建和发布由本仓库独立维护。

## 下载与安装

从[最新稳定版](https://github.com/Moraphia/OptiScaler-Own/releases/latest)下载两个 ZIP：

1. `OptiScalerManager-win-x64.zip`：解压后运行 `OptiScalerManager.exe`。需要 Windows 10/11 x64 和 .NET 8 Desktop Runtime。管理器启动时会请求管理员权限，以便处理受保护的游戏目录。
2. `OptiScaler-Package-win-x64.zip`：将整个 ZIP 解压到管理器程序旁的 `Package/` 文件夹。最终结构应为 `Package/OptiScaler.dll`、`Package/OptiScaler.ini` 和 `Package/OptiScaler/`，不要多套一层同名目录。
3. 在管理器中扫描 Steam 或添加游戏主程序 `.exe`，选择游戏，检查安装预览，再点击“安装 / 修复”。安装前退出游戏，并备份游戏目录中原有的同名代理 DLL 和配置。

也可按[官方安装文档](https://github.com/optiscaler/OptiScaler/wiki)手动部署游戏包。代理 DLL 名称（例如 `dxgi.dll`、`winmm.dll`）取决于游戏，不能保证所有游戏都能正常加载。联网且有反作弊保护的游戏可能拦截注入文件；不要尝试绕过反作弊。首次试用建议使用单机游戏。

管理器的“更新中心”只读取本仓库最新**稳定版** Release，分别下载管理器和游戏包，并用配套 SHA-256 清单校验。更新管理器时需先退出程序，再手动替换已解压的管理器文件；保留原 `Package/`。更新游戏包前仍会显示预览，不会静默覆盖游戏文件。

## 本分支的重点

- **OptiScaler 核心**：替换游戏已有的 DLSS、FSR、XeSS 等超分输入，提供图像设置、帧生成输入与输出配置。具体支持范围取决于游戏 API、显卡和运行库。
- **MFG 扩展**：为部分 RTX 40 系列环境提供实验性的多帧生成解锁。是否达到 3×—6×，取决于游戏是否有可接入的帧生成管线、实际加载的 Streamline/DLSS-G 版本及显卡；不能只凭菜单选项保证生效。
- **DLSS 5 Neural Rendering**：实验性桥接模块，必须由用户提供与环境兼容的 `nvngx_dlssnr.dll`；该 DLL **不包含在本项目的游戏包内**。高画质设置可能显著增加显存、功耗和帧时间。
- **OptiScaler Manager**：游戏发现、安装预览、备份与修复、状态检查、中文常用/专家配置以及稳定版更新。游戏内 `Insert` 菜单仍由 OptiScaler 提供，管理器并不取代它。

这些扩展并非所有游戏或硬件都能使用。若遇到启动崩溃、Output/FG 加载失败或缺失 DLL，请先检查实际加载的代理文件、游戏自带运行库、游戏日志与 `OptiScaler.ini`，不要把游戏菜单里出现的选项视为功能已成功启用。

## 自行构建

需要 Visual Studio 2022 C++ 工具链、Windows SDK、.NET 8 SDK，以及本仓库所引用的外部依赖。建议先使用已固定版本的子模块/依赖；更新第三方 SDK 前核对兼容性。

```powershell
git clone --recurse-submodules https://github.com/Moraphia/OptiScaler-Own.git
cd OptiScaler-Own
MSBuild OptiScaler.sln /p:Configuration=Release /p:Platform=x64 /m
dotnet test OptiScalerManager/OptiScalerManager.sln -c Release
dotnet publish OptiScalerManager/OptiScalerManager.App/OptiScalerManager.App.csproj -c Release -r win-x64 --self-contained false
```

游戏端输出位于 `x64/Release/a/`；管理器固定输出到 `OptiScalerManager/publish/`。不要直接把整个构建目录当成 Release：其中可能含有调试符号、链接文件或本机残留文件。依赖版本记录见 [`upstreams.json`](upstreams.json) 和 [`upstreams.lock.json`](upstreams.lock.json)。

## 来源与许可

OptiScaler 核心来自 [optiscaler/OptiScaler](https://github.com/optiscaler/OptiScaler)，MFG 与 Neural Rendering 移植参考了 [OptiScaler-Aurora](https://github.com/abc354402600/OptiScaler-Aurora) 及相关项目。第三方组件的著作权和许可仍归各自权利人；游戏包中的许可证随文件提供。本仓库主许可证见 [LICENSE](LICENSE)。如需向官方反馈问题，请先用官方原版复现；本分支特有问题请在此仓库提交 Issue。
