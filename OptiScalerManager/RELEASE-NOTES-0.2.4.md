# OptiScaler Own v0.2.4 稳定版

这次发布把《艾尔登法环》的 ERSS／Opti／RTXMFG 共存实验整理成可检查、可启用、可退回的管理器流程，并更新依赖矩阵与中文文档。它不是把付费 ERSS 装进 Opti，也不保证每台机器都能稳定使用 6×。

## 下载什么

- `OptiScalerManager-win-x64.zip`：Windows 10/11 x64 管理器，需 .NET 8 Desktop Runtime。解压运行 `OptiScalerManager.exe`。
- `OptiScaler-Package-win-x64.zip`：游戏端 OptiScaler 包。解压到管理器旁的 `Package/`，再在管理器里选游戏、预览、安装。**单独下载管理器 ZIP 不包含游戏 DLL。**
- 两份 ZIP 均附 `.manifest.json`，管理器使用 SHA-256 校验下载。法环 ERSS、RTXMFG 和慢动作附加组件不在这两个 ZIP 中。

## 新功能与修正

1. **法环专属共存路线**：识别 `eldenring.exe` 与 ERSS 5.1+ 后使用 Opti `winmm.dll`，保留 ERSS 的 `DXGI.dll`。前置齐全时只让 Opti 扩展 ERSS 的 Streamline 上限：`AdaMfgWrapperOnly=true`、`AdaMfgUnlock=false`、`FGInput/FGOutput=auto`。标准安装不覆盖 ERSS DLL、Streamline 或当前 ERSS 倍率。
2. **明确的高倍率开关**：渲染管线页列出 ERSS、Streamline、Opti、RTXMFG、可选慢动作附加组件的状态与版本。玩家可导入自行下载的 RTXMFG DLL（仅在 `dinput8.dll` 不存在时）；“应用”备份配置后设定 3×起点，“关闭”备份后退回 2×。必须退出游戏后操作，并在重启后确认实际呈现倍率。“关闭”不删除玩家自己的 RTXMFG DLL。常用配置新增 `AdaMfgWrapperOnly`，阻止它与 `AdaMfgUnlock` 同时开启。
3. **管理器与依赖矩阵**：游戏包 28 个 DLL 的版本、来源、签名状态、SHA-256 按 v0.2.4 重新生成；ERSS、RTXMFG、ERSS Streamline 及附加组件等 9 项管线前置另表记录，避免把“列在矩阵”误认为“已安装或已验证”。保留多管线入口：OptiScaler、ReShade＋RenoDX／Bridge、SAOG Magpie。
4. **安全性**：不自动取得 ERSS 付费 DLL，不覆盖未知 `dinput8.dll`，不在游戏运行时改写两份配置；配置修改前备份到用户本地数据目录。没有反作弊绕过功能。游戏包只更换经过本机测试的 Opti 核心和默认 INI，其余运行库沿用 v0.2.3：NVIDIA DLSS 310.9.1、Streamline 2.14.1；社区 Neural Rendering 310.8 仍为原来源，不宣称 NVIDIA 官方签名。

## 法环玩家如何使用

1. 仅在无反作弊的离线环境，从 [ERSS 作者页面](https://www.patreon.com/huutaiii/posts/114748623)自行获取并安装合法版本。付费版 ERSS 5.1.0 是这次本机验证版本；免费版及其他版本不应视为同等验证。按作者说明安装可选的 `RemoveFrameTimeConstraint` 附加组件；它处理极低基础帧率下的物理慢动作，不是降延迟插件。
2. 确认 ERSS 的 `sl.dlss_g.dll` 是 2.14.1 或更新版。本管理器只检查版本，不自动替换 ERSS 文件。[NVIDIA Streamline 2.14.1](https://github.com/NVIDIA-RTX/Streamline/releases/tag/v2.14.1)是公开来源。
3. 从 [RTXMFG 作者 Release](https://github.com/dashdogy/RTX40MFG-Unlock/releases)自行下载 1.3.3 Hotfix 2 或更新版，在“渲染管线”导入其 DLL；已有 `dinput8.dll` 时管理器拒绝覆盖。
4. 将本版游戏包放到管理器 `Package/`，选择法环实际 `eldenring.exe`，预览并安装 Opti。完全退出游戏后在“渲染管线”确认前置并点击“应用 3–6× 共存配置”。先试 3×；ERSS 菜单可再选更高档。Opti Insert 菜单、ERSS 菜单及 RTXMFG 菜单各有自己的职责，不要同时打开 Opti 自身的 `AdaMfgUnlock`。
5. 若卡死、花屏、顿挫或延迟明显，退回 ERSS 2×，或用管理器“关闭高倍率解锁”。备份保留在 `%LOCALAPPDATA%\OptiScalerManager\elden-ring-profiles\`。6×可能增加延迟；建议先以 3×作为日常档位。

本机 RTX 4080 SUPER＋ERSS 5.1.0＋Streamline 2.14.1＋RTXMFG 1.3.3 Hotfix 2 的离线法环测试观察到实际 3×、6×呈现，并可打开 Opti Insert 菜单；不等于跨游戏、跨驱动保证，也尚未量化输入到显示延迟。管理器新增按钮的自动化检查与单元测试不能代替玩家机器上的首次启动验证。

## 维护者说明

`ERSS.dll`、ERSS `DXGI.dll`、`RemoveFrameTimeConstraint.dll`、任何 `ERSS-*.7z`、玩家购买的压缩包和账号凭据都**没有**进入仓库或 Release。发布前检查两份 ZIP 内容及四个资产的 SHA-256。RTXMFG 本身也不随包分发，只提供作者链接与本地导入功能。
