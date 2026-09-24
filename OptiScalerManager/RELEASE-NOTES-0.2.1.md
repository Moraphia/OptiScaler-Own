# OptiScaler Own v0.2.1 稳定版

本次同时发布 Windows x64 管理器与游戏安装包。项目基于官方 OptiScaler，独立维护 RTX 40 系列 MFG 扩展、实验性 DLSS 5 Neural Rendering 桥接和中文图形管理器；不是 NVIDIA 或 OptiScaler 官方发行版。

## 下载与使用

1. 下载并解压 `OptiScalerManager-win-x64.zip`，运行 `OptiScalerManager.exe`。需安装 .NET 8 Desktop Runtime。
2. 下载 `OptiScaler-Package-win-x64.zip`，解压到管理器旁的 `Package/` 文件夹。确认 `Package/OptiScaler.dll` 和 `Package/OptiScaler.ini` 直接位于该目录，不要多嵌套一层。
3. 在管理器中添加游戏主程序，选择“安装 / 修复”，仔细检查预览后确认。安装前退出游戏，并备份原有同名文件。也可以在“更新中心”分别下载、校验两个 ZIP；更新管理器需退出程序后手动替换文件。

## 内容与限制

- 游戏包包含本分支编译的 `OptiScaler.dll`、默认配置及配套运行库。两个 ZIP 分别提供 SHA-256 清单。
- `nvngx_dlssnr.dll` **未包含**；如需使用 Neural Rendering，用户仍须自行提供与其环境兼容的文件。
- `v0.2.1` 是发行包与管理器版本；游戏 DLL 内显示的 `10.0.0-dev-own` 是 OptiScaler 内核版本标识，两者不必相同。
- 多帧生成倍率及输出是否可用取决于游戏、显卡和实际加载的 Streamline/DLSS-G 运行库；有选项不等于保证生效。实验性功能可能造成画面异常、性能下降或崩溃。
- 不要在会拦截注入文件的反作弊游戏中尝试绕过保护。第三方组件的许可证随游戏包提供。

此版本通过本地 C++ Release 构建与 19 项管理器测试；尚未对所有游戏逐一实测。C++ 链接阶段仍有 C4744、LNK4098 警告，后续需要独立排查。

源代码、构建说明与使用限制见仓库 [README](https://github.com/Moraphia/OptiScaler-Own#readme)。
