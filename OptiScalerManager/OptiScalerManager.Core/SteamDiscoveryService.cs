using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace OptiScalerManager.Core;

public sealed class SteamDiscoveryService : IGameDiscoveryService
{
    public Task<IReadOnlyList<GameCandidate>> ScanSteamAsync(CancellationToken cancellationToken = default)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Valve\Steam");
                var install = key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(install)) roots.Add(install);
            }
            catch { /* registry access is optional */ }
        }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

        var result = new List<GameCandidate>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var library in ReadLibraryFolders(root))
            {
                var apps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var text = File.ReadAllText(manifest);
                    var name = Match(text, "name") ?? Path.GetFileNameWithoutExtension(manifest);
                    var relative = Match(text, "installdir");
                    if (relative is null) continue;
                    var gamePath = Path.Combine(library, "steamapps", "common", relative);
                    if (Directory.Exists(gamePath)) result.Add(new GameCandidate(name, gamePath, FindExecutable(gamePath), "Steam"));
                }
            }
        }
        return Task.FromResult<IReadOnlyList<GameCandidate>>(result.GroupBy(x => x.InstallPath, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList());
    }

    public Task<GameInspection> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(path);
        var game = new GameEntry { Id = new GameId(FileUtilities.SafeGameId(full)), DisplayName = new DirectoryInfo(full).Name, InstallPath = full, ExecutablePath = FindExecutable(full) };
        var proxy = new[] { "dxgi.dll", "winmm.dll", "version.dll", "d3d12.dll", "dbghelp.dll", "wininet.dll", "winhttp.dll" }
            .FirstOrDefault(x => File.Exists(Path.Combine(full, x)) && IsOptiScalerDll(Path.Combine(full, x)));
        var optiDir = Path.Combine(full, "OptiScaler");
        var config = Path.Combine(full, "OptiScaler.ini");
        var manifest = Path.Combine(optiDir, "RuntimeSync", "manifest.json");
        var adoption = Path.Combine(optiDir, "RuntimeSync", "legacy-adoption.json");
        var legacy = new List<string>();
        foreach (var artifact in new[] { "nvapi64.dll", "nvngx.dll", "OptiScaler.asi", "Remove OptiScaler.bat", "Remove_OptiScaler.bat" }) if (File.Exists(Path.Combine(full, artifact))) legacy.Add(artifact);
        foreach (var candidate in new[] { "dxgi.dll", "winmm.dll", "d3d12.dll", "dbghelp.dll", "version.dll", "wininet.dll", "winhttp.dll" })
        {
            var candidatePath = Path.Combine(full, candidate);
            if (File.Exists(candidatePath) && IsOptiScalerDll(candidatePath)) legacy.Add(candidate);
        }
        var state = proxy is not null && Directory.Exists(optiDir) ? InstallState.Installed : Directory.Exists(optiDir) || File.Exists(config) ? InstallState.Partial : InstallState.NotDetected;
        if (File.Exists(manifest) && proxy is not null) state = InstallState.Installed;
        else if (File.Exists(adoption)) state = InstallState.NeedsRepair;
        else if (legacy.Count > 0) state = InstallState.Legacy;
        var warnings = legacy.Select(x => $"Legacy BAT artifact detected: {x}").ToList();
        var files = ReadManagedFiles(manifest, optiDir);
        if (files.Count == 0 && Directory.Exists(optiDir)) files = Directory.EnumerateFiles(optiDir, "*.dll", SearchOption.TopDirectoryOnly).Select(p => new RuntimeFileRecord(p, Path.GetFileName(p), GetFileVersion(p), TryHash(p), false, false)).ToList();
        if (files.Any(x => !x.IntegrityVerified)) warnings.Add("One or more managed files failed SHA256 verification.");
        var integrityFailed = files.Any(x => !x.IntegrityVerified);
        if (integrityFailed && File.Exists(manifest)) state = InstallState.NeedsRepair;
        return Task.FromResult(new GameInspection { Game = game, InstallState = state, ProxyDll = proxy, OptiScalerVersion = proxy is null ? null : GetFileVersion(Path.Combine(full, proxy)), ConfigExists = File.Exists(config), HasBackup = Directory.Exists(Path.Combine(optiDir, "RuntimeSync", "backup")), RuntimeSyncStatus = File.Exists(manifest) ? (integrityFailed ? "Manifest hash mismatch" : "Manifest verified") : File.Exists(adoption) ? "Legacy installation registered" : legacy.Count > 0 ? "Legacy BAT installation detected" : "No manifest", RuntimeFiles = files, Warnings = warnings, LegacyArtifacts = legacy });
    }

    private static IEnumerable<string> ReadLibraryFolders(string steamRoot)
    {
        yield return steamRoot;
        var file = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file)) yield break;
        foreach (Match m in Regex.Matches(File.ReadAllText(file), "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)) yield return m.Groups[1].Value.Replace("\\\\", "\\");
    }
    private static string? Match(string text, string key) => Regex.Match(text, $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase).Groups[1].Value is { Length: > 0 } value ? value : null;
    private static string? FindExecutable(string path) => Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly).OrderByDescending(FileLength).FirstOrDefault();
    private static long FileLength(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }
    private static bool IsOptiScalerDll(string path) => FileVersionInfo.GetVersionInfo(path).OriginalFilename?.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) == true || Path.GetFileName(path).Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase);
    private static string TryHash(string path) { try { return FileUtilities.Sha256(path); } catch { return "unavailable"; } }
    private static string? GetFileVersion(string path) { try { return FileVersionInfo.GetVersionInfo(path).FileVersion?.Trim(); } catch { return null; } }
    private static List<RuntimeFileRecord> ReadManagedFiles(string manifest, string optiDir)
    {
        var result = new List<RuntimeFileRecord>();
        if (!File.Exists(manifest)) return result;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
            if (!doc.RootElement.TryGetProperty("Files", out var entries)) return result;
            foreach (var entry in entries.EnumerateArray())
            {
                var path = entry.TryGetProperty("Destination", out var destination) ? destination.GetString() : null;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                var actual = TryHash(path);
                var expected = entry.TryGetProperty("Sha256", out var hash) ? hash.GetString() : null;
                result.Add(new RuntimeFileRecord(path, Path.GetFileName(path), GetFileVersion(path), actual, true, false, string.IsNullOrWhiteSpace(expected) || actual.Equals(expected, StringComparison.OrdinalIgnoreCase)));
            }
        }
        catch { }
        return result;
    }
}
