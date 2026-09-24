using System.Net.Http.Headers;
using System.IO.Compression;
using System.Text.Json;

namespace OptiScalerManager.Core;

public sealed class ReleaseService : IReleaseService, IDisposable
{
    private readonly HttpClient _client = new();
    private readonly string _repo;
    private readonly bool _ownChannel;
    public ReleaseService(string? ownRepository = null)
    {
        _ownChannel = !string.IsNullOrWhiteSpace(ownRepository);
        _repo = _ownChannel ? ownRepository! : "optiscaler/OptiScaler";
        if (!System.Text.RegularExpressions.Regex.IsMatch(_repo, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) throw new ArgumentException("Repository must be owner/name.", nameof(ownRepository));
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OptiScalerManager", "0.1"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseInfo?> CheckStableAsync(CancellationToken cancellationToken = default)
        => (await CheckStableCatalogAsync(cancellationToken))?.Game;

    public async Task<ReleaseCatalog?> CheckStableCatalogAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync($"https://api.github.com/repos/{_repo}/releases/latest", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return ParseCatalog(doc.RootElement, _ownChannel);
    }

    public static ReleaseCatalog ParseCatalog(JsonElement root, bool ownChannel)
    {
        var assets = root.TryGetProperty("assets", out var a) ? a.EnumerateArray().ToArray() : [];
        var tag = root.GetProperty("tag_name").GetString() ?? "unknown";
        var name = root.GetProperty("name").GetString() ?? "OptiScaler Release";
        var body = root.GetProperty("body").GetString() ?? "";
        var published = root.GetProperty("published_at").GetDateTimeOffset();
        ReleaseInfo? FindPackage(string assetName, string packageType)
        {
            var asset = assets.FirstOrDefault(x => string.Equals(x.GetProperty("name").GetString(), assetName, StringComparison.OrdinalIgnoreCase));
            var manifest = assets.FirstOrDefault(x => string.Equals(x.GetProperty("name").GetString(), assetName + ".manifest.json", StringComparison.OrdinalIgnoreCase));
            if (!ownChannel || asset.ValueKind != JsonValueKind.Object || manifest.ValueKind != JsonValueKind.Object) return null;
            return new ReleaseInfo { TagName = tag, Name = name, Body = body,
                DownloadUrl = asset.GetProperty("browser_download_url").GetString(), AssetName = assetName,
                ManifestDownloadUrl = manifest.GetProperty("browser_download_url").GetString(),
                IsPrerelease = false, PublishedAt = published, PackageType = packageType };
        }
        return new ReleaseCatalog(tag, name, body, published,
            FindPackage("OptiScalerManager-win-x64.zip", "manager"),
            FindPackage("OptiScaler-Package-win-x64.zip", "game"));
    }

    public Task<PackageInspection> InspectPackageAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        var warnings = new List<string>();
        var entries = new List<string>();
        var supported = packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var hasManager = false;
        var hasManagerCore = false;
        var hasDll = false;
        var hasIni = false;
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
                    if (entry.FullName.Equals("OptiScalerManager.exe", StringComparison.OrdinalIgnoreCase)) hasManager = true;
                    if (entry.FullName.Equals("OptiScalerManager.Core.dll", StringComparison.OrdinalIgnoreCase)) hasManagerCore = true;
                    if (entry.FullName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase)) hasDll = true;
                    if (entry.FullName.Equals("OptiScaler.ini", StringComparison.OrdinalIgnoreCase)) hasIni = true;
                    if (fileName.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase)) hasManifest = true;
                }
                if (!hasDll) warnings.Add("The package does not contain OptiScaler.dll.");
                if (!hasIni) warnings.Add("The package does not contain OptiScaler.ini.");
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
            HasManagerCore = hasManagerCore,
            HasOptiScalerDll = hasDll,
            HasOptiScalerIni = hasIni,
            HasManifest = hasManifest
        });
    }

    public static async Task<string> ExtractPackageAsync(string packagePath, string destination, string packageType = "game", CancellationToken cancellationToken = default)
    {
        using var service = new ReleaseService();
        var inspection = await service.InspectPackageAsync(packagePath, cancellationToken);
        var expectedFilesPresent = packageType switch
        {
            "manager" => inspection.HasManager && inspection.HasManagerCore,
            "game" => inspection.HasOptiScalerDll && inspection.HasOptiScalerIni,
            _ => throw new ArgumentException("Unknown package type.", nameof(packageType))
        };
        if (!inspection.IsSafe || !inspection.IsSupportedArchive || !expectedFilesPresent)
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
