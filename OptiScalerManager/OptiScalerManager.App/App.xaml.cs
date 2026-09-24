namespace OptiScalerManager.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        SettingsStore.Load();
        LanguageService.Initialize();
    }
}
