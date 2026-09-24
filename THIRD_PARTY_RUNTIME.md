# DLSS Neural Rendering 运行库来源

`nvngx_dlssnr.dll` 不是本仓库从源码构建的，也不是本仓库声称拥有的代码。当前游戏包采用 [OptiScaler-Aurora 的 `aurora-runtime-dlssnr-310.8` 发布资产](https://github.com/abc354402600/OptiScaler-Aurora/releases/tag/aurora-runtime-dlssnr-310.8)：

- 文件版本：310.8.0.0
- 文件大小：165,840,496 字节
- SHA-256：`E67DEE209320CDAFE0E93E45675D7AA34323A53ACC57A72B2E40A181581C989A`
- Windows Authenticode 校验：`HashMismatch`

该校验结果表示文件内容与其嵌入签名不一致。它可能是社区修改的兼容版本，但不能当成 NVIDIA 官方签名原版，也不能仅凭文件名或版本号确认制作它的最初仓库。用户应在使用前自行核对来源及接受风险。相关 NVIDIA SDK 条款参见随包的 `NVIDIA_RTX_SDK_LICENSE.txt`。

`nvngx.dll_dlssnr.dll` 是本分支从 `OptiScaler/dlssnr/forwarder/` 构建的转发器，与上述模型 DLL 不是同一个文件。OptiScaler 核心和本分支的源码见仓库，原项目及 Aurora 的贡献署名见根目录 README。

## 社区候选审计（2026-09-24，未纳入发布包）

[RHI 社区仓库的 `dlssnr-310.8.SF-v2`](https://github.com/RankFTW/rhi-repo/releases/tag/dlssnr-310.8.SF-v2) 资产 ZIP 的 SHA-256 为 `1DA35941894994EB087E017577829E492454E9BAE3A6A9397027069CEB74955C`。解包后 `nvngx_dlssnr.dll` 的文件版本是 **310.8.SF.0**，大小 165,830,144 字节，SHA-256 为 `6EB209E764F39872625DEBD6ABAF45E2BB6322F6F270F781F70C059AE30B3927`，Windows 签名状态为 `NotSigned`。它是与当前 310.8.0.0 不同的社区补丁候选，**不是 NVIDIA 310.9 NR 正式版**；尚未在本分支或 RTX 4080 Super 上完成兼容性和性能测试，不能仅凭名称自动替换。原有 Aurora 文件继续保留。

社区项目 [DLSSNR Signature Repair](https://github.com/kayle2203/dlssnr-signature-repair) 记录另一份签名有效的 310.8.0.0 哈希，但签名有效不代表适配 RTX 40 系显卡；也不等于本仓库已取得、测试或获准再分发该文件。
