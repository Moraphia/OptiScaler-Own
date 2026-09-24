using System.Diagnostics;

namespace OptiScalerManager.Core;

public sealed record RuntimeSyncEntry(
    string Destination,
    string? Backup,
    string SourceName,
    string? OriginalVersion,
    string? DeployedVersion,
    string OriginalHash,
    string DeployedHash,
    bool HadOriginal);

public sealed class RuntimeSyncResult
{
    public IReadOnlyList<RuntimeSyncEntry> Entries { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public int ChangedFiles { get; init; }
}

/// <summary>Bounded, package-driven port of dist/runtime_sync/runtime_sync.ps1.</summary>
public sealed class RuntimeSyncService
{
    private static readonly string[] BundledDlss = ["nvngx_dlss.dll", "nvngx_dlssd.dll", "nvngx_dlssg.dll"];
    private static readonly string[] KnownRuntimeDirectories =
    [
        @"Engine\Plugins\Runtime\Nvidia\DLSS\Binaries\ThirdParty\Win64",
        @"Engine\Plugins\Runtime\Nvidia\StreamlineCore\Binaries\ThirdParty\Win64",
        @"Engine\Plugins\Runtime\Nvidia\Streamline\Binaries\ThirdParty\Win64"
    ];
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "content", "paks", "saved", "logs", "movies", "sounds", "music", "videos", "localization",
        "shadercache", "derivedcache", "cache", "textures", "maps", "levels", "audio", "data", "assets",
        "mods", "screenshots", "redist", "_commonredist", "crashreport", "crashreports"
    };

    public Task<RuntimeSyncResult> SyncAsync(string packageDirectory, string installDirectory, string backupRoot, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var packageRuntime = Path.Combine(packageDirectory, "OptiScaler");
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in BundledDlss)
        {
            var path = Path.Combine(packageRuntime, name);
            if (File.Exists(path)) sources[name] = path;
        }
        var streamline = Path.Combine(packageRuntime, "streamline");
        if (Directory.Exists(streamline))
            foreach (var path in Directory.EnumerateFiles(streamline, "sl.*.dll", SearchOption.TopDirectoryOnly)) sources[Path.GetFileName(path)] = path;

        var warnings = new List<string>();
        if (sources.Count == 0)
        {
            warnings.Add("No bundled DLSS or Streamline runtime files were found; runtime synchronization was skipped.");
            return Task.FromResult(new RuntimeSyncResult { Warnings = warnings });
        }

        var targets = FindTargets(installDirectory, sources.Keys, packageRuntime, cancellationToken);
        var entries = new List<RuntimeSyncEntry>();
        var changed = 0;
        try
        {
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(target);
                var source = sources[name];
                if (name.StartsWith("sl.", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    var major = GetMajorVersion(target);
                    if (major is 1 or 0)
                    {
                        warnings.Add(major == 1 ? $"Protected Streamline 1.x runtime: {name}." : $"Protected Streamline runtime with unknown generation: {name}.");
                        continue;
                    }
                }

                var originalHash = FileUtilities.Sha256(target);
                var deployedHash = FileUtilities.Sha256(source);
                var originalVersion = GetFileVersion(target);
                var deployedVersion = GetFileVersion(source);
                string? backup = null;
                var hadOriginal = true;
                if (!originalHash.Equals(deployedHash, StringComparison.OrdinalIgnoreCase))
                {
                    var backupDirectory = Path.Combine(backupRoot, "runtime", FileUtilities.SafeGameId(target));
                    Directory.CreateDirectory(backupDirectory);
                    backup = Path.Combine(backupDirectory, name);
                    File.Copy(target, backup, false);
                    if (!FileUtilities.Sha256(backup).Equals(originalHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Runtime backup verification failed: {name}.");
                    var temp = target + ".manager.tmp";
                    try
                    {
                        File.Copy(source, temp, true);
                        File.Move(temp, target, true);
                        if (!FileUtilities.Sha256(target).Equals(deployedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Runtime hash verification failed: {name}.");
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                    changed++;
                    progress?.Report(new(OperationStage.Copy, 75, $"Synced runtime {name}"));
                }
                entries.Add(new RuntimeSyncEntry(target, backup, name, originalVersion, deployedVersion, originalHash, deployedHash, hadOriginal));
            }
        }
        catch
        {
            foreach (var entry in entries.Where(x => x.Backup is not null).Reverse())
                try { File.Copy(entry.Backup!, entry.Destination, true); } catch { }
            throw;
        }
        return Task.FromResult(new RuntimeSyncResult { Entries = entries, Warnings = warnings, ChangedFiles = changed });
    }

    private static IReadOnlyList<string> FindTargets(string installDirectory, IEnumerable<string> sourceNames, string packageRuntime, CancellationToken cancellationToken)
    {
        var wanted = new HashSet<string>(sourceNames, StringComparer.OrdinalIgnoreCase);
        var packageRoot = Path.GetFullPath(packageRuntime).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanRoot = ResolveScanRoot(installDirectory);
        void Add(string path)
        {
            if (!File.Exists(path)) return;
            var full = Path.GetFullPath(path);
            if (full.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase)) return;
            if (wanted.Contains(Path.GetFileName(full))) found.Add(full);
        }
        foreach (var name in wanted) Add(Path.Combine(installDirectory, name));
        foreach (var relative in KnownRuntimeDirectories)
        {
            var directory = Path.Combine(scanRoot, relative);
            if (!Directory.Exists(directory)) continue;
            foreach (var name in wanted) Add(Path.Combine(directory, name));
        }

        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((scanRoot, 0));
        var seen = 0;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (queue.Count > 0 && seen < 7000 && DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = queue.Dequeue(); seen++;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory); } catch { continue; }
            foreach (var file in files) if (wanted.Contains(Path.GetFileName(file))) Add(file);
            if (depth >= 9) continue;
            IEnumerable<string> directories;
            try { directories = Directory.EnumerateDirectories(directory); } catch { continue; }
            foreach (var child in directories)
            {
                var name = Path.GetFileName(child);
                if (name.Equals("OptiScaler", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".", StringComparison.Ordinal) || name.StartsWith("_storage", StringComparison.OrdinalIgnoreCase) || SkippedDirectories.Contains(name)) continue;
                queue.Enqueue((child, depth + 1));
            }
        }
        return found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ResolveScanRoot(string installDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(installDirectory));
        for (var i = 0; i < 6 && current is not null; i++)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Engine"))) return current.FullName;
            if (current.Parent is null || current.Parent.Name.Equals("common", StringComparison.OrdinalIgnoreCase)) break;
            current = current.Parent;
        }
        return Path.GetFullPath(installDirectory);
    }

    private static string? GetFileVersion(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(path).FileVersion?.Trim(); } catch { return null; }
    }
    private static int GetMajorVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (info.FileMajorPart is 1 or 2) return info.FileMajorPart;
            if (info.ProductMajorPart is 1 or 2) return info.ProductMajorPart;
        }
        catch { }
        return 0;
    }
}
