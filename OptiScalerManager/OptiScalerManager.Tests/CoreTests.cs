using Xunit;
using System.IO.Compression;

namespace OptiScalerManager.Tests;

public sealed class CoreTests : IDisposable
{
    [Fact]
    public async Task DependencyInventory_CoversReleaseDllsAndSourceSubmodules()
    {
        var directory = AppContext.BaseDirectory;
        var inventory = await OptiScalerManager.Core.DependencyInventoryService.LoadAsync(Path.Combine(directory, "dependency-inventory.json"));
        var modules = await OptiScalerManager.Core.DependencyInventoryService.LoadSubmodulesAsync(Path.Combine(directory, "source-submodules.json"));
        Assert.Equal("0.2.3", inventory.PackageVersion);
        Assert.Equal(28, inventory.Files.Count);
        Assert.Equal(9, modules.Count);
        Assert.Contains(inventory.Files, x => x.Path == "OptiScaler/nvngx_dlssnr.dll" && x.Signature == "HashMismatch");
        Assert.Contains(inventory.Files, x => x.Path == "nvngx.dll_dlssnr.dll" && x.Version == "not specified");
        Assert.Contains(inventory.Files, x => x.Path == "OptiScaler/streamline/sl.dlss_nr.dll" && x.Version == "2.13.0.0");
        Assert.Contains(modules, x => x.Path == "external/FidelityFX-SDK-v2");
    }

    [Theory]
    [InlineData("0.2.2.0", "v0.2.3", true, "需要更新")]
    [InlineData("0.2.3.0", "v0.2.3", true, "已是最新")]
    [InlineData("0.2.4", "v0.2.3", true, "本地版本较新")]
    [InlineData(null, "v0.2.3", true, "本地版本未知")]
    [InlineData("0.2.2", "v0.2.3", false, "未发布")]
    public void UpdateVersion_ComparesEachPackageSeparately(string? current, string latest, bool available, string expected)
        => Assert.Equal(expected, OptiScalerManager.Core.UpdateVersion.Compare(current, latest, available));

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
    public async Task InstallationPreview_RejectsPackageWithoutIni()
    {
        var gameRoot = Path.Combine(_root, "game-no-ini");
        var packageRoot = Path.Combine(_root, "package-no-ini");
        Directory.CreateDirectory(gameRoot);
        Directory.CreateDirectory(packageRoot);
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "game.exe"), "exe");
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "OptiScaler.dll"), "dll");
        var game = new OptiScalerManager.Core.GameEntry { Id = new("no-ini"), DisplayName = "No INI", InstallPath = gameRoot };
        var service = new OptiScalerManager.Core.InstallationService(new OptiScalerManager.Core.SteamDiscoveryService());
        var plan = await service.PreviewInstallAsync(game, new(packageRoot, "dxgi.dll"));
        Assert.False(plan.CanProceed);
        Assert.Contains(plan.Warnings, x => x.Contains("OptiScaler.ini", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IniDocument_PreservesUtf16AndLfWithoutFinalNewline()
    {
        var path = Path.Combine(_root, "utf16.ini");
        File.WriteAllText(path, "[Menu]\nName=测试", new System.Text.UnicodeEncoding(false, true));
        var ini = OptiScalerManager.Core.IniDocument.Load(path);
        ini.Set("Menu", "Name", "更新");
        ini.SaveAtomic(path);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal((byte)0xFF, bytes[0]);
        Assert.Equal((byte)0xFE, bytes[1]);
        Assert.Equal("[Menu]\nName=更新", File.ReadAllText(path, new System.Text.UnicodeEncoding(false, true)));
    }

    [Fact]
    public async Task ConfigSave_CreatesAutomaticSnapshot()
    {
        var gameRoot = Path.Combine(_root, "auto-snapshot-game");
        Directory.CreateDirectory(gameRoot);
        var path = Path.Combine(gameRoot, "OptiScaler.ini");
        await File.WriteAllTextAsync(path, "[Menu]\nEnabled=false\n");
        var game = new OptiScalerManager.Core.GameEntry { Id = new("auto-snapshot"), DisplayName = "Auto", InstallPath = gameRoot };
        var service = new OptiScalerManager.Core.ConfigService(Path.Combine(_root, "auto-snapshots"));
        var result = await service.WriteAsync(game, [new("Menu", "Enabled", "true")], OptiScalerManager.Core.FileUtilities.Sha256(path));
        Assert.True(result.Success);
        var snapshot = Assert.Single(await service.ListSnapshotsAsync(game));
        Assert.Contains("Enabled=false", await File.ReadAllTextAsync(snapshot.Path));
        Assert.Contains("Enabled=true", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public void CapabilityReport_DistinguishesConfigurationFromMissingRuntime()
    {
        var gameRoot = Path.Combine(_root, "capability-game");
        Directory.CreateDirectory(gameRoot);
        var path = Path.Combine(gameRoot, "OptiScaler.ini");
        File.WriteAllText(path, "[DLSSG]\nInterpolationCount=5\n[DlssNr]\nEnabled=true\n");
        var report = OptiScalerManager.Core.CapabilityService.Inspect(gameRoot, OptiScalerManager.Core.IniDocument.Load(path));
        Assert.All(report, feature => { Assert.True(feature.Configured); Assert.False(feature.DependenciesPresent); });
    }

    [Fact]
    public void ExpertMetadata_ExplainsMultiplierAndKeepsCustomValue()
    {
        Assert.Contains("5=6×", OptiScalerManager.Core.ConfigMetadata.Description("DLSSG", "InterpolationCount"));
        var options = OptiScalerManager.Core.ConfigMetadata.Options("DLSSG", "InterpolationCount", "6");
        Assert.Contains("5", options);
        Assert.Contains("6", options); // Existing experimental value remains visible/editable.
        Assert.Equal("02 · 帧生成", OptiScalerManager.Core.ConfigMetadata.Category("DLSSG"));
        Assert.Contains("1 = 2X", OptiScalerManager.Core.ConfigMetadata.Documentation("DLSSG", "InterpolationCount"));
        Assert.DoesNotContain("没有此项", OptiScalerManager.Core.ConfigMetadata.Documentation("FrameGen", "FGInput"));
    }

    [Fact]
    public void ExpertMetadata_ParsesCommentsPerSectionAndKey()
    {
        var docs = OptiScalerManager.Core.ConfigMetadata.ParseDocumentation("[A]\n; Alpha explanation\nOne=auto\n\n; Beta explanation\nTwo=true\n[B]\n; Other section\nOne=false\n");
        Assert.Equal("Alpha explanation", docs["A.One"]);
        Assert.Equal("Beta explanation", docs["A.Two"]);
        Assert.Equal("Other section", docs["B.One"]);
        var detail = OptiScalerManager.Core.ConfigMetadata.DetailedExplanation("DlssNr", "WorkingScale", "0.5");
        Assert.Contains("模型内部工作分辨率", detail);
        Assert.Contains("当前值：0.5", detail);
        Assert.Contains("项目原注释", detail);
        Assert.StartsWith("推测：", OptiScalerManager.Core.ConfigMetadata.Description("UnknownSection", "MysteryFlag"));
    }

    [Fact]
    public void IniEntry_ValueNotifiesBindingAfterPresetSelection()
    {
        var entry = new OptiScalerManager.Core.IniEntry { Section = "Menu", Key = "ShowFps", Value = "auto" };
        var notified = false;
        entry.PropertyChanged += (_, e) => notified = e.PropertyName == nameof(entry.Value);
        entry.Value = "true";
        Assert.True(notified);
        Assert.Equal("true", entry.Value);
    }

    [Fact]
    public void ReleaseCatalog_SelectsExactManagerAndGameAssets()
    {
        const string assets = """[{"name":"random.zip","browser_download_url":"https://example.test/random.zip"},{"name":"OptiScalerManager-win-x64.zip","browser_download_url":"https://example.test/manager.zip"},{"name":"OptiScalerManager-win-x64.zip.manifest.json","browser_download_url":"https://example.test/manager.json"},{"name":"OptiScaler-Package-win-x64.zip","browser_download_url":"https://example.test/game.zip"},{"name":"OptiScaler-Package-win-x64.zip.manifest.json","browser_download_url":"https://example.test/game.json"}]""";
        using var document = System.Text.Json.JsonDocument.Parse("{\"tag_name\":\"v1\",\"name\":\"Stable\",\"body\":\"\",\"published_at\":\"2026-09-24T00:00:00Z\",\"assets\":" + assets + "}");
        var catalog = OptiScalerManager.Core.ReleaseService.ParseCatalog(document.RootElement, true);
        Assert.Equal("manager", catalog.Manager?.PackageType);
        Assert.Equal("https://example.test/manager.zip", catalog.Manager?.DownloadUrl);
        Assert.Equal("game", catalog.Game?.PackageType);
        Assert.Equal("https://example.test/game.zip", catalog.Game?.DownloadUrl);
        Assert.Null(OptiScalerManager.Core.ReleaseService.ParseCatalog(document.RootElement, false).Manager);
    }

    [Fact]
    public void ReleaseManifest_RejectsWrongPackageType()
    {
        var hash = new string('a', 64);
        var manager = new OptiScalerManager.Core.ReleaseInfo { TagName = "v1", Name = "Stable", PackageType = "manager" };
        var valid = "{\"releaseChannel\":\"own\",\"packageType\":\"manager\",\"packageSha256\":\"" + hash + "\"}";
        OptiScalerManager.Core.ReleaseManifestValidator.Validate(valid, manager, hash);
        var wrong = valid.Replace("\"manager\"", "\"game\"");
        Assert.Throws<InvalidDataException>(() => OptiScalerManager.Core.ReleaseManifestValidator.Validate(wrong, manager, hash));
    }

    [Fact]
    public async Task ManagerArchive_ExtractsWithoutGameDll()
    {
        var zip = Path.Combine(_root, "manager.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("OptiScalerManager.exe");
            archive.CreateEntry("OptiScalerManager.Core.dll");
        }
        var destination = Path.Combine(_root, "manager-extracted");
        await OptiScalerManager.Core.ReleaseService.ExtractPackageAsync(zip, destination, "manager");
        Assert.True(File.Exists(Path.Combine(destination, "OptiScalerManager.exe")));
        await Assert.ThrowsAsync<InvalidDataException>(() => OptiScalerManager.Core.ReleaseService.ExtractPackageAsync(zip, Path.Combine(_root, "wrong-type"), "game"));
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

        var missingIni = Path.Combine(_root, "missing-ini.zip");
        using (var archive = ZipFile.Open(missingIni, ZipArchiveMode.Create)) archive.CreateEntry("OptiScaler.dll");
        var incomplete = await service.InspectPackageAsync(missingIni);
        Assert.False(incomplete.HasOptiScalerIni);
        await Assert.ThrowsAsync<InvalidDataException>(() => OptiScalerManager.Core.ReleaseService.ExtractPackageAsync(missingIni, Path.Combine(_root, "incomplete-extract")));
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
