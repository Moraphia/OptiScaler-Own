using System.Windows;
using System.Windows.Controls;
using OptiScalerManager.Core;

namespace OptiScalerManager.App;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly GameEntry? _game;

    public SettingsWindow(GameEntry? game)
    {
        InitializeComponent();
        _game = game;
        Select(LanguageBox, SettingsStore.Current.Language);
        Select(ProxyBox, SettingsStore.Current.ProxyDll);
        Select(GraphicsApiBox, SettingsStore.Current.GraphicsApi);
        ReduceAnimationsBox.IsChecked = SettingsStore.Current.ReduceAnimations;
        OpenConfigButton.IsEnabled = game is not null;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.Language = LanguageBox.SelectedValue?.ToString() ?? "zh-CN";
        SettingsStore.Current.ProxyDll = Text(ProxyBox);
        SettingsStore.Current.GraphicsApi = Text(GraphicsApiBox);
        SettingsStore.Current.ReduceAnimations = ReduceAnimationsBox.IsChecked == true;
        SettingsStore.Save();
        LanguageService.SetLanguage(SettingsStore.Current.Language);
        DialogResult = true;
    }

    private void OpenConfigClick(object sender, RoutedEventArgs e)
    {
        if (_game is not null) new ConfigWindow(_game) { Owner = this }.ShowDialog();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => Close();
    private static string Text(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
    private static void Select(ComboBox box, string value) { box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase)); if (box.SelectedIndex < 0) box.SelectedIndex = 0; }
}
