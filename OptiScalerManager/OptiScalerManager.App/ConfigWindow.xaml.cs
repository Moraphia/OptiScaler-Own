using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using OptiScalerManager.Core;

namespace OptiScalerManager.App;

public partial class ConfigWindow : Window
{
    private readonly GameEntry _game;
    private readonly ConfigService _config = new();
    private readonly ObservableCollection<IniEntry> _entries = [];
    private readonly ObservableCollection<ConfigSnapshot> _snapshots = [];
    private readonly Dictionary<string, string> _loadedBasic = new(StringComparer.OrdinalIgnoreCase);
    private IniDocument? _document;
    private string? _loadedHash;

    public ConfigWindow(GameEntry game, bool openExpert = false)
    {
        InitializeComponent();
        _game = game;
        GamePath.Text = game.InstallPath;
        EntriesGrid.ItemsSource = _entries;
        var expertView = CollectionViewSource.GetDefaultView(_entries);
        expertView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(IniEntry.Category)));
        expertView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(IniEntry.Category), System.ComponentModel.ListSortDirection.Ascending));
        expertView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(IniEntry.Section), System.ComponentModel.ListSortDirection.Ascending));
        SnapshotBox.ItemsSource = _snapshots;
        ConfigTabs.SelectedIndex = openExpert ? 1 : 0;
        LoadConfig();
        Loaded += async (_, _) => await LoadSnapshotsAsync();
    }

    private void LoadConfig()
    {
        try
        {
            var path = Path.Combine(_game.InstallPath, "OptiScaler.ini");
            _document = IniDocument.Load(path);
            _loadedHash = FileUtilities.Sha256(path);
            _entries.Clear();
            foreach (var entry in _document.GetEntries())
            {
                entry.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(IniEntry.Value) && EntriesGrid.SelectedItem == entry) ShowExpertDetails(entry); };
                _entries.Add(entry);
            }
            _loadedBasic.Clear();
            Set(UpscalerBox, "Upscalers", "Dx12Upscaler");
            Set(FgEnabledBox, "FrameGen", "Enabled");
            Set(FgInputBox, "FrameGen", "FGInput");
            Set(FgOutputBox, "FrameGen", "FGOutput");
            Set(ReplacementBox, "FrameGen", "FGNvngxReplacement");
            Set(MfgBox, "DLSSG", "InterpolationCount");
            Set(AdaUnlockBox, "DLSSG", "AdaMfgUnlock");
            Set(BlackwellKernelsBox, "DLSSG", "AdaBlackwellKernels");
            Set(NrBox, "DlssNr", "Enabled");
            Set(NrDualBox, "DlssNr", "DualFeature");
            Set(NrEnlargerBox, "DlssNr", "DualEnlarger");
            var log = _document.Get("Log", "LogToFile") ?? "auto";
            LogBox.IsThreeState = true;
            LogBox.IsChecked = log.Equals("true", StringComparison.OrdinalIgnoreCase) ? true : log.Equals("false", StringComparison.OrdinalIgnoreCase) ? false : null;
            StatusText.Text = $"已加载 {_entries.Count} 个配置项";
            CapabilityText.Text = string.Join("\n", CapabilityService.Inspect(_game.InstallPath, _document).Select(x => $"{x.Feature}：{x.Detail}"));
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void Set(ComboBox box, string section, string key)
    {
        var value = _document?.Get(section, key) ?? "auto";
        _loadedBasic[$"{section}\0{key}"] = value;
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals((x.Tag ?? x.Content)?.ToString(), value, StringComparison.OrdinalIgnoreCase));
        if (box.SelectedIndex < 0) { var item = new ComboBoxItem { Content = box == MfgBox && value == "6" ? "7× 请求（实验值 6）" : value, Tag = value }; box.Items.Add(item); box.SelectedItem = item; }
    }

    private void AddChanged(List<ConfigChange> changes, string section, string key, string value)
    {
        if (!_loadedBasic.TryGetValue($"{section}\0{key}", out var original) || !string.Equals(original, value, StringComparison.OrdinalIgnoreCase))
            changes.Add(new(section, key, value));
    }

    private void ExpertSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (EntriesGrid?.ItemsSource is null) return;
        var query = ExpertSearchBox.Text.Trim();
        var view = CollectionViewSource.GetDefaultView(_entries);
        view.Filter = item => item is IniEntry entry &&
            (query.Length == 0 || entry.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             entry.Section.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             entry.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             entry.Value.Contains(query, StringComparison.OrdinalIgnoreCase));
        view.Refresh();
    }

    private void ExpertSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExpertDetailText is null) return;
        if (EntriesGrid.SelectedItem is IniEntry entry) ShowExpertDetails(entry);
        else ExpertDetailText.Text = "选择一个配置项查看完整说明。";
    }

    private void ShowExpertDetails(IniEntry entry) => ExpertDetailText.Text = ConfigMetadata.DetailedExplanation(entry.Section, entry.Key, entry.Value);

    private void ExpertPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not IniEntry entry) return;
        var menu = new ContextMenu { Style = (Style)FindResource("ExpertPresetMenu") };
        foreach (var option in entry.Options)
        {
            var item = new MenuItem { Header = option == "auto" ? "auto（默认）" : option, IsChecked = string.Equals(option, entry.Value, StringComparison.OrdinalIgnoreCase), Style = (Style)FindResource("ExpertPresetMenuItem") };
            item.Click += (_, _) => entry.Value = option;
            menu.Items.Add(item);
        }
        button.ContextMenu = menu;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        var path = Path.Combine(_game.InstallPath, "OptiScaler.ini");
        if (_loadedHash is not null && File.Exists(path) && !FileUtilities.Sha256(path).Equals(_loadedHash, StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "配置文件已被其他程序修改。请关闭窗口后重新打开，再保存。";
            return;
        }
        EntriesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        EntriesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var changes = _entries.Where(x => !string.Equals(_document.Get(x.Section, x.Key), x.Value, StringComparison.Ordinal))
            .Select(x => new ConfigChange(x.Section, x.Key, x.Value)).ToList();
        AddChanged(changes, "Upscalers", "Dx12Upscaler", Text(UpscalerBox));
        AddChanged(changes, "FrameGen", "Enabled", Text(FgEnabledBox));
        AddChanged(changes, "FrameGen", "FGInput", Text(FgInputBox));
        AddChanged(changes, "FrameGen", "FGOutput", Text(FgOutputBox));
        AddChanged(changes, "FrameGen", "FGNvngxReplacement", Text(ReplacementBox));
        AddChanged(changes, "DLSSG", "InterpolationCount", Text(MfgBox));
        AddChanged(changes, "DLSSG", "AdaMfgUnlock", Text(AdaUnlockBox));
        AddChanged(changes, "DLSSG", "AdaBlackwellKernels", Text(BlackwellKernelsBox));
        AddChanged(changes, "DlssNr", "Enabled", Text(NrBox));
        AddChanged(changes, "DlssNr", "DualFeature", Text(NrDualBox));
        AddChanged(changes, "DlssNr", "DualEnlarger", Text(NrEnlargerBox));
        var logValue = LogBox.IsChecked is null ? "auto" : LogBox.IsChecked == true ? "true" : "false";
        if (!string.Equals(logValue, _document.Get("Log", "LogToFile") ?? "auto", StringComparison.OrdinalIgnoreCase)) changes.Add(new("Log", "LogToFile", logValue));
        if (changes.Count == 0) { StatusText.Text = "没有需要保存的修改。"; return; }
        var preview = string.Join("\n", changes.Take(16).Select(c => $"[{c.Section}] {c.Key}: {_document.Get(c.Section, c.Key) ?? "(未设置)"} → {c.Value}"));
        if (changes.Count > 16) preview += $"\n…另有 {changes.Count - 16} 项";
        if (MessageBox.Show(this, $"即将修改 {changes.Count} 项，保存前会自动创建快照：\n\n{preview}", "确认配置修改", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var result = await _config.WriteAsync(_game, changes, _loadedHash);
        StatusText.Text = result.Message;
        if (result.Success) DialogResult = true;
    }

    private async void SnapshotClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var snapshot = await _config.CreateSnapshotAsync(_game, $"Manager snapshot {DateTime.Now:g}");
            await LoadSnapshotsAsync();
            SnapshotBox.SelectedItem = _snapshots.FirstOrDefault(x => x.Id == snapshot.Id);
            StatusText.Text = $"快照已创建：{snapshot.Id}";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void RestoreSnapshotClick(object sender, RoutedEventArgs e)
    {
        if (SnapshotBox.SelectedItem is not ConfigSnapshot snapshot) { StatusText.Text = "请先选择一个快照。"; return; }
        if (MessageBox.Show(this, $"确认恢复快照 {snapshot.Id}？", "恢复快照", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = await _config.RestoreSnapshotAsync(_game, snapshot);
        StatusText.Text = result.Message;
        if (result.Success) { LoadConfig(); await LoadSnapshotsAsync(); }
    }

    private async Task LoadSnapshotsAsync()
    {
        _snapshots.Clear();
        foreach (var snapshot in await _config.ListSnapshotsAsync(_game)) _snapshots.Add(snapshot);
        SnapshotBox.SelectedIndex = -1;
    }

    private static string Text(ComboBox box) => ((box.SelectedItem as ComboBoxItem)?.Tag ?? (box.SelectedItem as ComboBoxItem)?.Content)?.ToString() ?? box.Text;
}
