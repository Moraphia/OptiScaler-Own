using System.Collections.ObjectModel;

namespace OptiScalerManager.Core;

public enum GraphicsApi { Unknown, DirectX11, DirectX12, Vulkan }
public enum InstallState { NotDetected, Installed, Partial, Legacy, NeedsRepair, Blocked }
public enum OperationStage { Preflight, Backup, Copy, Verify, Commit, Rollback, Complete }

public sealed record GameId(string Value)
{
    public override string ToString() => Value;
}

public sealed class GameEntry
{
    public required GameId Id { get; init; }
    public required string DisplayName { get; init; }
    public required string InstallPath { get; set; }
    public string? ExecutablePath { get; set; }
    public string Launcher { get; set; } = "Manual";
    public GraphicsApi GraphicsApi { get; set; } = GraphicsApi.Unknown;
    public DateTimeOffset? LastInspected { get; set; }
}

public sealed record GameCandidate(string DisplayName, string InstallPath, string? ExecutablePath, string Launcher);

public sealed class GameInspection
{
    public required GameEntry Game { get; init; }
    public InstallState InstallState { get; init; }
    public string? ProxyDll { get; init; }
    public string? OptiScalerVersion { get; init; }
    public string? RuntimeSyncStatus { get; init; }
    public bool HasBackup { get; init; }
    public bool ConfigExists { get; init; }
    public IReadOnlyList<RuntimeFileRecord> RuntimeFiles { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> LegacyArtifacts { get; init; } = [];
}

public sealed record RuntimeFileRecord(string Path, string FileName, string? Version, string Sha256, bool IsManaged, bool IsBackup, bool IntegrityVerified = true)
{
    public string HashShort => string.IsNullOrWhiteSpace(Sha256) ? "—" : Sha256[..Math.Min(12, Sha256.Length)];
    public string IntegrityText => IntegrityVerified ? "OK" : "CHECK";
}

public sealed record OperationProgress(OperationStage Stage, int Percent, string Message);

public sealed class OperationResult
{
    public bool Success { get; init; }
    public bool RolledBack { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public static OperationResult Ok(string message) => new() { Success = true, Message = message };
    public static OperationResult Fail(string message, params string[] errors) => new() { Message = message, Errors = errors };
}

public sealed record InstallOptions(string PackageDirectory, string ProxyDll, bool SyncRuntimes = true,
    IReadOnlyList<ConfigChange>? IniOverrides = null);

public sealed record FilePlanEntry(string Source, string Destination, bool WillBackup, string? ExistingSha256);

public sealed class OperationPlan
{
    public IReadOnlyList<FilePlanEntry> Files { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool CanProceed { get; init; }
}

public sealed class ConfigDocument
{
    public required string Path { get; init; }
    public required string Text { get; init; }
    public string Sha256 { get; init; } = "";
    public bool ChangedExternally { get; init; }
}

public sealed record ConfigChange(string Section, string Key, string? Value);
public sealed record ConfigSnapshot(string Id, string GameId, DateTimeOffset CreatedAt, string Sha256, string Path, string Description);

public sealed class ReleaseInfo
{
    public required string TagName { get; init; }
    public required string Name { get; init; }
    public string Body { get; init; } = "";
    public string? DownloadUrl { get; init; }
    public string? AssetName { get; init; }
    public string? ManifestDownloadUrl { get; init; }
    public string? Sha256 { get; init; }
    public bool IsPrerelease { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public string PackageType { get; init; } = "game";
}

public sealed record ReleaseCatalog(string TagName, string Name, string Body, DateTimeOffset PublishedAt,
    ReleaseInfo? Manager, ReleaseInfo? Game);

public sealed class PackageInspection
{
    public required string PackagePath { get; init; }
    public bool IsSupportedArchive { get; init; }
    public bool IsSafe { get; init; }
    public string Sha256 { get; init; } = "";
    public long SizeBytes { get; init; }
    public IReadOnlyList<string> Entries { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public bool HasManager { get; init; }
    public bool HasManagerCore { get; init; }
    public bool HasOptiScalerDll { get; init; }
    public bool HasOptiScalerIni { get; init; }
    public bool HasManifest { get; init; }
}

public sealed record CompatibilityPreset(
    string GameId,
    string DisplayName,
    string? RecommendedProxy,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticReport(string Summary, IReadOnlyList<string> Findings, DateTimeOffset CreatedAt);
