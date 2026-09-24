using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using OptiScalerManager.Core;

namespace OptiScalerManager.App;

public partial class UpdateWindow : Window
{
    private readonly ReleaseService _releases = new();
    private ReleaseInfo? _release;
    private string? _packagePath;
    private readonly List<string> _releaseMetadata = [];
    private bool _busy;

    public UpdateWindow()
    {
        InitializeComponent();
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

    private async Task CheckAsync()
    {
        if (_busy) return;
        _busy = true; SetBusy(true, "正在读取 GitHub Release…");
        try
        {
            _release = await _releases.CheckStableAsync();
            if (_release is null)
            {
                ReleaseName.Text = "暂时无法获取稳定版";
                ReleaseMeta.Text = "请检查网络连接，或稍后重试。";
                StatusText.Text = "OFFLINE";
                StatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x45, 0x2B, 0x23));
                DownloadButton.IsEnabled = false;
                Details.Text = "没有修改本地文件。\r\n\r\n更新中心只在你主动打开时访问 GitHub，不会阻塞管理器启动。";
                return;
            }

            ReleaseName.Text = _release.Name;
            ReleaseMeta.Text = $"{_release.TagName}  ·  {_release.PublishedAt.LocalDateTime:g}  ·  {(_release.AssetName ?? "无 ZIP 资产")}";
            StatusText.Text = _release.DownloadUrl is null ? "NO PACKAGE" : "AVAILABLE";
            StatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x19, 0x35, 0x2F));
            DownloadButton.IsEnabled = _release.DownloadUrl is not null;
            Details.Text = "这是官方 OptiScaler 发布信息，仅供查看。此独立分支包含实验性模块，自动下载官方包可能覆盖这些功能，因此下载已禁用。\r\n\r\n" + (_release.Body.Trim() is { Length: > 0 } body ? body : "发布说明为空。");
        }
        catch (Exception ex) { Details.Text = $"检查失败：{ex.Message}"; StatusText.Text = "ERROR"; }
        finally { _busy = false; SetBusy(false, ""); }
    }

    private async void DownloadClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _release?.DownloadUrl is null) return;
        _busy = true; SetBusy(true, "正在下载…");
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiScalerManager", "packages");
            Directory.CreateDirectory(root);
            var fileName = string.IsNullOrWhiteSpace(_release.AssetName) ? $"{_release.TagName}.zip" : _release.AssetName;
            var package = Path.Combine(root, FileUtilities.SafeFileName(fileName));
            _packagePath = await _releases.DownloadAsync(_release, package, new Progress<OperationProgress>(p => PackageState.Text = $"{p.Message}  {p.Percent}%"));
            if (_packagePath is null) throw new InvalidDataException("Release did not provide a downloadable package.");
            if (!string.IsNullOrWhiteSpace(_release.ManifestDownloadUrl))
            {
                var manifestText = await _releases.DownloadTextAsync(_release.ManifestDownloadUrl);
                var manifestPath = package + ".manifest.json";
                await File.WriteAllTextAsync(manifestPath, manifestText);
                using var manifest = System.Text.Json.JsonDocument.Parse(manifestText);
                _releaseMetadata.Clear();
                foreach (var propertyName in new[] { "managerVersion", "optiscalerVersion", "releaseChannel", "releaseDate", "minimumWindowsVersion", "supportedArchitecture", "packageSha256", "upstreamCommits", "runtimeVersions", "licenseFiles", "licensePolicies" })
                    if (manifest.RootElement.TryGetProperty(propertyName, out var value)) _releaseMetadata.Add($"{propertyName}: {(value.ValueKind is System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array ? value.GetRawText() : value.ToString())}");
                if (manifest.RootElement.TryGetProperty("packageSha256", out var expectedElement))
                {
                    var expected = expectedElement.GetString();
                    var actual = FileUtilities.Sha256(package);
                    if (!string.IsNullOrWhiteSpace(expected) && !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Package SHA256 does not match the release manifest.");
                }
            }
            var inspection = await _releases.InspectPackageAsync(_packagePath);
            var report = new StringBuilder();
            report.AppendLine($"文件：{_packagePath}");
            report.AppendLine($"SHA256：{inspection.Sha256}");
            report.AppendLine($"大小：{inspection.SizeBytes / 1024d / 1024d:0.00} MB");
            report.AppendLine($"ZIP：{(inspection.IsSupportedArchive ? "支持" : "不支持")}");
            report.AppendLine($"OptiScaler.dll：{(inspection.HasOptiScalerDll ? "存在" : "缺失")}");
            report.AppendLine($"Manager：{(inspection.HasManager ? "存在" : "可选，缺失")}");
            report.AppendLine($"路径安全：{(inspection.IsSafe ? "通过" : "失败")}");
            if (_releaseMetadata.Count > 0) { report.AppendLine(); report.AppendLine("发布清单："); foreach (var item in _releaseMetadata) report.AppendLine(item); }
            if (inspection.Warnings.Count > 0) { report.AppendLine(); report.AppendLine("注意："); foreach (var warning in inspection.Warnings) report.AppendLine("- " + warning); }
            if (inspection.Errors.Count > 0) { report.AppendLine(); report.AppendLine("错误："); foreach (var error in inspection.Errors) report.AppendLine("- " + error); }
            Details.Text = report.ToString();
            if (!inspection.IsSafe || !inspection.HasOptiScalerDll) throw new InvalidDataException("下载包未通过安全检查，未解包。");

            var versionDir = Path.Combine(root, FileUtilities.SafeFileName(_release.TagName));
            if (Directory.Exists(versionDir)) Directory.Delete(versionDir, true);
            await ReleaseService.ExtractPackageAsync(_packagePath, versionDir);
            await FileUtilities.WriteJsonAtomicAsync(Path.Combine(root, "latest-package.json"), new { Release = _release.TagName, Package = _packagePath, Extracted = versionDir, inspection.Sha256, Date = DateTimeOffset.UtcNow });
            PackageState.Text = "已下载并通过检查";
            StatusText.Text = "READY TO INSTALL";
            Details.Text += $"\r\n解包目录：{versionDir}\r\n\r\n现在可以关闭窗口，在游戏页面点击“安装 / 修复”。";
        }
        catch (Exception ex) { PackageState.Text = "检查失败"; Details.Text += $"\r\n\r\n失败：{ex.Message}"; }
        finally { _busy = false; SetBusy(false, ""); }
    }

    private void SetBusy(bool busy, string state)
    {
        CheckButton.IsEnabled = !busy; DownloadButton.IsEnabled = !busy && _release?.DownloadUrl is not null;
        if (!string.IsNullOrWhiteSpace(state)) PackageState.Text = state;
        Mouse.OverrideCursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
