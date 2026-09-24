using System.Windows;

namespace OptiScalerManager.App;

public static class LanguageService
{
    public static string CurrentCulture { get; private set; } = "zh-CN";

    public static void Initialize()
    {
        if (SettingsStore.Current.Language is "zh-CN" or "en-US") SetLanguage(SettingsStore.Current.Language);
    }

    public static void SetLanguage(string cultureName)
    {
        if (string.Equals(CurrentCulture, cultureName, StringComparison.OrdinalIgnoreCase)) return;
        var uri = new Uri($"/OptiScalerManager.App;component/Resources/Strings.{cultureName}.xaml", UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = uri };
        var resources = Application.Current.Resources;
        var previous = resources.MergedDictionaries.FirstOrDefault(x => x.Source?.OriginalString.Contains("Resources/Strings.", StringComparison.OrdinalIgnoreCase) == true);
        if (previous is not null) resources.MergedDictionaries.Remove(previous);
        resources.MergedDictionaries.Add(dictionary);
        CurrentCulture = cultureName;
        SettingsStore.Current.Language = cultureName;
        SettingsStore.Save();
    }
}
