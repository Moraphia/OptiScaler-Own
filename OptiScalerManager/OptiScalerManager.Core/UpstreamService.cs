using System.Text.Json;

namespace OptiScalerManager.Core;

public sealed class UpstreamDefinition
{
    public string Id { get; init; } = "";
    public string Repository { get; init; } = "";
    public string Ref { get; init; } = "";
    public string UpdatePolicy { get; init; } = "";
    public string License { get; init; } = "";
    public string RedistributionPolicy { get; init; } = "";
    public bool Enabled { get; init; }
}

public sealed class UpstreamLockEntry
{
    public string Id { get; init; } = "";
    public string Repository { get; init; } = "";
    public string Ref { get; init; } = "";
    public string Policy { get; init; } = "";
    public string Version { get; init; } = "";
    public string Url { get; init; } = "";
    public DateTimeOffset? CheckedAt { get; init; }
}

public sealed record UpstreamStatus(
    string Id,
    string Repository,
    string Policy,
    string License,
    string RedistributionPolicy,
    string Version,
    string Status,
    DateTimeOffset? CheckedAt);

public sealed class UpstreamService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<UpstreamStatus>> LoadMatrixAsync(string manifestPath, string lockPath, CancellationToken cancellationToken = default)
    {
        var definitions = await ReadListAsync<UpstreamDefinition>(manifestPath, "upstreams", cancellationToken);
        var locks = await ReadListAsync<UpstreamLockEntry>(lockPath, "upstreams", cancellationToken);
        var lockMap = locks.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        return definitions.Where(x => x.Enabled).Select(definition =>
        {
            if (!lockMap.TryGetValue(definition.Id, out var locked))
                return new UpstreamStatus(definition.Id, definition.Repository, definition.UpdatePolicy, definition.License, definition.RedistributionPolicy, "—", "待 CI 检查", null);
            var unavailable = string.IsNullOrWhiteSpace(locked.Version) || locked.Version.Equals("unavailable", StringComparison.OrdinalIgnoreCase);
            return new UpstreamStatus(definition.Id, definition.Repository, definition.UpdatePolicy, definition.License, definition.RedistributionPolicy, unavailable ? "不可用" : ShortVersion(locked.Version), unavailable ? "查询失败" : "已锁定", locked.CheckedAt);
        }).ToList();
    }

    private static async Task<List<T>> ReadListAsync<T>(string path, string property, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return [];
        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty(property, out var array)) return [];
            return JsonSerializer.Deserialize<List<T>>(array.GetRawText(), JsonOptions) ?? [];
        }
        catch (JsonException) { return []; }
    }

    private static string ShortVersion(string version) => version.Length > 12 && version.All(Uri.IsHexDigit) ? version[..12] : version;
}
