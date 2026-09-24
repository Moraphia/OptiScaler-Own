using System.Text.Json;
using System.Diagnostics;

namespace OptiScalerManager.Core;

public interface IGameDiscoveryService
{
    Task<IReadOnlyList<GameCandidate>> ScanSteamAsync(CancellationToken cancellationToken = default);
    Task<GameInspection> InspectAsync(string path, CancellationToken cancellationToken = default);
}

public interface IConfigService
{
    Task<ConfigDocument> ReadAsync(GameEntry game, CancellationToken cancellationToken = default);
    Task<OperationResult> WriteAsync(GameEntry game, IReadOnlyList<ConfigChange> changes, string? expectedSha256 = null, CancellationToken cancellationToken = default);
    Task<ConfigSnapshot> CreateSnapshotAsync(GameEntry game, string description, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConfigSnapshot>> ListSnapshotsAsync(GameEntry game, CancellationToken cancellationToken = default);
    Task<OperationResult> RestoreSnapshotAsync(GameEntry game, ConfigSnapshot snapshot, CancellationToken cancellationToken = default);
}

public interface IInstallationService
{
    Task<OperationPlan> PreviewInstallAsync(GameEntry game, InstallOptions options, CancellationToken cancellationToken = default);
    Task<OperationResult> AdoptLegacyAsync(GameEntry game, CancellationToken cancellationToken = default);
    Task<OperationResult> InstallAsync(GameEntry game, InstallOptions options, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> VerifyAsync(GameEntry game, CancellationToken cancellationToken = default);
    Task<OperationResult> RepairAsync(GameEntry game, InstallOptions options, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<OperationResult> UninstallAsync(GameEntry game, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class ConfigService : IConfigService
{
    private readonly string _dataRoot;
    public ConfigService(string? dataRoot = null) => _dataRoot = dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptiScalerManager", "snapshots");
    public Task<ConfigDocument> ReadAsync(GameEntry game, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(game.InstallPath, "OptiScaler.ini");
        if (!File.Exists(path)) throw new FileNotFoundException("OptiScaler.ini was not found.", path);
        var text = File.ReadAllText(path); return Task.FromResult(new ConfigDocument { Path = path, Text = text, Sha256 = FileUtilities.Sha256(path) });
    }
    public Task<OperationResult> WriteAsync(GameEntry game, IReadOnlyList<ConfigChange> changes, string? expectedSha256 = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = Path.Combine(game.InstallPath, "OptiScaler.ini");
            if (!File.Exists(path)) return Task.FromResult(OperationResult.Fail("OptiScaler.ini was not found."));
            if (!string.IsNullOrWhiteSpace(expectedSha256) && !FileUtilities.Sha256(path).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(OperationResult.Fail("OptiScaler.ini was changed outside the Manager; save was blocked."));
            var ini = IniDocument.Load(path); foreach (var c in changes) ini.Set(c.Section, c.Key, c.Value); ini.SaveAtomic(path);
            return Task.FromResult(OperationResult.Ok("Configuration saved."));
        }
        catch (Exception ex) { return Task.FromResult(OperationResult.Fail("Configuration could not be saved.", ex.Message)); }
    }
    public async Task<ConfigSnapshot> CreateSnapshotAsync(GameEntry game, string description, CancellationToken cancellationToken = default)
    {
        var source = Path.Combine(game.InstallPath, "OptiScaler.ini"); if (!File.Exists(source)) throw new FileNotFoundException("OptiScaler.ini was not found.", source);
        var id = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff"); var dir = Path.Combine(_dataRoot, game.Id.Value); Directory.CreateDirectory(dir); var target = Path.Combine(dir, id + ".ini"); File.Copy(source, target, false);
        var snapshot = new ConfigSnapshot(id, game.Id.Value, DateTimeOffset.UtcNow, FileUtilities.Sha256(target), target, description); await FileUtilities.WriteJsonAtomicAsync(Path.ChangeExtension(target, ".json"), snapshot, cancellationToken); return snapshot;
    }
    public async Task<IReadOnlyList<ConfigSnapshot>> ListSnapshotsAsync(GameEntry game, CancellationToken cancellationToken = default)
    {
        var dir = Path.Combine(_dataRoot, game.Id.Value);
        if (!Directory.Exists(dir)) return [];
        var result = new List<ConfigSnapshot>();
        foreach (var json in Directory.EnumerateFiles(dir, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { var snapshot = JsonSerializer.Deserialize<ConfigSnapshot>(await File.ReadAllTextAsync(json, cancellationToken)); if (snapshot is not null) result.Add(snapshot); } catch (JsonException) { }
        }
        return result.OrderByDescending(x => x.CreatedAt).ToList();
    }
    public async Task<OperationResult> RestoreSnapshotAsync(GameEntry game, ConfigSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(snapshot.Path) || !FileUtilities.Sha256(snapshot.Path).Equals(snapshot.Sha256, StringComparison.OrdinalIgnoreCase))
                return OperationResult.Fail("Snapshot is missing or failed SHA256 verification.");
            var target = Path.Combine(game.InstallPath, "OptiScaler.ini");
            var temp = target + ".manager.tmp";
            await using (var input = File.OpenRead(snapshot.Path)) await using (var output = File.Create(temp)) await input.CopyToAsync(output, cancellationToken);
            File.Move(temp, target, true);
            return OperationResult.Ok("Snapshot restored.");
        }
        catch (Exception ex) { return OperationResult.Fail("Snapshot could not be restored.", ex.Message); }
    }
}

public sealed class InstallationService : IInstallationService
{
    private static readonly string[] Proxies = ["dxgi.dll", "winmm.dll", "version.dll", "d3d12.dll", "dbghelp.dll", "wininet.dll", "winhttp.dll"];
    private readonly IGameDiscoveryService _discovery;
    private readonly RuntimeSyncService _runtimeSync = new();
    public InstallationService(IGameDiscoveryService discovery) => _discovery = discovery;
    public Task<OperationPlan> PreviewInstallAsync(GameEntry game, InstallOptions options, CancellationToken cancellationToken = default)
    {
        var files = new List<FilePlanEntry>(); var warnings = new List<string>();
        if (!FileUtilities.IsGameDirectory(game.InstallPath)) warnings.Add("The selected folder does not contain a top-level game executable.");
        if (!Proxies.Contains(options.ProxyDll, StringComparer.OrdinalIgnoreCase)) warnings.Add("Unsupported proxy DLL.");
        if (!Directory.Exists(options.PackageDirectory)) warnings.Add("Package directory was not found.");
        var dll = Path.Combine(options.PackageDirectory, "OptiScaler.dll"); if (File.Exists(dll)) files.Add(new FilePlanEntry(dll, Path.Combine(game.InstallPath, options.ProxyDll), true, ExistingHash(Path.Combine(game.InstallPath, options.ProxyDll)))); else warnings.Add("OptiScaler.dll was not found in the package.");
        var ini = Path.Combine(options.PackageDirectory, "OptiScaler.ini"); if (File.Exists(ini)) files.Add(new FilePlanEntry(ini, Path.Combine(game.InstallPath, "OptiScaler.ini"), true, ExistingHash(Path.Combine(game.InstallPath, "OptiScaler.ini"))));
        foreach (var folder in new[] { "OptiScaler", "Licenses" })
        {
            var sourceRoot = Path.Combine(options.PackageDirectory, folder);
            if (!Directory.Exists(sourceRoot)) continue;
            foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(options.PackageDirectory, source);
                var destination = Path.GetFullPath(Path.Combine(game.InstallPath, relative));
                if (!destination.StartsWith(Path.GetFullPath(game.InstallPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { warnings.Add($"Unsafe package path skipped: {relative}"); continue; }
                files.Add(new FilePlanEntry(source, destination, true, ExistingHash(destination)));
            }
        }
        return Task.FromResult(new OperationPlan { Files = files, Warnings = warnings, CanProceed = files.Count > 0 && warnings.All(x => !x.Contains("not found", StringComparison.OrdinalIgnoreCase) && !x.Contains("Unsupported", StringComparison.OrdinalIgnoreCase)) });
    }
    public async Task<OperationResult> AdoptLegacyAsync(GameEntry game, CancellationToken cancellationToken = default)
    {
        try
        {
            var inspection = await _discovery.InspectAsync(game.InstallPath, cancellationToken);
            if (inspection.LegacyArtifacts.Count == 0) return OperationResult.Ok("No legacy BAT installation was detected.");
            var backupRoot = Path.Combine(game.InstallPath, "OptiScaler", "RuntimeSync", "legacy-backup", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(backupRoot);
            var files = new List<object>();
            foreach (var artifact in inspection.LegacyArtifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = Path.GetFullPath(Path.Combine(game.InstallPath, artifact));
                if (!File.Exists(source) || !IsInside(game.InstallPath, source)) continue;
                var backup = Path.Combine(backupRoot, Path.GetFileName(source));
                File.Copy(source, backup, false);
                files.Add(new { Destination = source, Backup = backup, Sha256 = FileUtilities.Sha256(source) });
            }
            if (files.Count == 0) return OperationResult.Fail("Legacy files disappeared before they could be backed up.");
            var adoption = new { SchemaVersion = 1, CreatedAt = DateTimeOffset.UtcNow, InstallDir = game.InstallPath, Files = files };
            await FileUtilities.WriteJsonAtomicAsync(Path.Combine(game.InstallPath, "OptiScaler", "RuntimeSync", "legacy-adoption.json"), adoption, cancellationToken);
            return OperationResult.Ok($"Legacy installation registered. {files.Count} file(s) were backed up; no legacy file was deleted.");
        }
        catch (Exception ex) { return OperationResult.Fail("Legacy installation could not be adopted.", ex.Message); }
    }
    public async Task<OperationResult> InstallAsync(GameEntry game, InstallOptions options, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var plan = await PreviewInstallAsync(game, options, cancellationToken); if (!plan.CanProceed) return OperationResult.Fail("Preflight failed.", plan.Warnings.ToArray());
        var backupRoot = Path.Combine(game.InstallPath, "OptiScaler", "RuntimeSync", "backup", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff")); var copied = new List<(string destination, string? backup)>(); var runtimeEntries = new List<RuntimeSyncEntry>();
        try
        {
            progress?.Report(new(OperationStage.Preflight, 10, "Preflight complete")); Directory.CreateDirectory(backupRoot);
            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested(); string? backup = null;
                if (File.Exists(file.Destination)) { backup = Path.Combine(backupRoot, Path.GetRelativePath(game.InstallPath, file.Destination)); Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(file.Destination, backup, true); }
                copied.Add((file.Destination, backup)); progress?.Report(new(OperationStage.Backup, 25, $"Backed up {Path.GetFileName(file.Destination)}"));
            }
            foreach (var file in plan.Files)
            {
                var temp = file.Destination + ".manager.tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);
                File.Copy(file.Source, temp, true);
                File.Move(temp, file.Destination, true);
                if (!FileUtilities.Sha256(file.Source).Equals(FileUtilities.Sha256(file.Destination), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Hash verification failed for {Path.GetFileName(file.Destination)}.");
                progress?.Report(new(OperationStage.Copy, 65, $"Installed {Path.GetFileName(file.Destination)}"));
            }
            if (options.SyncRuntimes)
            {
                var runtimeResult = await _runtimeSync.SyncAsync(options.PackageDirectory, game.InstallPath, backupRoot, progress, cancellationToken);
                runtimeEntries.AddRange(runtimeResult.Entries);
            }
            var packageEntries = plan.Files.Select(x => new { x.Destination, Backup = copied.FirstOrDefault(c => c.destination.Equals(x.Destination, StringComparison.OrdinalIgnoreCase)).backup, Sha256 = FileUtilities.Sha256(x.Destination), HadOriginal = x.ExistingSha256 is not null, Source = Path.GetFileName(x.Source), Version = GetFileVersion(x.Destination) }).Cast<object>();
            var allEntries = packageEntries.Concat(runtimeEntries.Select(x => new { x.Destination, x.Backup, Sha256 = x.DeployedHash, x.HadOriginal, Source = x.SourceName, Version = x.DeployedVersion }));
            await FileUtilities.WriteJsonAtomicAsync(Path.Combine(game.InstallPath, "OptiScaler", "RuntimeSync", "manifest.json"), new { SchemaVersion = 3, InstallDir = game.InstallPath, CreatedAt = DateTimeOffset.UtcNow, Proxy = options.ProxyDll, BackupRoot = backupRoot, Files = allEntries }, cancellationToken);
            progress?.Report(new(OperationStage.Verify, 90, "Files verified")); return OperationResult.Ok("OptiScaler installed successfully.");
        }
        catch (Exception ex)
        {
            progress?.Report(new(OperationStage.Rollback, 90, "Rolling back changes")); foreach (var item in runtimeEntries.Reverse<RuntimeSyncEntry>()) try { if (item.Backup is not null) File.Copy(item.Backup, item.Destination, true); else if (!item.HadOriginal && File.Exists(item.Destination)) File.Delete(item.Destination); } catch { } foreach (var item in copied) try { if (item.backup is not null) File.Copy(item.backup, item.destination, true); else if (File.Exists(item.destination)) File.Delete(item.destination); } catch { }
            return new OperationResult { Message = "Installation failed and was rolled back where possible.", Errors = [ex.Message], RolledBack = true };
        }
    }
    public Task<OperationResult> VerifyAsync(GameEntry game, CancellationToken cancellationToken = default) => VerifyCore(game);
    public Task<OperationResult> RepairAsync(GameEntry game, InstallOptions options, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => InstallAsync(game, options, progress, cancellationToken);
    public async Task<OperationResult> UninstallAsync(GameEntry game, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var manifestPath = Path.Combine(game.InstallPath, "OptiScaler", "RuntimeSync", "manifest.json");
            if (!File.Exists(manifestPath)) return OperationResult.Fail("No managed manifest was found; uninstall was blocked to protect the original files.");
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
            if (!doc.RootElement.TryGetProperty("Files", out var entries)) return OperationResult.Fail("The managed manifest is incomplete; uninstall was blocked.");
            var restore = new List<(string Destination, string? Backup, bool HadOriginal)>();
            foreach (var entry in entries.EnumerateArray())
            {
                var destination = entry.GetProperty("Destination").GetString(); if (string.IsNullOrWhiteSpace(destination)) continue;
                var backup = entry.TryGetProperty("Backup", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
                if (!IsInside(game.InstallPath, destination) || (!string.IsNullOrWhiteSpace(backup) && !IsInside(game.InstallPath, backup)))
                    return OperationResult.Fail("The managed manifest contains an unsafe path; uninstall was blocked.");
                if (File.Exists(destination) && entry.TryGetProperty("Sha256", out var expectedElement) && expectedElement.ValueKind == JsonValueKind.String)
                {
                    var expected = expectedElement.GetString();
                    if (!string.IsNullOrWhiteSpace(expected) && !FileUtilities.Sha256(destination).Equals(expected, StringComparison.OrdinalIgnoreCase))
                        return OperationResult.Fail($"Managed file was changed outside the Manager: {Path.GetFileName(destination)}. Uninstall was blocked to protect the change.");
                }
                var hadOriginal = entry.TryGetProperty("HadOriginal", out var hadOriginalElement) && hadOriginalElement.ValueKind == JsonValueKind.True;
                if (hadOriginal && (string.IsNullOrWhiteSpace(backup) || !File.Exists(backup)))
                {
                    return OperationResult.Fail($"Restore backup is missing for {Path.GetFileName(destination)}; uninstall was blocked.");
                }
                restore.Add((destination, backup, hadOriginal));
            }
            foreach (var item in restore)
            {
                if (item.Backup is not null) File.Copy(item.Backup, item.Destination, true);
                else if (File.Exists(item.Destination)) File.Delete(item.Destination);
            }
            progress?.Report(new(OperationStage.Complete, 100, "Original files restored"));
            // Preserve files not owned by this installation, including user configuration and legacy data.
            File.Delete(manifestPath);
            return OperationResult.Ok("OptiScaler was removed and original files were restored.");
        }
        catch (Exception ex) { return OperationResult.Fail("Uninstall failed.", ex.Message); }
    }
    private async Task<OperationResult> VerifyCore(GameEntry game)
    {
        try
        {
            var path = Path.Combine(game.InstallPath, "OptiScaler", "RuntimeSync", "manifest.json");
            if (!File.Exists(path)) return OperationResult.Fail("RuntimeSync manifest is missing.");
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            foreach (var f in doc.RootElement.GetProperty("Files").EnumerateArray())
            {
                var destination = f.GetProperty("Destination").GetString();
                if (string.IsNullOrWhiteSpace(destination) || !IsInside(game.InstallPath, destination)) return OperationResult.Fail("The managed manifest contains an unsafe path.");
                if (!File.Exists(destination)) return OperationResult.Fail("A managed file is missing.", destination);
                if (f.TryGetProperty("Sha256", out var expectedElement) && expectedElement.ValueKind == JsonValueKind.String)
                {
                    var expected = expectedElement.GetString();
                    if (!string.IsNullOrWhiteSpace(expected) && !FileUtilities.Sha256(destination).Equals(expected, StringComparison.OrdinalIgnoreCase))
                        return OperationResult.Fail($"A managed file failed SHA256 verification: {Path.GetFileName(destination)}.", destination);
                }
            }
            return OperationResult.Ok("Managed files passed SHA256 verification.");
        }
        catch (Exception ex) { return OperationResult.Fail("Verification failed.", ex.Message); }
    }
    private static bool IsInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
    private static string? GetFileVersion(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(path).FileVersion?.Trim(); } catch { return null; }
    }
    private static string? ExistingHash(string path) => File.Exists(path) ? FileUtilities.Sha256(path) : null;
}

public sealed class ProcessService
{
    public static void Launch(GameEntry game) { if (string.IsNullOrWhiteSpace(game.ExecutablePath) || !File.Exists(game.ExecutablePath)) throw new FileNotFoundException("Game executable was not found."); Process.Start(new ProcessStartInfo(game.ExecutablePath) { WorkingDirectory = game.InstallPath, UseShellExecute = true }); }
}
