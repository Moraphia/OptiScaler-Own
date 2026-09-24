using System.Text.Json;

namespace OptiScalerManager.Core;

public sealed record PublishedDll(string Path, string Version, string Source, string Signature, string Sha256);
public sealed record SourceSubmodule(string Path, string Repository, string Commit);
public sealed record DependencyInventory(string PackageVersion, string PackageSha256, IReadOnlyList<PublishedDll> Files);

public static class DependencyInventoryService
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static async Task<DependencyInventory> LoadAsync(string path)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var root = document.RootElement;
        var files = JsonSerializer.Deserialize<List<PublishedDll>>(root.GetProperty("files"), Options) ?? [];
        if (files.Count != root.GetProperty("dllCount").GetInt32() || files.Count == 0)
            throw new InvalidDataException("Dependency inventory file count is inconsistent.");
        if (files.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count ||
            files.Any(x => string.IsNullOrWhiteSpace(x.Path) || string.IsNullOrWhiteSpace(x.Version) ||
                string.IsNullOrWhiteSpace(x.Source) || x.Sha256.Length != 64 || !x.Sha256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Dependency inventory has invalid or duplicate DLL entries.");
        var packageHash = root.GetProperty("packageSha256").GetString() ?? "";
        if (packageHash.Length != 64 || !packageHash.All(Uri.IsHexDigit))
            throw new InvalidDataException("Dependency inventory package hash is invalid.");
        return new DependencyInventory(root.GetProperty("packageVersion").GetString() ?? "未知", packageHash, files);
    }

    public static async Task<IReadOnlyList<SourceSubmodule>> LoadSubmodulesAsync(string path)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var rows = JsonSerializer.Deserialize<List<SourceSubmodule>>(document.RootElement.GetProperty("submodules"), Options) ?? [];
        if (rows.Count == 0 || rows.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count ||
            rows.Any(x => string.IsNullOrWhiteSpace(x.Repository) || x.Commit.Length != 40 || !x.Commit.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Source submodule inventory is invalid.");
        return rows;
    }
}
