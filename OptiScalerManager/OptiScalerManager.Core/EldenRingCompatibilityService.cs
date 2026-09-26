using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OptiScalerManager.Core;

public sealed record EldenRingCompatibilityStatus(
    bool ErssPresent, string ErssVersion, bool StreamlineReady, string StreamlineVersion,
    bool OptiPresent, bool RtxMfgPresent, string RtxMfgVersion, bool UnlockConfigured,
    bool AddonPresent, string Detail)
{
    public bool CanApply => ErssPresent && StreamlineReady && OptiPresent && RtxMfgPresent;
}

/// <summary>
/// Opt-in profile for the tested ERSS DXGI + Opti winmm + RTXMFG dinput8 chain.
/// ERSS and its paid binaries are never downloaded, copied or redistributed here.
/// </summary>
public sealed class EldenRingCompatibilityService
{
    private readonly string _backupRoot;
    public const string ErssSourceUrl = "https://www.patreon.com/huutaiii/posts/114748623";
    public const string RtxMfgSourceUrl = "https://github.com/dashdogy/RTX40MFG-Unlock/releases";
    public const string TestedOptiCoreSha256 = "47E62EA02D8B35246CA4325290009886CA224746CBAFF45E2E486AD536F622B0";

    public EldenRingCompatibilityService(string? backupRoot = null) => _backupRoot = backupRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiScalerManager", "elden-ring-profiles");

    public EldenRingCompatibilityStatus Inspect(GameEntry game)
    {
        var root = game.InstallPath;
        var erSsVersion = VersionOf(Path.Combine(root, "ERSS.dll"));
        var erSs = IsModule(Path.Combine(root, "ERSS.dll"), "ERSS") && IsAtLeast(erSsVersion, 5, 1, 0) &&
                   IsModule(Path.Combine(root, "DXGI.dll"), "DXHooks") &&
                   File.Exists(Path.Combine(root, "ERSS", "ERSS.toml"));
        var streamline = Path.Combine(root, "ERSS", "bin", "sl.dlss_g.dll");
        var slVersion = VersionOf(streamline);
        var slReady = File.Exists(streamline) && IsAtLeast(slVersion, 2, 14, 1);
        var optiDll = Path.Combine(root, "winmm.dll");
        var opti = IsModule(optiDll, "OptiScaler") && IsTestedCore(optiDll) &&
                   File.Exists(Path.Combine(root, "OptiScaler.ini"));
        var mfg = Path.Combine(root, "dinput8.dll");
        var mfgReady = IsModule(mfg, "RTXMFG") && IsAtLeast(VersionOf(mfg), 1, 3, 3, 2);
        var configured = false;
        if (opti)
        {
            try
            {
                var ini = IniDocument.Load(Path.Combine(root, "OptiScaler.ini"));
                configured = ini.Get("DLSSG", "AdaMfgWrapperOnly")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true &&
                             ini.Get("DLSSG", "AdaMfgUnlock")?.Equals("false", StringComparison.OrdinalIgnoreCase) == true;
            }
            catch (Exception) { /* An unreadable INI is not a verified profile. */ }
        }
        var addon = File.Exists(Path.Combine(root, "ERSS", "addons", "RemoveFrameTimeConstraint.dll"));
        var detail = $"ERSS {(erSs ? erSsVersion : "未匹配（需完整 5.1+ 安装）")} · " +
                     $"ERSS Streamline {(File.Exists(streamline) ? slVersion : "未检测到")}{(slReady ? "" : "（需 2.14.1 或更新）")} · " +
                     $"Opti winmm {(opti ? "本版已校验" : "未匹配本版 DLL")} · " +
                     $"RTXMFG dinput8 {(mfgReady ? VersionOf(mfg) : "未匹配（需 1.3.3 Hotfix 2+）")} · " +
                     $"3–6× 配置 {(configured ? "已写入，需重启验证" : "未启用")} · " +
                     $"物理慢动作附加组件 {(addon ? "已检测到" : "可选，未检测到")}。";
        return new(erSs, VersionOf(Path.Combine(root, "ERSS.dll")), slReady, slVersion, opti,
            mfgReady, VersionOf(mfg), configured, addon, detail);
    }

    public async Task<OperationResult> SetUnlockAsync(GameEntry game, bool enable, CancellationToken ct = default)
    {
        if (!Path.GetFileName(game.ExecutablePath ?? "").Equals("eldenring.exe", StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail("此配置仅适用于艾尔登法环的 eldenring.exe。");
        if (Process.GetProcessesByName("eldenring").Length != 0)
            return OperationResult.Fail("请完全退出法环后再修改配置。");
        var state = Inspect(game);
        if (enable && !state.CanApply)
            return OperationResult.Fail("前置组件不完整；请先自行安装 ERSS，并确认 ERSS Streamline ≥2.14.1、Opti winmm 和 RTXMFG dinput8。不会代替用户取得付费文件。", state.Detail);
        if (!state.OptiPresent || !state.ErssPresent)
            return OperationResult.Fail("未检测到完整的 Opti＋ERSS 配置，不能安全改写。", state.Detail);

        var iniPath = Path.Combine(game.InstallPath, "OptiScaler.ini");
        var tomlPath = Path.Combine(game.InstallPath, "ERSS", "ERSS.toml");
        var backup = Path.Combine(_backupRoot, FileUtilities.SafeGameId(Path.GetFullPath(game.InstallPath)),
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(backup);
            File.Copy(iniPath, Path.Combine(backup, "OptiScaler.ini"), false);
            File.Copy(tomlPath, Path.Combine(backup, "ERSS.toml"), false);
            var ini = IniDocument.Load(iniPath);
            ini.Set("DLSSG", "AdaMfgUnlock", "false");
            ini.Set("DLSSG", "AdaMfgWrapperOnly", enable ? "true" : "false");
            ini.Set("FrameGen", "FGInput", "auto");
            ini.Set("FrameGen", "FGOutput", "auto");

            var toml = await File.ReadAllTextAsync(tomlPath, ct);
            if (!Regex.IsMatch(toml, @"(?m)^[ \t]*DLSSGNumGenFrames[ \t]*=[ \t]*\d+[ \t]*$"))
                return OperationResult.Fail("ERSS.toml 缺少 DLSSGNumGenFrames；没有修改任何游戏配置。", backup);
            var updated = Regex.Replace(toml, @"(?m)^([ \t]*DLSSGNumGenFrames[ \t]*=[ \t]*)\d+([ \t]*)$",
                m => m.Groups[1].Value + (enable ? "2" : "1") + m.Groups[2].Value);
            ini.SaveAtomic(iniPath);
            var temp = tomlPath + ".manager.tmp";
            await File.WriteAllTextAsync(temp, updated, ct);
            File.Move(temp, tomlPath, true);
            return OperationResult.Ok(enable
                ? "已写入法环 3× 安全起点；ERSS 菜单可再选择 4–6×。完全重启游戏后核对实际倍率，不能仅凭配置宣称生效。备份：" + backup
                : "已关闭法环 3–6× 共存解锁并把 ERSS 请求恢复为 2×。RTXMFG 文件保留，完全重启游戏后生效。备份：" + backup);
        }
        catch (Exception ex)
        {
            try
            {
                var iniBackup = Path.Combine(backup, "OptiScaler.ini");
                var tomlBackup = Path.Combine(backup, "ERSS.toml");
                if (File.Exists(iniBackup)) File.Copy(iniBackup, iniPath, true);
                if (File.Exists(tomlBackup)) File.Copy(tomlBackup, tomlPath, true);
            }
            catch { /* Report the backup path for manual recovery. */ }
            return OperationResult.Fail("法环配置失败；已尝试从备份恢复。", ex.Message, backup);
        }
    }

    public OperationResult ImportRtxMfg(GameEntry game, string source)
    {
        if (!Path.GetFileName(game.ExecutablePath ?? "").Equals("eldenring.exe", StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail("只能导入到法环的实际 EXE 目录。");
        if (Process.GetProcessesByName("eldenring").Length != 0) return OperationResult.Fail("请先完全退出法环。");
        if (!IsModule(source, "RTXMFG") || !IsAtLeast(VersionOf(source), 1, 3, 3, 2))
            return OperationResult.Fail("所选文件不是 RTXMFG 1.3.3 Hotfix 2 或更新版；未复制任何文件。");
        var target = Path.Combine(game.InstallPath, "dinput8.dll");
        if (File.Exists(target)) return OperationResult.Fail("dinput8.dll 已存在；为保护原文件，不会覆盖。请自行检查冲突。");
        try
        {
            var temp = target + ".manager.tmp";
            File.Copy(source, temp, false);
            if (!FileUtilities.Sha256(temp).Equals(FileUtilities.Sha256(source), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 校验失败。");
            File.Move(temp, target, false);
            return OperationResult.Ok("RTXMFG 已从你选择的文件导入为 dinput8.dll；不会随管理器发布。重新启动游戏后才会加载。");
        }
        catch (Exception ex) { return OperationResult.Fail("RTXMFG 导入失败。", ex.Message); }
    }

    private static bool IsModule(string path, string marker)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return new[] { info.ProductName, info.FileDescription, info.OriginalFilename }
                .Any(x => x?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true);
        }
        catch { return false; }
    }

    private static string VersionOf(string path)
    {
        if (!File.Exists(path)) return "未检测到";
        try { return FileVersionInfo.GetVersionInfo(path).FileVersion ?? "版本未知"; }
        catch { return "版本未知"; }
    }

    private static bool IsAtLeast(string text, int major, int minor, int build, int revision = -1)
        => Version.TryParse(text.Replace(',', '.'), out var parsed) &&
           parsed >= (revision < 0 ? new Version(major, minor, build) : new Version(major, minor, build, revision));

    private static bool IsTestedCore(string path)
    {
        try { return FileUtilities.Sha256(path).Equals(TestedOptiCoreSha256, StringComparison.OrdinalIgnoreCase); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
