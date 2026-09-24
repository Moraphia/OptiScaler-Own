using Xunit;
using System.IO.Compression;

namespace OptiScalerManager.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OptiScalerManagerTests", Guid.NewGuid().ToString("N"));
    public CoreTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void IniDocument_PreservesCommentsAndUnknownKeys()
    {
        var path = Path.Combine(_root, "OptiScaler.ini");
        File.WriteAllText(path, "; keep this comment\r\n[Menu]\r\nUnknown=keep\r\nShortcutKey=45\r\n");
        var ini = OptiScalerManager.Core.IniDocument.Load(path);
        ini.Set("Menu", "ShortcutKey", "46"); ini.SaveAtomic(path);
        var text = File.ReadAllText(path);
        Assert.Contains("; keep this comment", text); Assert.Contains("Unknown=keep", text); Assert.Contains("ShortcutKey=46", text);
    }

    [Fact]
    public void FileUtilities_Sha256IsStable()
    {
        var path = Path.Combine(_root, "data.bin"); File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Equal(64, OptiScalerManager.Core.FileUtilities.Sha256(path).Length);
        Assert.Equal(OptiScalerManager.Core.FileUtilities.Sha256(path), OptiScalerManager.Core.FileUtilities.Sha256(path));
    }

    [Fact]
    public async Task InstallationPreview_RejectsMissingPackage()
    {
        var game = new OptiScalerManager.Core.GameEntry { Id = new("test"), DisplayName = "Test", InstallPath = _root };
        var service = new OptiScalerManager.Core.InstallationService(new OptiScalerManager.Core.SteamDiscoveryService());
        var plan = await service.PreviewInstallAsync(game, new("missing", "dxgi.dll"));
        Assert.False(plan.CanProceed); Assert.Contains(plan.Warnings, x => x.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageInspection_RejectsTraversalAndAcceptsReleaseZip()
    {
        var valid = Path.Combine(_root, "release.zip");
        using (var archive = ZipFile.Open(valid, ZipArchiveMode.Create))
        {
            var dll = archive.CreateEntry("OptiScaler.dll");
            await using (var stream = dll.Open()) await stream.WriteAsync(new byte[] { 1, 2, 3 });
            archive.CreateEntry("OptiScaler.ini");
        }
        using var service = new OptiScalerManager.Core.ReleaseService();
        var result = await service.InspectPackageAsync(valid);
        Assert.True(result.IsSafe);
        Assert.True(result.HasOptiScalerDll);

        var unsafeZip = Path.Combine(_root, "unsafe.zip");
        using (var archive = ZipFile.Open(unsafeZip, ZipArchiveMode.Create)) archive.CreateEntry("../outside.txt");
        var unsafeResult = await service.InspectPackageAsync(unsafeZip);
        Assert.False(unsafeResult.IsSafe);
        Assert.Contains(unsafeResult.Errors, x => x.Contains("Unsafe archive path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VerifyDetectsTamperedManagedFileAndBlocksUninstall()
    {
        var gameRoot = Path.Combine(_root, "game");
        var packageRoot = Path.Combine(_root, "package");
        Directory.CreateDirectory(gameRoot);
        Directory.CreateDirectory(packageRoot);
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "game.exe"), "exe");
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "OptiScaler.dll"), "managed dll");
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "OptiScaler.ini"), "[Test]\nEnabled=false\n");
        var game = new OptiScalerManager.Core.GameEntry { Id = new("hash-test"), DisplayName = "Hash Test", InstallPath = gameRoot, ExecutablePath = Path.Combine(gameRoot, "game.exe") };
        var service = new OptiScalerManager.Core.InstallationService(new OptiScalerManager.Core.SteamDiscoveryService());

        var installed = await service.InstallAsync(game, new(packageRoot, "dxgi.dll"));
        Assert.True(installed.Success);
        Assert.True((await service.VerifyAsync(game)).Success);
        await File.AppendAllTextAsync(Path.Combine(gameRoot, "dxgi.dll"), "tampered");
        var verification = await service.VerifyAsync(game);
        Assert.False(verification.Success);
        Assert.Contains("SHA256", verification.Message, StringComparison.OrdinalIgnoreCase);
        var uninstall = await service.UninstallAsync(game);
        Assert.False(uninstall.Success);
        Assert.Contains("blocked", uninstall.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigServiceDetectsExternalEditAndRestoresVerifiedSnapshot()
    {
        var gameRoot = Path.Combine(_root, "config-game");
        var snapshotRoot = Path.Combine(_root, "snapshots");
        Directory.CreateDirectory(gameRoot);
        var configPath = Path.Combine(gameRoot, "OptiScaler.ini");
        await File.WriteAllTextAsync(configPath, "; preserved\n[Upscaler]\nSharpness=auto\n");
        var game = new OptiScalerManager.Core.GameEntry { Id = new("config-test"), DisplayName = "Config Test", InstallPath = gameRoot };
        var service = new OptiScalerManager.Core.ConfigService(snapshotRoot);
        var loaded = await service.ReadAsync(game);
        await File.AppendAllTextAsync(configPath, "[User]\nChanged=true\n");
        var blocked = await service.WriteAsync(game, new[] { new OptiScalerManager.Core.ConfigChange("Upscaler", "Sharpness", "0.5") }, loaded.Sha256);
        Assert.False(blocked.Success);
        Assert.Contains("changed outside", blocked.Message, StringComparison.OrdinalIgnoreCase);

        var snapshot = await service.CreateSnapshotAsync(game, "test snapshot");
        await File.WriteAllTextAsync(configPath, "corrupt");
        var restored = await service.RestoreSnapshotAsync(game, snapshot);
        Assert.True(restored.Success);
        Assert.Equal(snapshot.Sha256, OptiScalerManager.Core.FileUtilities.Sha256(configPath));
        Assert.Single(await service.ListSnapshotsAsync(game));
    }

    [Fact]
    public async Task LegacyBatInstallationIsDetectedAndAdoptedWithoutDeletingFiles()
    {
        var gameRoot = Path.Combine(_root, "legacy-game");
        Directory.CreateDirectory(gameRoot);
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "game.exe"), "exe");
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "OptiScaler.asi"), "legacy");
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "Remove OptiScaler.bat"), "legacy remover");
        var game = new OptiScalerManager.Core.GameEntry { Id = new("legacy-test"), DisplayName = "Legacy Test", InstallPath = gameRoot };
        var discovery = new OptiScalerManager.Core.SteamDiscoveryService();
        var service = new OptiScalerManager.Core.InstallationService(discovery);
        var inspection = await discovery.InspectAsync(gameRoot);
        Assert.Equal(OptiScalerManager.Core.InstallState.Legacy, inspection.InstallState);
        Assert.Contains("OptiScaler.asi", inspection.LegacyArtifacts);
        var adopted = await service.AdoptLegacyAsync(game);
        Assert.True(adopted.Success);
        Assert.True(File.Exists(Path.Combine(gameRoot, "OptiScaler.asi")));
        Assert.True(File.Exists(Path.Combine(gameRoot, "OptiScaler", "RuntimeSync", "legacy-adoption.json")));
    }

    [Fact]
    public async Task RuntimeSyncReplacesAndRestoresGameOwnedRuntime()
    {
        var gameRoot = Path.Combine(_root, "runtime-game");
        var packageRoot = Path.Combine(_root, "runtime-package");
        Directory.CreateDirectory(gameRoot);
        Directory.CreateDirectory(Path.Combine(packageRoot, "OptiScaler"));
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "game.exe"), "exe");
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "nvngx_dlss.dll"), "game runtime");
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "OptiScaler.dll"), "managed dll");
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "OptiScaler.ini"), "[Test]\nEnabled=false\n");
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "OptiScaler", "nvngx_dlss.dll"), "bundled runtime");
        var game = new OptiScalerManager.Core.GameEntry { Id = new("runtime-test"), DisplayName = "Runtime Test", InstallPath = gameRoot };
        var service = new OptiScalerManager.Core.InstallationService(new OptiScalerManager.Core.SteamDiscoveryService());

        var installed = await service.InstallAsync(game, new(packageRoot, "dxgi.dll"));
        Assert.True(installed.Success);
        Assert.Equal("bundled runtime", await File.ReadAllTextAsync(Path.Combine(gameRoot, "nvngx_dlss.dll")));
        Assert.True((await service.VerifyAsync(game)).Success);
        var inspection = await new OptiScalerManager.Core.SteamDiscoveryService().InspectAsync(gameRoot);
        Assert.Contains(inspection.RuntimeFiles, x => x.FileName.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase) && x.IntegrityVerified);
        var uninstalled = await service.UninstallAsync(game);
        Assert.True(uninstalled.Success);
        Assert.Equal("game runtime", await File.ReadAllTextAsync(Path.Combine(gameRoot, "nvngx_dlss.dll")));
    }

    [Fact]
    public async Task UpstreamMatrixCombinesManifestAndLockState()
    {
        var manifest = Path.Combine(_root, "upstreams.json");
        var lockFile = Path.Combine(_root, "upstreams.lock.json");
        await File.WriteAllTextAsync(manifest, "{\"upstreams\":[{\"id\":\"optiscaler\",\"repository\":\"optiscaler/OptiScaler\",\"updatePolicy\":\"commit\",\"license\":\"MIT\",\"redistributionPolicy\":\"source-and-build\",\"enabled\":true},{\"id\":\"xess\",\"repository\":\"intel/xess\",\"updatePolicy\":\"release\",\"license\":\"Intel\",\"redistributionPolicy\":\"review\",\"enabled\":true}]}" );
        await File.WriteAllTextAsync(lockFile, "{\"upstreams\":[{\"id\":\"optiscaler\",\"version\":\"0123456789abcdef0123456789abcdef01234567\",\"checkedAt\":\"2026-09-15T00:00:00Z\"}]}" );
        var matrix = await new OptiScalerManager.Core.UpstreamService().LoadMatrixAsync(manifest, lockFile);
        Assert.Equal(2, matrix.Count);
        Assert.Equal("0123456789ab", matrix.Single(x => x.Id == "optiscaler").Version);
        Assert.Equal("待 CI 检查", matrix.Single(x => x.Id == "xess").Status);
    }
}
