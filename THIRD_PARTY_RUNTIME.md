# DLSS Neural Rendering 运行库来源

`nvngx_dlssnr.dll` 不是本仓库从源码构建的，也不是本仓库声称拥有的代码。当前游戏包采用 [OptiScaler-Aurora 的 `aurora-runtime-dlssnr-310.8` 发布资产](https://github.com/abc354402600/OptiScaler-Aurora/releases/tag/aurora-runtime-dlssnr-310.8)：

- 文件版本：310.8.0.0
- 文件大小：165,840,496 字节
- SHA-256：`E67DEE209320CDAFE0E93E45675D7AA34323A53ACC57A72B2E40A181581C989A`
- Windows Authenticode 校验：`HashMismatch`

该校验结果表示文件内容与其嵌入签名不一致。它可能是社区修改的兼容版本，但不能当成 NVIDIA 官方签名原版，也不能仅凭文件名或版本号确认制作它的最初仓库。用户应在使用前自行核对来源及接受风险。相关 NVIDIA SDK 条款参见随包的 `NVIDIA_RTX_SDK_LICENSE.txt`。

`nvngx.dll_dlssnr.dll` 是本分支从 `OptiScaler/dlssnr/forwarder/` 构建的转发器，与上述模型 DLL 不是同一个文件。OptiScaler 核心和本分支的源码见仓库，原项目及 Aurora 的贡献署名见根目录 README。
