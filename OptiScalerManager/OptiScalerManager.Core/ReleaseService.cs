using System.Net.Http.Headers;
using System.IO.Compression;
using System.Text.Json;

namespace OptiScalerManager.Core;

public sealed class ReleaseService : IReleaseService, IDisposable
{
    private readonly HttpClient _client = new();
    private const string Repo = "optiscaler/OptiScaler";
    public ReleaseService()
    {
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OptiScalerManager", "0.1"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseInfo?> CheckStableAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = doc.RootElement;
        var assets = root.TryGetProperty("assets", out var a) ? a.EnumerateArray() : [];
        var asset = assets.FirstOrDefault(x => x.GetProperty("name").GetString()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
        var manifest = assets.FirstOrDefault(x => x.GetProperty("name").GetString()?.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase) == true);
        return new ReleaseInfo
        {
            TagName = root.GetProperty("tag_name").GetString() ?? "unknown",
            Name = root.GetProperty("name").GetString() ?? "OptiScaler Release",
            Body = root.GetProperty("body").GetString() ?? "",
            // Official releases do not contain this branch's experimental modules.
            // Keep the update view read-only until an own-release feed is configured.
            DownloadUrl = null,
            AssetName = null,
            ManifestDownloadUrl = null,
            IsPrerelease = root.GetProperty("prerelease").GetBoolean(),
            PublishedAt = root.GetProperty("published_at").GetDateTimeOffset()
        };
    }

    public Task<PackageInspection> InspectPackageAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        var warnings = new List<string>();
        var entries = new List<string>();
        var supported = packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var hasManager = false;
        var hasDll = false;
        var hasManifest = false;

        if (!File.Exists(packagePath)) errors.Add("Package file was not found.");
        else if (!supported) errors.Add("Only ZIP release packages can be inspected on this system.");
        else
        {
            try
            {
                using var archive = ZipFile.OpenRead(packagePath);
                var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(packagePath)!, Path.GetFileNameWithoutExtension(packagePath)));
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var normalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    var target = Path.GetFullPath(Path.Combine(root, normalized));
                    if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !target.Equals(root, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"Unsafe archive path: {entry.FullName}");
                        continue;
                    }
                    entries.Add(entry.FullName);
                    var fileName = Path.GetFileName(entry.FullName);
                    if (fileName.Equals("OptiScalerManager.exe", StringComparison.OrdinalIgnoreCase)) hasManager = true;
                    if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase)) hasDll = true;
                    if (fileName.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase)) hasManifest = true;
                }
                if (!hasDll) warnings.Add("The package does not contain OptiScaler.dll.");
                if (!hasManager) warnings.Add("The package does not contain the optional Manager executable.");
            }
            catch (Exception ex) { errors.Add($"ZIP inspection failed: {ex.Message}"); }
        }

        return Task.FromResult(new PackageInspection
        {
            PackagePath = packagePath,
            IsSupportedArchive = supported,
            IsSafe = errors.Count == 0,
            Sha256 = File.Exists(packagePath) ? FileUtilities.Sha256(packagePath) : "",
            SizeBytes = File.Exists(packagePath) ? new FileInfo(packagePath).Length : 0,
            Entries = entries,
            Warnings = warnings,
            Errors = errors,
            HasManager = hasManager,
            HasOptiScalerDll = hasDll,
            HasManifest = hasManifest
        });
    }

    public static async Task<string> ExtractPackageAsync(string packagePath, string destination, CancellationToken cancellationToken = default)
    {
        using var service = new ReleaseService();
        var inspection = await service.InspectPackageAsync(packagePath, cancellationToken);
        if (!inspection.IsSafe || !inspection.IsSupportedArchive || !inspection.HasOptiScalerDll)
            throw new InvalidDataException(string.Join(" ", inspection.Errors.Concat(inspection.Warnings)));
        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(packagePath);
        var root = Path.GetFullPath(destination);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Unsafe archive path: {entry.FullName}");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = File.Create(target);
            await input.CopyToAsync(output, cancellationToken);
        }
        return destination;
    }

    public async Task<string?> DownloadAsync(ReleaseInfo release, string destination, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(release.DownloadUrl)) return null;
        using var response = await _client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode(); var total = response.Content.Headers.ContentLength; await using var input = await response.Content.ReadAsStreamAsync(cancellationToken); await using var output = File.Create(destination); var buffer = new byte[1024 * 128]; long read = 0; int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0) { await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken); read += count; progress?.Report(new(OperationStage.Copy, total is > 0 ? (int)(read * 100 / total.Value) : 0, "Downloading release")); }
        return destination;
    }

    public async Task<string> DownloadTextAsync(string url, CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public void Dispose() => _client.Dispose();
}

public interface IReleaseService
{
    Task<ReleaseInfo?> CheckStableAsync(CancellationToken cancellationToken = default);
    Task<string?> DownloadAsync(ReleaseInfo release, string destination, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<PackageInspection> InspectPackageAsync(string packagePath, CancellationToken cancellationToken = default);
}
