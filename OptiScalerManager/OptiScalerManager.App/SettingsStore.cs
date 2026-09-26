using System.Text.Json;
using System.IO;

namespace OptiScalerManager.App;

public sealed class UserSettings
{
    public string Language { get; set; } = "zh-CN";
    public string ProxyDll { get; set; } = "dxgi.dll";
    public string GraphicsApi { get; set; } = "Auto";
    public bool ReduceAnimations { get; set; }
}

public static class SettingsStore
{
    public static UserSettings Current { get; private set; } = new();
    private static string PathName => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiScalerManager", "settings.json");

    public static void Load()
    {
        try
        {
            if (!File.Exists(PathName)) return;
            Current = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(PathName), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new UserSettings();
            Current.Language = Current.Language switch
            {
                "en-US" or "English" => "en-US",
                _ => "zh-CN"
            };
        }
        catch { Current = new UserSettings(); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
            File.WriteAllText(PathName, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
