using Microsoft.Win32;
using OptiScalerManager.Core;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace OptiScalerManager.App;

public partial class PipelineWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly GameEntry? _game;
    private readonly PipelineAdvice _advice;
    private readonly ReShadeBridgeService _bridge = new();
    private readonly OfficialReShadeService _reshade = new();
    private readonly MagpiePipelineService _magpie = new();
    private readonly EldenRingCompatibilityService _eldenRing = new();
    private string? _bridgeFolder;
    private string? _magpieExe;

    public PipelineWindow(GameEntry? game)
    {
        _game = game;
        _advice = game is null ? new(KnownGame.Other, RenderPipeline.Magpie, "视频与窗口",
            "视频与互动影像建议使用独立窗口处理管线；不依赖游戏的超分接口。", "视频插帧不等于原生 DLSS-G 6×。", null) : PipelineAdvisor.For(game);
        InitializeComponent();
        GameTitle.Text = $"{_advice.Title} · 推荐：{_advice.Recommended}";
        AdviceText.Text = _advice.Requirements;
        PrerequisiteText.Text = GetPrerequisiteStatus(game, _advice.Game);
        WarningText.Text = _advice.Warning;
        EldenRingCard.Visibility = _advice.Game == KnownGame.EldenRing ? Visibility.Visible : Visibility.Collapsed;
        RefreshEldenRingStatus();
        OfflineConfirmation.Visibility = _advice.Game == KnownGame.Nightreign ? Visibility.Visible : Visibility.Collapsed;
        ChooseBridgeButton.IsEnabled = game is not null;
        InstallBridgeButton.IsEnabled = game is not null;
        UninstallBridgeButton.IsEnabled = game is not null;
        var staged = Path.Combine(AppContext.BaseDirectory, "BridgeComponents");
        if (game is not null && File.Exists(Path.Combine(staged, "renodx-dlss5.addon64")))
        {
            _bridgeFolder = staged;
            BridgeFolderText.Text = staged;
            var plan = _bridge.Preview(game, staged);
            ResultText.Text = plan.CanProceed ? $"可安装 {plan.Files.Count} 个缺失组件；已有文件保持原样。" : string.Join("\n", plan.Warnings);
        }
    }

    private static string GetPrerequisiteStatus(GameEntry? game, KnownGame kind)
    {
        if (game is null) return "";
        var root = game.InstallPath;
        if (!Directory.Exists(root)) return "游戏目录不存在，无法检查前置组件。";
        if (kind == KnownGame.EldenRing)
        {
            var erSs = File.Exists(Path.Combine(root, "ERSS.dll")) || Directory.Exists(Path.Combine(root, "ERSS2")) || File.Exists(Path.Combine(root, "ERSS-FG.dll"));
            var opti = File.Exists(Path.Combine(root, "OptiScaler.ini"));
            return $"前置检查：ERSS {(erSs ? "已检测到" : "未检测到")}；游戏 EXE 目录 OptiScaler {(opti ? "已检测到" : "未检测到")}。文件检测不等于运行时已生效。";
        }
        if (kind == KnownGame.Sekiro)
        {
            var tsr = Directory.EnumerateFiles(root, "*SekiroTSR*", SearchOption.TopDirectoryOnly).Any();
            var reshade = File.Exists(Path.Combine(root, "ReShade64.dll"));
            var ini = Path.Combine(root, "OptiScaler.ini");
            var chained = File.Exists(ini) && string.Equals(IniDocument.Load(ini).Get("Plugins", "LoadReshade"), "true", StringComparison.OrdinalIgnoreCase);
            return $"前置检查：SekiroTSR {(tsr ? "已检测到" : "未检测到")}；ReShade64.dll {(reshade ? "已检测到" : "未检测到")}；OptiScaler LoadReshade {(chained ? "已启用" : "未启用")}。";
        }
        return "";
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void GuideClick(object sender, RoutedEventArgs e) => OpenUrl(_advice.GuideUrl ?? "https://github.com/optiscaler/OptiScaler/wiki/Compatibility-List");
    private void BridgeSourceClick(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/NIGos/dlss5-bridge/releases");
    private void MagpieSourceClick(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/SAOG0721/Magpie/releases");

    private void RefreshEldenRingStatus()
    {
        if (_game is null || _advice.Game != KnownGame.EldenRing) return;
        EldenRingStatus.Text = _eldenRing.Inspect(_game).Detail;
    }

    private void ErssSourceClick(object sender, RoutedEventArgs e) => OpenUrl(EldenRingCompatibilityService.ErssSourceUrl);
    private void RtxMfgSourceClick(object sender, RoutedEventArgs e) => OpenUrl(EldenRingCompatibilityService.RtxMfgSourceUrl);

    private void ImportRtxMfgClick(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        var dialog = new OpenFileDialog { Title = "选择从 RTXMFG 作者处取得的 RTXMFG.dll", Filter = "RTXMFG DLL|*.dll", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, $"仅在目标 dinput8.dll 不存在时复制：\n{dialog.FileName}\n→ {_game.InstallPath}\\dinput8.dll\n\n继续？",
            "导入自备 RTXMFG", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var result = _eldenRing.ImportRtxMfg(_game, dialog.FileName);
        ResultText.Text = result.Message + (result.Errors.Count > 0 ? " " + string.Join("；", result.Errors) : "");
        RefreshEldenRingStatus();
    }

    private async Task SetEldenRingUnlockAsync(bool enable)
    {
        if (_game is null) return;
        var message = enable
            ? "将备份 OptiScaler.ini 与 ERSS.toml，配置 3× 起点及可选 4–6× 解锁。游戏必须完全退出；不会修改或获取 ERSS DLL。继续？"
            : "将备份两个配置文件，关闭法环高倍率共存配置并把 ERSS 请求设回 2×；RTXMFG DLL 会保留。继续？";
        if (MessageBox.Show(this, message, "确认法环配置", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = await _eldenRing.SetUnlockAsync(_game, enable);
        ResultText.Text = result.Message + (result.Errors.Count > 0 ? " " + string.Join("；", result.Errors) : "");
        RefreshEldenRingStatus();
    }

    private async void ApplyEldenRingClick(object sender, RoutedEventArgs e) => await SetEldenRingUnlockAsync(true);
    private async void DisableEldenRingClick(object sender, RoutedEventArgs e) => await SetEldenRingUnlockAsync(false);

    private async void DownloadReshadeClick(object sender, RoutedEventArgs e)
    {
        var staged = Path.Combine(AppContext.BaseDirectory, "BridgeComponents");
        DownloadReshadeButton.IsEnabled = false;
        ResultText.Text = "正在从 reshade.me 获取 full add-on 版并校验；不会运行安装器。";
        try
        {
            var module = await _reshade.FetchModuleAsync(staged);
            _bridgeFolder = staged;
            BridgeFolderText.Text = staged;
            ResultText.Text = $"已取得官方 ReShade {OfficialReShadeService.Version}：{module}。现在可预览桥接安装。";
        }
        catch (Exception ex) { ResultText.Text = "ReShade 获取失败：" + ex.Message; }
        finally { DownloadReshadeButton.IsEnabled = true; }
    }

    private void ChooseBridgeClick(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        var dialog = new OpenFileDialog { Title = "选择组件文件夹中的 dlss5-bridge.addon64", Filter = "dlss5-bridge.addon64|dlss5-bridge.addon64", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        _bridgeFolder = Path.GetDirectoryName(dialog.FileName);
        BridgeFolderText.Text = _bridgeFolder;
        var plan = _bridge.Preview(_game, _bridgeFolder!);
        ResultText.Text = plan.CanProceed ? $"可安装 {plan.Files.Count} 个缺失组件；已有文件保持原样。" : string.Join("\n", plan.Warnings);
    }

    private async void InstallBridgeClick(object sender, RoutedEventArgs e)
    {
        if (_bridgeFolder is null) { ResultText.Text = "先选择组件文件夹。若游戏尚无 ReShade，该文件夹还须包含 ReShade64.dll。"; return; }
        if (_game is null) return;
        if (_advice.Game == KnownGame.Nightreign && OfflineConfirmation.IsChecked != true)
        { ResultText.Text = "黑夜君临仅支持确认无反作弊的离线测试，请先勾选确认。"; return; }
        var plan = _bridge.Preview(_game, _bridgeFolder);
        if (!plan.CanProceed) { ResultText.Text = string.Join("\n", plan.Warnings); return; }
        var names = string.Join("\n", plan.Files.Select(x => "• " + Path.GetFileName(x.Destination)));
        if (MessageBox.Show(this, $"将部署到：\n{_game.InstallPath}\n\n{names}\n\n确定继续？", "确认 ReShade 桥接安装", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var result = await _bridge.InstallAsync(_game, _bridgeFolder);
        ResultText.Text = result.Success ? result.Message : result.Message + " " + string.Join("；", result.Errors);
    }

    private async void UninstallBridgeClick(object sender, RoutedEventArgs e)
    {
        if (_game is null) return;
        if (!_bridge.IsManaged(_game)) { ResultText.Text = "没有本管理器安装的 ReShade 桥接组件。"; return; }
        if (MessageBox.Show(this, "只卸载管理器记录的桥接文件，继续？", "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var result = await _bridge.UninstallAsync(_game);
        ResultText.Text = result.Message;
    }

    private async void DownloadMagpieClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ResultText.Text = "正在从 SAOG0721/Magpie 下载预发布版，请稍候……";
            _magpieExe = await _magpie.InstallLatestExperimentalAsync();
            ResultText.Text = "已下载：" + _magpieExe;
        }
        catch (Exception ex) { ResultText.Text = "Magpie 下载失败：" + ex.Message; }
    }

    private void LaunchMagpieClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_magpieExe is null)
            {
                var dialog = new OpenFileDialog { Title = "选择已下载的 SAOG Magpie.exe", Filter = "Magpie.exe|Magpie.exe", CheckFileExists = true };
                if (dialog.ShowDialog(this) != true) return;
                _magpieExe = dialog.FileName;
            }
            MagpiePipelineService.Launch(_magpieExe);
            ResultText.Text = "Magpie 已启动。请在其界面中选择目标游戏或视频窗口。";
        }
        catch (Exception ex) { ResultText.Text = "启动失败：" + ex.Message; }
    }
}
