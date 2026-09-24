namespace OptiScalerManager.Core;

public sealed record FeatureReadiness(string Feature, bool Configured, bool DependenciesPresent, string Detail);

public static class CapabilityService
{
    public static IReadOnlyList<FeatureReadiness> Inspect(string gameDirectory, IniDocument ini)
    {
        var runtime = Path.Combine(gameDirectory, "OptiScaler");
        var mfg = ini.Get("DLSSG", "InterpolationCount");
        var mfgConfigured = int.TryParse(mfg, out var count) && count > 1;
        var adaUnlock = ini.Get("DLSSG", "AdaMfgUnlock")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        var fgDll = File.Exists(Path.Combine(runtime, "nvngx_dlssg.dll")) || File.Exists(Path.Combine(gameDirectory, "nvngx_dlssg.dll"));
        var nrConfigured = ini.Get("DlssNr", "Enabled")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        var nrDll = File.Exists(Path.Combine(runtime, "nvngx_dlssnr.dll")) || File.Exists(Path.Combine(gameDirectory, "nvngx_dlssnr.dll"));
        return
        [
            new("多帧生成", mfgConfigured, fgDll, mfgConfigured ? fgDll ? $"已配置 {count + 1}×{(adaUnlock ? "（RTX 40 实验解锁）" : "")}；仅确认 DLL 存在，尚未验证游戏管线或运行时启用。高倍率请比较基础帧率、帧时间与功耗。" : "已配置多帧生成，但未找到 nvngx_dlssg.dll。" : "未配置多帧生成。"),
            new("DLSS 神经渲染", nrConfigured, nrDll, nrConfigured ? nrDll ? "已配置；仅确认 DLL 存在，尚未验证显卡支持或运行时启用。" : "已配置，但未找到 nvngx_dlssnr.dll。" : "未启用神经渲染。")
        ];
    }
}
