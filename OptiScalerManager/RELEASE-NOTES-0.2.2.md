# OptiScaler Own v0.2.2 稳定版

本版重新标识游戏内 `Insert` 菜单：标题、文件版本信息和更新提示使用 **OptiScaler Own · Moraphia 独立构建**，更新检查与发布入口指向本仓库。代码仍基于官方 OptiScaler，并保留相关作者与第三方署名。

游戏包现包含实验性 DLSS Neural Rendering 所需的两个文件：

- `nvngx.dll_dlssnr.dll`：本分支从源码编译的转发器，位于游戏包根目录。
- `nvngx_dlssnr.dll`：Aurora 社区发布的 310.8.0.0 运行库，位于 `OptiScaler/` 目录。SHA-256：`E67DEE209320CDAFE0E93E45675D7AA34323A53ACC57A72B2E40A181581C989A`，与 [Aurora 发布资产](https://github.com/abc354402600/OptiScaler-Aurora/releases/tag/aurora-runtime-dlssnr-310.8)一致。该文件的 Authenticode 校验结果为 `HashMismatch`，不是可验证的 NVIDIA 官方签名原版；使用者应自行判断是否接受这一社区运行库。

详情及权利说明见 [第三方运行库来源记录](https://github.com/Moraphia/OptiScaler-Own/blob/main/THIRD_PARTY_RUNTIME.md)。

下载 `OptiScalerManager-win-x64.zip` 和 `OptiScaler-Package-win-x64.zip`。解压管理器后，将游戏包内容放入管理器旁的 `Package/`，确保其中直接有 `OptiScaler.dll`、`OptiScaler.ini` 和 `OptiScaler/`；然后在管理器选择游戏，检查预览并确认安装。需 Windows x64 和 .NET 8 Desktop Runtime。两个 ZIP 均附带 SHA-256 校验清单。

多帧生成与 Neural Rendering 仍是实验性功能，效果取决于游戏、显卡、驱动以及实际加载的运行库。不要绕过反作弊。完整说明见 [中文 README](https://github.com/Moraphia/OptiScaler-Own#readme)。
