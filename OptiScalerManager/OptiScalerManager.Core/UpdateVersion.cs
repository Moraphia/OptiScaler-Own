namespace OptiScalerManager.Core;

public static class UpdateVersion
{
    public static string Compare(string? current, string? latest, bool available)
    {
        if (!available) return "未发布";
        if (!Version.TryParse(Normalize(current), out var installed)) return "本地版本未知";
        if (!Version.TryParse(Normalize(latest), out var released)) return "发布版本未知";
        var comparison = Expand(installed).CompareTo(Expand(released));
        return comparison < 0 ? "需要更新" : comparison == 0 ? "已是最新" : "本地版本较新";
    }

    private static string? Normalize(string? value) => value?.Trim().TrimStart('v', 'V');

    private static Version Expand(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
}
