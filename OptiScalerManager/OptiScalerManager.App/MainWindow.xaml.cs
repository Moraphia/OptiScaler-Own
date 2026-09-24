using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using OptiScalerManager.Core;

namespace OptiScalerManager.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<GameEntry> _games = [];
    private readonly SteamDiscoveryService _discovery = new();
    private readonly InstallationService _installer;
    private readonly ReleaseService _releases = new();
    private readonly ObservableCollection<string> _operationLog = [];
    private readonly string _gamesFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiScalerManager", "games.json");

    public MainWindow()
    {
        InitializeComponent(); _installer = new InstallationService(_discovery); GamesList.ItemsSource = _games; OperationTimeline.ItemsSource = _operationLog; LanguageBox.SelectedValue = LanguageService.CurrentCulture;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (SettingsStore.Current.ReduceAnimations)
        {
            PageRoot.Opacity = 1;
            if (PageRoot.RenderTransform is System.Windows.Media.TranslateTransform transform) transform.Y = 0;
        }
        else ((Storyboard)FindResource("PageEnter")).Begin(this);
        if (File.Exists(_gamesFile)) try { foreach (var game in JsonSerializer.Deserialize<List<GameEntry>>(await File.ReadAllTextAsync(_gamesFile)) ?? []) _games.Add(game); } catch { }
        UpdateCount(); if (_games.Count > 0) GamesList.SelectedIndex = 0;
    }

    private async void AddGameClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择游戏主程序", Filter = "游戏程序 (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        var path = Path.GetDirectoryName(dialog.FileName)!; var candidate = new GameEntry { Id = new GameId(FileUtilities.SafeGameId(path)), DisplayName = Path.GetFileNameWithoutExtension(dialog.FileName), InstallPath = path, ExecutablePath = dialog.FileName, Launcher = "Manual" };
        if (_games.Any(x => x.Id == candidate.Id)) { GamesList.SelectedItem = _games.First(x => x.Id == candidate.Id); return; }
        _games.Add(candidate); await SaveGamesAsync(); UpdateCount(); GamesList.SelectedItem = candidate;
    }

    private async void ScanSteamClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait; var candidates = await _discovery.ScanSteamAsync();
            var added = candidates.Where(c => !_games.Any(g => g.InstallPath.Equals(c.InstallPath, StringComparison.OrdinalIgnoreCase))).ToList();
            if (added.Count == 0) { MessageBox.Show(this, "没有发现新的 Steam 游戏。", "Steam 扫描", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var result = MessageBox.Show(this, $"发现 {added.Count} 个候选游戏，是否全部加入管理列表？\n\n" + string.Join("\n", added.Take(12).Select(x => "• " + x.DisplayName)), "确认扫描结果", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            foreach (var c in added) _games.Add(new GameEntry { Id = new GameId(FileUtilities.SafeGameId(c.InstallPath)), DisplayName = c.DisplayName, InstallPath = c.InstallPath, ExecutablePath = c.ExecutablePath, Launcher = c.Launcher });
            await SaveGamesAsync(); UpdateCount();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "扫描失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { Mouse.OverrideCursor = null; }
    }

    private async void GameSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (GamesList.SelectedItem is GameEntry game) await InspectAsync(game); }
    private async void RefreshClick(object sender, RoutedEventArgs e) { if (GamesList.SelectedItem is GameEntry game) await InspectAsync(game); }
    private async Task InspectAsync(GameEntry game)
    {
        try
        {
            var result = await _discovery.InspectAsync(game.InstallPath);
            var manifest = Path.Combine(game.InstallPath, "OptiScaler", "RuntimeSync", "manifest.json");
            var integrity = File.Exists(manifest) ? await _installer.VerifyAsync(game) : null;
            var needsRepair = integrity is not null && !integrity.Success;
            SelectedName.Text = game.DisplayName;
            SelectedPath.Text = game.InstallPath;
            InstallStatus.Text = needsRepair ? "需要修复" : result.InstallState switch { InstallState.Installed => "已安装", InstallState.Partial => "安装不完整", InstallState.Legacy => "遗留安装", InstallState.NeedsRepair => "需要修复", _ => "未安装" };
            InstallStatus.Foreground = needsRepair ? (System.Windows.Media.Brush)FindResource("Warning") : result.InstallState == InstallState.Installed ? (System.Windows.Media.Brush)FindResource("Success") : (System.Windows.Media.Brush)FindResource("Warning");
            InstallDetail.Text = integrity is not null ? integrity.Message : result.RuntimeSyncStatus ?? "等待安装";
            ProxyText.Text = result.ProxyDll ?? "未检测到";
            SyncText.Text = result.RuntimeSyncStatus ?? "未配置";
            BackupText.Text = result.HasBackup ? "可恢复" : "无备份";
            HealthText.Text = needsRepair ? "CHECK" : integrity?.Success == true || result.InstallState == InstallState.Installed ? "READY" : "CHECK";
            RuntimeText.Text = result.RuntimeFiles.Count > 0 ? $"{result.RuntimeFiles.Count} 文件" : "未检测";
            RuntimeGrid.ItemsSource = result.RuntimeFiles.Where(x => x.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
            AdoptLegacyButton.Visibility = result.InstallState == InstallState.Legacy ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { InstallStatus.Text = "检查失败"; InstallDetail.Text = ex.Message; }
    }

    private async void AdoptLegacyClick(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameEntry game) return;
        var result = MessageBox.Show(this, "管理器会备份旧 BAT 安装留下的文件并记录接管信息，不会删除旧文件。是否继续？", "接管旧安装", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        var adoption = await _installer.AdoptLegacyAsync(game);
        MessageBox.Show(this, adoption.Message, "旧安装接管", MessageBoxButton.OK, adoption.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        await InspectAsync(game);
    }

    private async void InstallClick(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameEntry game) { MessageBox.Show(this, "请先选择游戏。", "安装", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var package = FindPackageDirectory(); if (package is null) { MessageBox.Show(this, "请先在更新中心下载一个经过检查的安装包，或将发布包解压到管理器的 Package 目录。", "缺少安装包", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        OperationPlan plan;
        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            plan = await _installer.PreviewInstallAsync(game, new InstallOptions(package, SettingsStore.Current.ProxyDll));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "安装预检查失败", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        finally { Mouse.OverrideCursor = null; }
        if (!plan.CanProceed)
        {
            MessageBox.Show(this, string.Join("\n", plan.Warnings), "无法安全安装", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (new InstallPreviewWindow(game, plan) { Owner = this }.ShowDialog() != true) return;
        _operationLog.Clear(); AddOperation("开始安装预览已确认");
        var result = await _installer.InstallAsync(game, new InstallOptions(package, SettingsStore.Current.ProxyDll), new Progress<OperationProgress>(ReportOperation));
        AddOperation(result.Success ? "完成 · 安装成功" : $"失败 · {result.Message}");
        MessageBox.Show(this, result.Message, result.Success ? "安装完成" : "安装失败", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error); await InspectAsync(game);
    }
    private async void UninstallClick(object sender, RoutedEventArgs e) { if (GamesList.SelectedItem is GameEntry game && MessageBox.Show(this, "确定恢复原始文件并卸载 OptiScaler？", "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) { _operationLog.Clear(); AddOperation("开始卸载"); var result = await _installer.UninstallAsync(game, new Progress<OperationProgress>(ReportOperation)); AddOperation(result.Success ? "完成 · 原始文件已恢复" : $"失败 · {result.Message}"); MessageBox.Show(this, result.Message, "卸载", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error); await InspectAsync(game); } }
    private void LaunchClick(object sender, RoutedEventArgs e) { if (GamesList.SelectedItem is GameEntry game) try { ProcessService.Launch(game); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error); } }
    private void UpdateClick(object sender, RoutedEventArgs e) => new UpdateWindow { Owner = this }.ShowDialog();
    private void SettingsClick(object sender, RoutedEventArgs e) => new SettingsWindow(GamesList.SelectedItem as GameEntry) { Owner = this }.ShowDialog();
    private void UpdateCount() => GameCount.Text = _games.Count.ToString();
    private void LanguageChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LanguageBox?.SelectedValue is string culture) LanguageService.SetLanguage(culture);
    }
    private void ReportOperation(OperationProgress progress)
    {
        InstallDetail.Text = $"{progress.Message}  {progress.Percent}%";
        AddOperation($"{progress.Stage,-9} {progress.Percent,3}%  {progress.Message}");
    }
    private void AddOperation(string message)
    {
        _operationLog.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        if (_operationLog.Count > 80) _operationLog.RemoveAt(0);
        if (_operationLog.Count > 0) OperationTimeline.ScrollIntoView(_operationLog[^1]);
    }
    private static string? FindPackageDirectory()
    {
        var downloaded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiScalerManager", "packages", "latest-package.json");
        if (File.Exists(downloaded))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(downloaded));
                var extracted = doc.RootElement.GetProperty("Extracted").GetString();
                if (!string.IsNullOrWhiteSpace(extracted) && Directory.Exists(extracted)) return extracted;
            }
            catch { }
        }
        if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "OptiScaler"))) return AppContext.BaseDirectory;
        var local = Path.Combine(AppContext.BaseDirectory, "Package");
        return Directory.Exists(local) ? local : null;
    }
    private async Task SaveGamesAsync() { Directory.CreateDirectory(Path.GetDirectoryName(_gamesFile)!); await FileUtilities.WriteJsonAtomicAsync(_gamesFile, _games.ToList()); }
}
