using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace OptiScalerManager.Core;

public enum RenderPipeline { OptiScaler, ReShadeBridge, Magpie }
public enum KnownGame { Other, EldenRing, Sekiro, Nightreign }

public sealed record PipelineAdvice(KnownGame Game, RenderPipeline Recommended, string Title,
    string Requirements, string Warning, string? GuideUrl);

public static class PipelineAdvisor
{
    public static PipelineAdvice For(GameEntry game)
    {
        var exe = Path.GetFileName(game.ExecutablePath ?? "").ToLowerInvariant();
        var name = game.DisplayName.ToLowerInvariant();
        if (exe == "nightreign.exe" || name.Contains("nightreign") || name.Contains("黑夜君临"))
            return new(KnownGame.Nightreign, RenderPipeline.ReShadeBridge, "艾尔登法环：黑夜君临",
                "未确认可供 OptiScaler 使用的原生超分／DLSS-G 输入。先以 ReShade 桥接作实验；原生 DLSS-G 6×不能保证。",
                "仅供明确关闭反作弊的离线环境测试；不为受保护的在线模式部署注入组件。",
                "https://github.com/NIGos/dlss5-bridge");
        if (exe == "eldenring.exe" || name.Contains("elden ring") && !name.Contains("nightreign") || name.Contains("艾尔登法环") && !name.Contains("黑夜君临"))
            return new(KnownGame.EldenRing,
                File.Exists(Path.Combine(game.InstallPath, "ERSS.dll")) || Directory.Exists(Path.Combine(game.InstallPath, "ERSS2")) || File.Exists(Path.Combine(game.InstallPath, "ERSS-FG.dll")) ? RenderPipeline.OptiScaler : RenderPipeline.ReShadeBridge,
                "艾尔登法环",
                "先从作者取得 ERSS；管理器使用 ERSS DXGI＋Opti winmm＋RTXMFG dinput8 的可选共存配置。高倍率仍须在游戏内验证。",
                "仅离线、无反作弊环境。此组合只在一套 RTX 4080 SUPER／ERSS 5.1.0／Streamline 2.14.1 上人工验证；6×可能增加操作延迟。",
                "https://github.com/optiscaler/OptiScaler/wiki/Elden-Ring-%28ERSS%E2%80%90FG%29");
        if (exe == "sekiro.exe" || name.Contains("sekiro") || name.Contains("只狼"))
            return new(KnownGame.Sekiro,
                Directory.Exists(game.InstallPath) && Directory.EnumerateFiles(game.InstallPath, "*SekiroTSR*", SearchOption.TopDirectoryOnly).Any()
                    ? RenderPipeline.OptiScaler : RenderPipeline.ReShadeBridge,
                "只狼：影逝二度",
                "先安装 SekiroTSR 以提供 DLSS 输入，再用 OptiScaler 的 DX11 路线。仅有 DLSS 输入并不等于已有 DLSS-G；6×尚未验证。",
                "如果输入模组失效，可改用 ReShade 桥接；切勿叠加两套帧生成。",
                "https://github.com/optiscaler/OptiScaler/wiki/SekiroTSR");
        return new(KnownGame.Other, RenderPipeline.OptiScaler, "通用游戏",
            "已有 DLSS／FSR／XeSS 输入优先用 OptiScaler；缺少输入时可尝试 ReShade 桥接；视频或无法注入的窗口用 Magpie。",
            "ReShade 的估算深度／运动矢量不能保证原生 DLSS-G 6×。", null);
    }
}

public static class KnownGamePathResolver
{
    public static string? FindExecutable(string installRoot)
    {
        foreach (var relative in new[] { @"Game\nightreign.exe", @"Game\eldenring.exe", "sekiro.exe", "nightreign.exe", "eldenring.exe" })
        {
            var path = Path.Combine(installRoot, relative);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}

public sealed record KnownGameInstallProfile(string ProxyDll, IReadOnlyList<ConfigChange> IniOverrides,
    IReadOnlyList<string> Notices);

public static class KnownGameInstallProfiles
{
    public static KnownGameInstallProfile For(GameEntry game, string fallbackProxy)
    {
        var kind = PipelineAdvisor.For(game).Game;
        var root = game.InstallPath;
        if (kind == KnownGame.EldenRing)
        {
            var erSs = File.Exists(Path.Combine(root, "ERSS.dll")) ||
                       Directory.Exists(Path.Combine(root, "ERSS2")) || File.Exists(Path.Combine(root, "ERSS-FG.dll"));
            var proxy = "winmm.dll";
            var companion = new EldenRingCompatibilityService().Inspect(game);
            var overrides = companion.ErssPresent && companion.StreamlineReady && companion.RtxMfgPresent
                ? new ConfigChange[] { new("DLSSG", "AdaMfgUnlock", "false"), new("DLSSG", "AdaMfgWrapperOnly", "true"),
                    new("FrameGen", "FGInput", "auto"), new("FrameGen", "FGOutput", "auto") }
                : [];
            return new(proxy, overrides, erSs
                ? ["法环使用 Opti winmm.dll，与 ERSS 的 DXGI.dll 分离。若 ERSS Streamline ≥2.14.1 且已安装 RTXMFG，安装时只写入 Opti 共存配置；ERSS 倍率不会被安装流程覆盖。需要 3× 安全起点时请到“渲染管线”点应用。重启后仍须核对实际倍率。"]
                : ["未检测到 ERSS 输入模组；管理器不会提供或安装付费 ERSS 文件。请先从作者页面自行获取。仅安装 Opti 无法增加法环的原生 DLSS/FG 输入。"]);
        }
        if (kind == KnownGame.Sekiro)
        {
            var reshade = File.Exists(Path.Combine(root, "ReShade64.dll"));
            return new("dxgi.dll", reshade ? [new("Plugins", "LoadReshade", "true")] : [],
                reshade ? ["检测到 ReShade64.dll；安装时自动启用 OptiScaler 的 LoadReshade。还需 SekiroTSR 提供 DLSS 输入。"]
                        : ["未检测到 ReShade64.dll／SekiroTSR 所需的 ReShade 环境。仅安装 OptiScaler 不会给只狼添加 DLSS 输入。"]);
        }
        if (kind == KnownGame.Nightreign)
            return new(fallbackProxy, [], ["黑夜君临尚无已验证的 OptiScaler 输入方案；默认推荐 ReShade 桥接。只在无反作弊的离线环境测试。"]);
        return new(fallbackProxy, [], []);
    }
}

public sealed record BridgeFile(string Source, string Destination, string Sha256, string? Backup);
public sealed record BridgeManifest(string GameRoot, DateTimeOffset InstalledAt, IReadOnlyList<BridgeFile> Files);

/// <summary>Downloads ReShade from its official site for this user; no ReShade binary is redistributed.</summary>
public sealed class OfficialReShadeService
{
    public const string Version = "6.8.0";
    public const string SourceUrl = "https://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe";
    public const string InstallerSha256 = "AFE4C8F13048306307983B8B3D41D5BF00A86820440B0E57DEA10950E1176445";
    public const string ModuleSha256 = "0CEE63F9C9F13F3AC909C5B4903F4DBB4B719A7AB3B4F13B0DEAF83C814B94F7";
    private readonly HttpClient _http;

    public OfficialReShadeService(HttpClient? http = null) => _http = http ?? new HttpClient();

    public async Task<string> FetchModuleAsync(string componentDirectory, CancellationToken ct = default)
    {
        var destination = Path.Combine(componentDirectory, "ReShade64.dll");
        if (File.Exists(destination))
        {
            if (FileUtilities.Sha256(destination).Equals(ModuleSha256, StringComparison.OrdinalIgnoreCase)) return destination;
            throw new InvalidDataException("组件目录已有不同版本的 ReShade64.dll；不会覆盖。请移走旧文件后重试。");
        }
        using var response = await _http.GetAsync(SourceUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 20_000_000) throw new InvalidDataException("ReShade 安装器体积异常。");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        {
            if (memory.Length + read > 20_000_000) throw new InvalidDataException("ReShade 安装器体积异常。");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var installer = memory.ToArray();
        if (!Convert.ToHexString(SHA256.HashData(installer)).Equals(InstallerSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("官网下载文件的 SHA-256 与已验证版本不符；请等待管理器更新来源记录。");
        var module = ExtractModule(installer);
        if (!Convert.ToHexString(SHA256.HashData(module)).Equals(ModuleSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装器内 ReShade64.dll 的 SHA-256 校验失败。");
        Directory.CreateDirectory(componentDirectory);
        var temp = destination + ".download.tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, module, ct);
            File.Move(temp, destination, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return destination;
    }

    public static byte[] ExtractModule(byte[] installer)
    {
        for (var offset = 0; offset <= installer.Length - 30; offset += 512)
        {
            if (installer[offset] != 0x50 || installer[offset + 1] != 0x4B || installer[offset + 2] != 0x03 || installer[offset + 3] != 0x04) continue;
            try
            {
                using var stream = new MemoryStream(installer, offset, installer.Length - offset, false);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                if (archive.GetEntry("ReShade32.dll") is null || archive.GetEntry("ReShade64.dll") is not { } entry || entry.Length is <= 0 or > 20_000_000) continue;
                using var output = new MemoryStream(checked((int)entry.Length));
                using var source = entry.Open();
                source.CopyTo(output);
                return output.ToArray();
            }
            catch (InvalidDataException) { }
        }
        throw new InvalidDataException("官方安装器中未找到 ReShade64.dll；安装器格式可能已变化。");
    }
}

/// <summary>Installs only files supplied by the user. Never replaces an unknown proxy or addon.</summary>
public sealed class ReShadeBridgeService
{
    private static readonly string[] Components = ["dlss5-bridge.addon64", "renodx-dlss5.addon64", "nvngx_dlss.dll", "nvngx_dlssnr.dll"];
    private readonly string _stateRoot;
    public ReShadeBridgeService(string? stateRoot = null) => _stateRoot = stateRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiScalerManager", "pipelines", "reshade");

    private string ManifestPath(GameEntry game) => Path.Combine(_stateRoot, FileUtilities.SafeGameId(Path.GetFullPath(game.InstallPath)), "manifest.json");
    public bool IsManaged(GameEntry game) => File.Exists(ManifestPath(game));

    public OperationPlan Preview(GameEntry game, string sourceDirectory)
    {
        var warnings = new List<string>(); var files = new List<FilePlanEntry>();
        if (!FileUtilities.IsGameDirectory(game.InstallPath)) warnings.Add("目标文件夹没有顶层游戏 EXE。");
        if (IsManaged(game)) warnings.Add("该游戏已有托管的 ReShade 桥接安装；请先卸载。");
        if (!Directory.Exists(sourceDirectory)) warnings.Add("组件文件夹不存在。");
        var optiInstalled = HasOptiScalerProxy(game.InstallPath);
        var existingReshade = new[] { "dxgi.dll", "d3d11.dll", "ReShade64.dll" }
            .Select(name => Path.Combine(game.InstallPath, name)).FirstOrDefault(IsReshadeBinary);
        if (existingReshade is null)
        {
            var source = Path.Combine(sourceDirectory, "ReShade64.dll");
            var destination = Path.Combine(game.InstallPath, optiInstalled ? "ReShade64.dll" : "dxgi.dll");
            if (!File.Exists(source)) warnings.Add("未检测到现有 ReShade；组件文件夹还需 ReShade64.dll（官方 full add-on 版）。");
            else if (File.Exists(destination)) warnings.Add($"目标已存在且未识别为 ReShade，拒绝覆盖：{Path.GetFileName(destination)}");
            else AddNewFile(source, destination);
        }
        foreach (var name in Components)
        {
            var source = Path.Combine(sourceDirectory, name);
            if (!File.Exists(source)) { warnings.Add($"缺少组件：{name}"); continue; }
            var destination = Path.Combine(game.InstallPath, name);
            if (File.Exists(destination))
            {
                if (!FileUtilities.Sha256(source).Equals(FileUtilities.Sha256(destination), StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"目标已有不同版本，拒绝覆盖：{name}");
                continue;
            }
            AddNewFile(source, destination);
        }
        void AddNewFile(string source, string destination)
        {
            if (File.Exists(destination + ".pipeline.tmp")) warnings.Add($"存在未识别的临时文件：{Path.GetFileName(destination)}.pipeline.tmp");
            files.Add(new(source, destination, false, null));
        }
        if (optiInstalled)
        {
            var ini = IniDocument.Load(Path.Combine(game.InstallPath, "OptiScaler.ini"));
            if (!string.Equals(ini.Get("Plugins", "LoadReshade"), "true", StringComparison.OrdinalIgnoreCase))
                warnings.Add("OptiScaler 共存模式要求事先在 INI 中设置 LoadReshade=true；管理器不会暗改现有配置。");
        }
        if (files.Count == 0) warnings.Add("所有组件已存在；没有需要托管的新文件。");
        return new() { Files = files, Warnings = warnings, CanProceed = warnings.Count == 0 };
    }

    private static bool IsReshadeBinary(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return new[] { info.ProductName, info.FileDescription, info.OriginalFilename }
                .Any(value => value?.Contains("ReShade", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch { return false; }
    }

    private static bool HasOptiScalerProxy(string root)
    {
        foreach (var name in new[] { "dxgi.dll", "winmm.dll", "version.dll", "d3d12.dll", "dbghelp.dll", "wininet.dll", "winhttp.dll" })
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path)) continue;
            try { if (FileVersionInfo.GetVersionInfo(path).OriginalFilename?.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) == true) return true; }
            catch { }
        }
        return false;
    }

    public async Task<OperationResult> InstallAsync(GameEntry game, string sourceDirectory, CancellationToken ct = default)
    {
        var plan = Preview(game, sourceDirectory);
        if (!plan.CanProceed) return OperationResult.Fail("桥接组件预检查未通过。", plan.Warnings.ToArray());
        var installed = new List<BridgeFile>();
        var temporary = new List<string>();
        try
        {
            foreach (var file in plan.Files)
            {
                ct.ThrowIfCancellationRequested();
                var temp = file.Destination + ".pipeline.tmp";
                temporary.Add(temp);
                File.Copy(file.Source, temp, false);
                File.Move(temp, file.Destination);
                temporary.Remove(temp);
                var hash = FileUtilities.Sha256(file.Destination);
                if (!hash.Equals(FileUtilities.Sha256(file.Source), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装后校验失败。");
                installed.Add(new(file.Source, file.Destination, hash, null));
            }
            await FileUtilities.WriteJsonAtomicAsync(ManifestPath(game), new BridgeManifest(game.InstallPath, DateTimeOffset.UtcNow, installed), ct);
            return OperationResult.Ok("ReShade＋NR 桥接组件已部署。是否真正运行仍取决于游戏、显卡和模型兼容性。");
        }
        catch (Exception ex)
        {
            foreach (var temp in temporary) try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            foreach (var file in installed) try { if (File.Exists(file.Destination) && FileUtilities.Sha256(file.Destination) == file.Sha256) File.Delete(file.Destination); } catch { }
            return OperationResult.Fail("桥接组件安装失败，已回滚新文件。", ex.Message);
        }
    }

    public async Task<OperationResult> UninstallAsync(GameEntry game, CancellationToken ct = default)
    {
        var path = ManifestPath(game);
        if (!File.Exists(path)) return OperationResult.Fail("没有托管安装记录，不会删除未知文件。");
        try
        {
            var manifest = JsonSerializer.Deserialize<BridgeManifest>(await File.ReadAllTextAsync(path, ct));
            if (manifest is null || !Path.GetFullPath(manifest.GameRoot).Equals(Path.GetFullPath(game.InstallPath), StringComparison.OrdinalIgnoreCase))
                return OperationResult.Fail("安装记录与目标游戏不符。");
            var root = Path.GetFullPath(game.InstallPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var file in manifest.Files)
            {
                if (!Path.GetFullPath(file.Destination).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return OperationResult.Fail("安装记录含越界路径。");
                if (File.Exists(file.Destination) && !FileUtilities.Sha256(file.Destination).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    return OperationResult.Fail($"文件已被其他程序更改，卸载已停止：{Path.GetFileName(file.Destination)}");
            }
            foreach (var file in manifest.Files) if (File.Exists(file.Destination)) File.Delete(file.Destination);
            File.Delete(path);
            return OperationResult.Ok("已移除托管的 ReShade 桥接文件；未碰游戏原有文件。");
        }
        catch (Exception ex) { return OperationResult.Fail("桥接组件卸载失败。", ex.Message); }
    }
}

public sealed class MagpiePipelineService
{
    private readonly string _root;
    private readonly HttpClient _http;
    public MagpiePipelineService(string? root = null, HttpClient? http = null)
    {
        _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiScalerManager", "pipelines", "magpie");
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OptiScalerManager/0.2");
    }
    public async Task<string> InstallLatestExperimentalAsync(CancellationToken ct = default)
    {
        using var releases = await _http.GetFromJsonAsync<JsonDocument>("https://api.github.com/repos/SAOG0721/Magpie/releases?per_page=10", ct)
            ?? throw new InvalidDataException("未取得 SAOG Magpie 发布信息。");
        var selected = releases.RootElement.EnumerateArray()
            .SelectMany(r => r.GetProperty("assets").EnumerateArray().Select(a => new { Tag = r.GetProperty("tag_name").GetString(), Asset = a }))
            .FirstOrDefault(x => x.Asset.GetProperty("name").GetString() == "Magpie-Experimental-x64.zip")
            ?? throw new InvalidDataException("没有找到 Magpie-Experimental-x64.zip。");
        var versionName = Regex.Replace(selected.Tag ?? "experimental", "[^A-Za-z0-9._-]", "_");
        var versionDir = Path.Combine(_root, versionName);
        if (Directory.Exists(versionDir))
        {
            var cachedExe = Directory.EnumerateFiles(versionDir, "Magpie.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (cachedExe is not null) return cachedExe;
            throw new InvalidDataException("本地 Magpie 版本文件夹不完整；请检查后重试。");
        }
        var url = selected.Asset.GetProperty("browser_download_url").GetString() ?? throw new InvalidDataException("缺少下载地址。");
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 1_500_000_000) throw new InvalidDataException("Magpie 包体积异常。");
        using var memory = new MemoryStream();
        await response.Content.CopyToAsync(memory, ct);
        if (memory.Length > 1_500_000_000) throw new InvalidDataException("Magpie 包体积异常。");
        if (selected.Asset.TryGetProperty("digest", out var digestElement) && digestElement.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            var actual = Convert.ToHexString(SHA256.HashData(memory.GetBuffer().AsSpan(0, checked((int)memory.Length))));
            if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Magpie 下载包的 SHA-256 与 GitHub 发布记录不符。");
        }
        memory.Position = 0;
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        if (zip.Entries.Count > 5000 || zip.Entries.Sum(x => x.Length) > 1_500_000_000)
            throw new InvalidDataException("Magpie 压缩包内容异常。");
        var staging = Path.Combine(_root, "staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("压缩包包含越界路径。");
                if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = entry.Open(); await using var output = File.Create(target);
                await input.CopyToAsync(output, ct);
            }
            var exe = Directory.EnumerateFiles(staging, "Magpie.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidDataException("压缩包内未找到 Magpie.exe。");
            if (Directory.Exists(versionDir))
            {
                var existing = Path.Combine(versionDir, Path.GetRelativePath(staging, exe));
                if (!File.Exists(existing)) throw new InvalidDataException("本地 Magpie 版本文件夹不完整；请检查后重试。");
                return existing;
            }
            Directory.Move(staging, versionDir);
            return Path.Combine(versionDir, Path.GetRelativePath(staging, exe));
        }
        finally
        {
            var root = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(staging).StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }

    public static void Launch(string executable)
    {
        if (!File.Exists(executable) || !Path.GetFileName(executable).Equals("Magpie.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("请选择 SAOG Magpie 的 Magpie.exe。", executable);
        Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = true });
    }
}
