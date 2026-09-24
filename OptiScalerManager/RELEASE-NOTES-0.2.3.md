# OptiScaler Own v0.2.3 稳定版

本版更新 NVIDIA 运行库，并把本仓库格式检查改为约束新修改的 C++ 行；历史格式问题尚未批量整理。游戏内核心和管理器版本同步为 `0.2.3`；功能与设置沿用 v0.2.2。

- `nvngx_dlss.dll`、`nvngx_dlssg.dll`、`nvngx_dlssd.dll`：来自 [NVIDIA 官方 DLSS SDK 310.9.1](https://github.com/NVIDIA/DLSS/releases/tag/v310.9.1) / [Streamline SDK 2.14.1](https://github.com/NVIDIA-RTX/Streamline/releases/tag/v2.14.1) 的签名有效生产版 DLL。
- `sl.*.dll`：将官方 2.14.1 SDK 提供的 11 个生产版插件同步更新。SDK 未提供 `sl.dlss_nr.dll`，所以此文件仍为 2.13.0.0；不要误认为全部插件版本一致。
- `nvngx_dlssnr.dll`：仍是 v0.2.2 使用的 Aurora 社区 310.8.0.0 运行库，SHA-256 `E67DEE209320CDAFE0E93E45675D7AA34323A53ACC57A72B2E40A181581C989A`，Authenticode 状态 `HashMismatch`。本版**没有**更换或认证它。

在一台 RTX 4080 Super 环境中，**GTA V Enhanced 故事模式**加载了 `sl.interposer` / `sl.common` / `sl.dlss_g` 2.14.1、DLSS / DLSS-G 310.9.1 和现有 NR DLL；用户确认 6× 多帧生成生效且未发现明显问题。这是单游戏人工测试，不等于其他游戏、显卡或反作弊环境均已验证。联网/反作弊模式不在测试范围，不要绕过反作弊。

下载管理器和游戏包两个 ZIP，按 [中文 README](https://github.com/Moraphia/OptiScaler-Own#readme) 安装。更新已部署的游戏前备份原有 DLL 和 `OptiScaler.ini`，注意管理器的运行库同步可能触及游戏根目录中同名 DLL。每个 ZIP 均有配套 SHA-256 清单；来源与许可见 [第三方运行库说明](https://github.com/Moraphia/OptiScaler-Own/blob/main/THIRD_PARTY_RUNTIME.md)。
