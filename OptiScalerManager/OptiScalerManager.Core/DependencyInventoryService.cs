using System.Text.Json;
using System.Diagnostics;

namespace OptiScalerManager.Core;

public sealed record PublishedDll(string Path, string Version, string Source, string Signature, string Sha256);
public sealed record SourceSubmodule(string Path, string Repository, string Commit);
public sealed record DependencyInventory(string PackageVersion, string PackageSha256, IReadOnlyList<PublishedDll> Files);
public sealed record PipelineDependency(string Name, string Version, string Source, string Distribution, string Use, string Note);
public sealed record PublishedDllComparison(string Path, string Version, string Source, string Signature, string Sha256,
    string LocalVersion, string LocalStatus);

public static class DependencyInventoryService
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static Task<IReadOnlyList<PublishedDllComparison>> CompareLocalPackageAsync(DependencyInventory inventory, string packageDirectory)
        => Task.Run<IReadOnlyList<PublishedDllComparison>>(() => inventory.Files.Select(file =>
        {
            var path = Path.GetFullPath(Path.Combine(packageDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(packageDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return new PublishedDllComparison(file.Path, file.Version, file.Source, file.Signature, file.Sha256, "—", "路径异常");
            if (!Directory.Exists(packageDirectory))
                return new PublishedDllComparison(file.Path, file.Version, file.Source, file.Signature, file.Sha256, "—", "未提供 Package");
            if (!File.Exists(path))
                return new PublishedDllComparison(file.Path, file.Version, file.Source, file.Signature, file.Sha256, "—", "缺失");
            try
            {
                var version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "未标版本";
                var status = FileUtilities.Sha256(path).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) ? "一致" : "与快照不同";
                return new PublishedDllComparison(file.Path, file.Version, file.Source, file.Signature, file.Sha256, version, status);
            }
            catch (Exception) { return new PublishedDllComparison(file.Path, file.Version, file.Source, file.Signature, file.Sha256, "—", "读取失败"); }
        }).ToList());

    public static async Task<IReadOnlyList<PipelineDependency>> LoadPipelineAsync(string path)
    {
        var rows = JsonSerializer.Deserialize<List<PipelineDependency>>(await File.ReadAllTextAsync(path), Options) ?? [];
        if (rows.Count == 0 || rows.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count ||
            rows.Any(x => string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Version) || string.IsNullOrWhiteSpace(x.Source)))
            throw new InvalidDataException("渲染管线依赖矩阵不完整。");
        return rows;
    }

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
