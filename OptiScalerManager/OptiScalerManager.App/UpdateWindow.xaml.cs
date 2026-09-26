using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using OptiScalerManager.Core;

namespace OptiScalerManager.App;

public partial class UpdateWindow : Wpf.Ui.Controls.FluentWindow
{
    private ReleaseService _releases;
    private string? _ownRepository = "Moraphia/OptiScaler-Own";
    private ReleaseCatalog? _catalog;
    private readonly List<string> _releaseMetadata = [];
    private bool _busy;
    private readonly string _managerVersion = FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "OptiScalerManager.exe")).FileVersion ?? "未知";
    private string? _localPackageVersion;

    public UpdateWindow()
    {
        InitializeComponent();
        var channelPath = Path.Combine(AppContext.BaseDirectory, "release-channel.json");
        if (File.Exists(channelPath))
        {
            try
            {
                using var channel = System.Text.Json.JsonDocument.Parse(File.ReadAllText(channelPath));
                _ownRepository = channel.RootElement.GetProperty("repository").GetString();
            }
            catch (Exception ex) { MessageBox.Show(this, $"更新源配置无效：{ex.Message}", "更新中心", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        _releases = new ReleaseService(_ownRepository);
        RepositoryBox.Text = _ownRepository ?? "";
        ManagerVersionState.Text = $"管理器：当前 {_managerVersion} · 等待检查";
        Loaded += async (_, _) => await CheckAsync();
        Loaded += async (_, _) => await LoadDependenciesAsync();
        Closed += (_, _) => _releases.Dispose();
    }

    private async Task LoadDependenciesAsync()
    {
        var root = AppContext.BaseDirectory;
        try
        {
            var inventory = await DependencyInventoryService.LoadAsync(Path.Combine(root, "dependency-inventory.json"));
            var comparisons = await DependencyInventoryService.CompareLocalPackageAsync(inventory, Path.Combine(root, "Package"));
            InventorySummary.Text = $"正式游戏包 v{inventory.PackageVersion} 快照：{inventory.Files.Count} 个 DLL · 本机 Package 一致 {comparisons.Count(x => x.LocalStatus == "一致")}／不同 {comparisons.Count(x => x.LocalStatus == "与快照不同")}／缺失 {comparisons.Count(x => x.LocalStatus == "缺失")}。快照包 SHA-256：{inventory.PackageSha256}";
            RuntimeGrid.ItemsSource = comparisons;
            var submodules = await DependencyInventoryService.LoadSubmodulesAsync(Path.Combine(root, "source-submodules.json"));
            SubmoduleSummary.Text = $"源码 Git 子模块 {submodules.Count} 个；这里的 commit 是构建所用 gitlink，不是上游最新版本。";
            SubmoduleGrid.ItemsSource = submodules;
            var upstreams = await new UpstreamService().LoadMatrixAsync(Path.Combine(root, "upstreams.json"), Path.Combine(root, "upstreams.lock.json"));
            UpstreamSummary.Text = $"外部追踪 {upstreams.Count} 项；记录版本不是包内 DLL 版本，未标时间的记录不能判定是否有更新。";
            DependencyGrid.ItemsSource = upstreams;
            var pipelineRows = await DependencyInventoryService.LoadPipelineAsync(Path.Combine(root, "pipeline-dependencies.json"));
            PipelineDependencySummary.Text = $"渲染管线额外前置 {pipelineRows.Count} 项。它们不是正式游戏包内的 28 个 DLL；记录版本不代表本机已安装或已验证运行。";
            PipelineDependencyGrid.ItemsSource = pipelineRows;
        }
        catch (Exception ex) { InventorySummary.Text = $"依赖清单加载失败：{ex.Message}"; }
    }

    private static async Task<string?> ReadLocalPackageVersionAsync()
    {
        var adjacent = Path.Combine(AppContext.BaseDirectory, "Package");
        var versionFile = Path.Combine(adjacent, "package-version.json");
        var coreFile = Path.Combine(adjacent, "OptiScaler.dll");
        if (File.Exists(versionFile) && File.Exists(coreFile))
        {
            try
            {
                using var versionDocument = JsonDocument.Parse(await File.ReadAllTextAsync(versionFile));
                var metadata = versionDocument.RootElement;
                var expectedCore = metadata.GetProperty("coreSha256").GetString();
                if (expectedCore is not null && (await Task.Run(() => FileUtilities.Sha256(coreFile))).Equals(expectedCore, StringComparison.OrdinalIgnoreCase))
                    return metadata.GetProperty("packageVersion").GetString();
            }
            catch (Exception) { /* An untrusted or incomplete local package remains unknown. */ }
        }
        var marker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiScalerManager", "packages", "latest-package.json");
        if (!File.Exists(marker)) return null;
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(marker));
            var entry = document.RootElement;
            var packagePath = entry.GetProperty("Package").GetString();
            var expectedHash = entry.GetProperty("Sha256").GetString();
            var tag = entry.GetProperty("Release").GetString();
            if (packagePath is null || expectedHash is null || !File.Exists(packagePath)) return null;
            var actualHash = await Task.Run(() => FileUtilities.Sha256(packagePath));
            return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase) ? tag : null;
        }
        catch (Exception) { return null; }
    }

    private void ShowVersionStates()
    {
        var latest = _catalog?.TagName;
        var managerState = UpdateVersion.Compare(_managerVersion, latest, _catalog?.Manager is not null);
        var packageState = UpdateVersion.Compare(_localPackageVersion, latest, _catalog?.Game is not null);
        ManagerVersionState.Text = $"管理器：当前 {_managerVersion} · 最新 {latest ?? "未知"} · {managerState}";
        PackageVersionState.Text = $"本机已下载游戏包：当前 {_localPackageVersion ?? "未知"} · 最新 {latest ?? "未知"} · {packageState}";
        StatusText.Text = managerState == "需要更新" || packageState == "需要更新" ? "需要更新" :
            managerState == "已是最新" && packageState == "已是最新" ? "已是最新" : "请核对版本";
        StatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(
            StatusText.Text == "需要更新" ? (byte)0x45 : (byte)0x19,
            StatusText.Text == "需要更新" ? (byte)0x32 : (byte)0x35,
            StatusText.Text == "需要更新" ? (byte)0x20 : (byte)0x2F));
    }

    private async void CheckClick(object sender, RoutedEventArgs e) => await CheckAsync();

    private async void SetRepositoryClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var requested = RepositoryBox.Text.Trim();
        try
        {
            var replacement = new ReleaseService(requested.Length == 0 ? null : requested);
            var path = Path.Combine(AppContext.BaseDirectory, "release-channel.json");
            await FileUtilities.WriteJsonAtomicAsync(path, new { repository = requested.Length == 0 ? (string?)null : requested });
            _releases.Dispose();
            _releases = replacement;
            _ownRepository = requested.Length == 0 ? null : requested;
            _catalog = null;
            await CheckAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, $"更新源未保存：{ex.Message}", "更新中心", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async Task CheckAsync()
    {
        if (_busy) return;
        _busy = true; SetBusy(true, "正在读取 GitHub Release…");
        try
        {
            _localPackageVersion = await ReadLocalPackageVersionAsync();
            _catalog = await _releases.CheckStableCatalogAsync();
            if (_catalog is null)
            {
                ReleaseName.Text = "暂时无法获取稳定版";
                ReleaseMeta.Text = "请检查网络连接，或稍后重试。";
                StatusText.Text = "OFFLINE";
                ManagerVersionState.Text = $"管理器：当前 {_managerVersion} · 最新未知";
                PackageVersionState.Text = $"本机已下载游戏包：当前 {_localPackageVersion ?? "未知"} · 最新未知";
                StatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x45, 0x2B, 0x23));
                DownloadManagerButton.IsEnabled = false;
                DownloadGameButton.IsEnabled = false;
                Details.Text = "没有修改本地文件。\r\n\r\n更新中心只在你主动打开时访问 GitHub，不会阻塞管理器启动。";
                return;
            }

            ReleaseName.Text = _catalog.Name;
            ReleaseMeta.Text = $"{_catalog.TagName}  ·  {_catalog.PublishedAt.LocalDateTime:g}  ·  管理器：{(_catalog.Manager is null ? "未发布" : "可下载")}  ·  游戏包：{(_catalog.Game is null ? "未发布" : "可下载")}";
            ShowVersionStates();
            DownloadManagerButton.IsEnabled = _catalog.Manager is not null;
            DownloadGameButton.IsEnabled = _catalog.Game is not null;
            Details.Text = (_ownRepository is null ? "这是官方发布信息，仅供查看。请配置自己的稳定版 GitHub Release 仓库。" : $"稳定版更新源：{_ownRepository}。管理器和游戏包必须使用各自的精确文件名与校验清单；管理器下载后不会在运行时覆盖自身。") + "\r\n\r\n" + (_catalog.Body.Trim() is { Length: > 0 } body ? body : "发布说明为空。");
        }
        catch (Exception ex) { Details.Text = $"检查失败：{ex.Message}"; StatusText.Text = "ERROR"; }
        finally { _busy = false; SetBusy(false, ""); }
    }

    private async void DownloadManagerClick(object sender, RoutedEventArgs e)
    {
        if (_catalog?.Manager is { } release) await DownloadAsync(release);
    }

    private async void DownloadGameClick(object sender, RoutedEventArgs e)
    {
        if (_catalog?.Game is { } release) await DownloadAsync(release);
    }

    private async Task DownloadAsync(ReleaseInfo release)
    {
        if (_busy || release.DownloadUrl is null) return;
        _busy = true; SetBusy(true, "正在下载…");
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiScalerManager", "packages");
            Directory.CreateDirectory(root);
            var fileName = release.AssetName ?? throw new InvalidDataException("Release asset name is missing.");
            var package = Path.Combine(root, FileUtilities.SafeFileName(fileName));
            var packagePath = await _releases.DownloadAsync(release, package, new Progress<OperationProgress>(p => PackageState.Text = $"{p.Message}  {p.Percent}%"));
            if (packagePath is null) throw new InvalidDataException("Release did not provide a downloadable package.");
            if (string.IsNullOrWhiteSpace(release.ManifestDownloadUrl)) throw new InvalidDataException("独立分支发布缺少校验清单。");
            {
                var manifestText = await _releases.DownloadTextAsync(release.ManifestDownloadUrl);
                var manifestPath = package + ".manifest.json";
                await File.WriteAllTextAsync(manifestPath, manifestText);
                using var manifest = System.Text.Json.JsonDocument.Parse(manifestText);
                _releaseMetadata.Clear();
                foreach (var propertyName in new[] { "managerVersion", "optiscalerVersion", "releaseChannel", "releaseDate", "minimumWindowsVersion", "supportedArchitecture", "packageSha256", "upstreamCommits", "runtimeVersions", "licenseFiles", "licensePolicies" })
                    if (manifest.RootElement.TryGetProperty(propertyName, out var value)) _releaseMetadata.Add($"{propertyName}: {(value.ValueKind is System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array ? value.GetRawText() : value.ToString())}");
                ReleaseManifestValidator.Validate(manifestText, release, FileUtilities.Sha256(package));
            }
            var inspection = await _releases.InspectPackageAsync(packagePath);
            var report = new StringBuilder();
            report.AppendLine($"类型：{(release.PackageType == "manager" ? "管理器" : "游戏安装包")}");
            report.AppendLine($"文件：{packagePath}");
            report.AppendLine($"SHA256：{inspection.Sha256}");
            report.AppendLine($"大小：{inspection.SizeBytes / 1024d / 1024d:0.00} MB");
            report.AppendLine($"ZIP：{(inspection.IsSupportedArchive ? "支持" : "不支持")}");
            report.AppendLine($"OptiScaler.dll：{(inspection.HasOptiScalerDll ? "存在" : "缺失")}");
            report.AppendLine($"OptiScaler.ini：{(inspection.HasOptiScalerIni ? "存在" : "缺失")}");
            report.AppendLine($"Manager：{(inspection.HasManager ? "存在" : "可选，缺失")}");
            report.AppendLine($"路径安全：{(inspection.IsSafe ? "通过" : "失败")}");
            if (_releaseMetadata.Count > 0) { report.AppendLine(); report.AppendLine("发布清单："); foreach (var item in _releaseMetadata) report.AppendLine(item); }
            if (inspection.Warnings.Count > 0) { report.AppendLine(); report.AppendLine("注意："); foreach (var warning in inspection.Warnings) report.AppendLine("- " + warning); }
            if (inspection.Errors.Count > 0) { report.AppendLine(); report.AppendLine("错误："); foreach (var error in inspection.Errors) report.AppendLine("- " + error); }
            Details.Text = report.ToString();
            if (!inspection.IsSafe || (release.PackageType == "manager" ? !inspection.HasManager || !inspection.HasManagerCore : !inspection.HasOptiScalerDll || !inspection.HasOptiScalerIni))
                throw new InvalidDataException("下载包未通过对应类型的安全检查，未解包。");

            var versionDir = Path.Combine(root, FileUtilities.SafeFileName(release.TagName + "-" + release.PackageType + "-" + inspection.Sha256[..12]));
            await ReleaseService.ExtractPackageAsync(packagePath, versionDir, release.PackageType);
            if (release.PackageType == "game")
            {
                await FileUtilities.WriteJsonAtomicAsync(Path.Combine(root, "latest-package.json"), new { Release = release.TagName, Package = packagePath, Extracted = versionDir, inspection.Sha256, VerifiedOwnChannel = true, Date = DateTimeOffset.UtcNow });
                _localPackageVersion = release.TagName;
            }
            PackageState.Text = "已下载并通过检查";
            ShowVersionStates();
            Details.Text += $"\r\n解包目录：{versionDir}\r\n\r\n" + (release.PackageType == "game" ? "现在可以关闭窗口，在游戏页面点击“安装 / 修复”。" : "这是独立的管理器更新。请先退出当前管理器，再把此目录中的管理器文件复制到原管理器目录；保留原有 Package 文件夹。下载过程不会覆盖正在运行的程序。");
        }
        catch (Exception ex) { PackageState.Text = "检查失败"; Details.Text += $"\r\n\r\n失败：{ex.Message}"; }
        finally { _busy = false; SetBusy(false, ""); }
    }

    private void SetBusy(bool busy, string state)
    {
        CheckButton.IsEnabled = !busy;
        DownloadManagerButton.IsEnabled = !busy && _catalog?.Manager is not null;
        DownloadGameButton.IsEnabled = !busy && _catalog?.Game is not null;
        if (!string.IsNullOrWhiteSpace(state)) PackageState.Text = state;
        Mouse.OverrideCursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
