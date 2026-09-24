using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using OptiScalerManager.Core;

namespace OptiScalerManager.App;

public partial class UpdateWindow : Window
{
    private ReleaseService _releases;
    private string? _ownRepository = "Moraphia/OptiScaler-Own";
    private ReleaseCatalog? _catalog;
    private readonly List<string> _releaseMetadata = [];
    private bool _busy;

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
        Loaded += async (_, _) => await CheckAsync();
        Loaded += async (_, _) => await LoadDependenciesAsync();
        Closed += (_, _) => _releases.Dispose();
    }

    private async Task LoadDependenciesAsync()
    {
        var root = AppContext.BaseDirectory;
        DependencyGrid.ItemsSource = await new UpstreamService().LoadMatrixAsync(Path.Combine(root, "upstreams.json"), Path.Combine(root, "upstreams.lock.json"));
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
            _catalog = await _releases.CheckStableCatalogAsync();
            if (_catalog is null)
            {
                ReleaseName.Text = "暂时无法获取稳定版";
                ReleaseMeta.Text = "请检查网络连接，或稍后重试。";
                StatusText.Text = "OFFLINE";
                StatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x45, 0x2B, 0x23));
                DownloadManagerButton.IsEnabled = false;
                DownloadGameButton.IsEnabled = false;
                Details.Text = "没有修改本地文件。\r\n\r\n更新中心只在你主动打开时访问 GitHub，不会阻塞管理器启动。";
                return;
            }

            ReleaseName.Text = _catalog.Name;
            ReleaseMeta.Text = $"{_catalog.TagName}  ·  {_catalog.PublishedAt.LocalDateTime:g}  ·  管理器：{(_catalog.Manager is null ? "未发布" : "可下载")}  ·  游戏包：{(_catalog.Game is null ? "未发布" : "可下载")}";
            StatusText.Text = _catalog.Manager is null && _catalog.Game is null ? "NO PACKAGE" : "AVAILABLE";
            StatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x19, 0x35, 0x2F));
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
                await FileUtilities.WriteJsonAtomicAsync(Path.Combine(root, "latest-package.json"), new { Release = release.TagName, Package = packagePath, Extracted = versionDir, inspection.Sha256, VerifiedOwnChannel = true, Date = DateTimeOffset.UtcNow });
            PackageState.Text = "已下载并通过检查";
            StatusText.Text = release.PackageType == "game" ? "READY TO INSTALL" : "MANAGER READY";
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
