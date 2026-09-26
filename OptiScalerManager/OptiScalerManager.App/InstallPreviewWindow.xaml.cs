using System.Collections.ObjectModel;
using System.Windows;
using OptiScalerManager.Core;

namespace OptiScalerManager.App;

public partial class InstallPreviewWindow : Wpf.Ui.Controls.FluentWindow
{
    public sealed record PreviewFile(string Action, string Destination, string Detail);

    public InstallPreviewWindow(GameEntry game, OperationPlan plan)
    {
        InitializeComponent();
        Subtitle.Text = $"准备为 {game.DisplayName} 写入 {plan.Files.Count} 个文件";
        Summary.Text = $"{plan.Files.Count} 个文件将被处理 · {plan.Files.Count(x => x.ExistingSha256 is not null)} 个文件会先备份";
        WarningText.Text = plan.Warnings.Count == 0 ? "已通过预检查。现有文件会保存到 RuntimeSync/backup，失败时会尝试自动回滚。" : string.Join("  ", plan.Warnings);
        FilesList.ItemsSource = new ObservableCollection<PreviewFile>(plan.Files.Select(x => new PreviewFile(
            x.ExistingSha256 is null ? "新增" : "备份并替换",
            x.Destination,
            x.ExistingSha256 is null ? "目标文件不存在，将写入新文件" : $"现有 SHA256: {x.ExistingSha256}")));
        ConfirmButton.IsEnabled = plan.CanProceed;
    }

    private void ConfirmClick(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void CancelClick(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
