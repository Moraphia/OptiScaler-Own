using OptiScalerManager.Core;
using System.IO.Compression;
using Xunit;

namespace OptiScalerManager.Tests;

public sealed class PipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OptiScalerPipelineTests", Guid.NewGuid().ToString("N"));
    public PipelineTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void ExtractsOnlyThe64BitModuleFromOfficialStyleSetupArchive()
    {
        using var embedded = new MemoryStream();
        using (var zip = new ZipArchive(embedded, ZipArchiveMode.Create, true))
        {
            using (var x86 = new StreamWriter(zip.CreateEntry("ReShade32.dll").Open())) x86.Write("x86");
            using (var x64 = new StreamWriter(zip.CreateEntry("ReShade64.dll").Open())) x64.Write("x64-addon");
        }
        var installer = new byte[512 + checked((int)embedded.Length)];
        embedded.ToArray().CopyTo(installer, 512);
        Assert.Equal("x64-addon", System.Text.Encoding.UTF8.GetString(OfficialReShadeService.ExtractModule(installer)));
        Assert.Throws<InvalidDataException>(() => OfficialReShadeService.ExtractModule(new byte[1024]));
    }

    [Fact]
    public async Task LocalPackageMatrixSeparatesSnapshotMatchesChangesAndMissingFiles()
    {
        var package = Path.Combine(_root, "matrix-package");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "same.dll"), "same");
        File.WriteAllText(Path.Combine(package, "changed.dll"), "new");
        var files = new[] { "same.dll", "changed.dll", "missing.dll" }
            .Select(name => new PublishedDll(name, "1.0", "test", "unknown",
                name == "same.dll" ? FileUtilities.Sha256(Path.Combine(package, name)) : new string('0', 64))).ToArray();
        var rows = await DependencyInventoryService.CompareLocalPackageAsync(new("test", new string('0', 64), files), package);
        Assert.Equal(["一致", "与快照不同", "缺失"], rows.Select(x => x.LocalStatus));
    }

    [Theory]
    [InlineData("eldenring.exe", KnownGame.EldenRing, RenderPipeline.ReShadeBridge)]
    [InlineData("sekiro.exe", KnownGame.Sekiro, RenderPipeline.ReShadeBridge)]
    [InlineData("nightreign.exe", KnownGame.Nightreign, RenderPipeline.ReShadeBridge)]
    public void RecognizesSoulsProfiles(string exe, KnownGame expectedGame, RenderPipeline expectedPipeline)
    {
        var game = new GameEntry { Id = new("test"), DisplayName = exe, InstallPath = _root, ExecutablePath = Path.Combine(_root, exe) };
        var advice = PipelineAdvisor.For(game);
        Assert.Equal(expectedGame, advice.Game);
        Assert.Equal(expectedPipeline, advice.Recommended);
        Assert.NotEmpty(advice.Requirements);
    }

    [Fact]
    public void NightreignTakesPriorityOverEldenRingName()
    {
        var game = new GameEntry { Id = new("test"), DisplayName = "ELDEN RING NIGHTREIGN", InstallPath = _root };
        Assert.Equal(KnownGame.Nightreign, PipelineAdvisor.For(game).Game);
    }

    [Theory]
    [InlineData(@"Game\eldenring.exe")]
    [InlineData(@"Game\nightreign.exe")]
    [InlineData("sekiro.exe")]
    public void FindsActualExecutableInsteadOfLauncher(string relative)
    {
        var exe = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "game");
        File.WriteAllText(Path.Combine(_root, "start_protected_game.exe"), "launcher");
        Assert.Equal(exe, KnownGamePathResolver.FindExecutable(_root));
    }

    [Fact]
    public void EldenRingKeepsExistingDxgiAndUsesTestedWinmmProxy()
    {
        var game = new GameEntry { Id = new("elden"), DisplayName = "ELDEN RING", InstallPath = _root, ExecutablePath = Path.Combine(_root, "eldenring.exe") };
        Directory.CreateDirectory(Path.Combine(_root, "ERSS2"));
        File.WriteAllText(Path.Combine(_root, "dxgi.dll"), "ERSS loader");
        var profile = KnownGameInstallProfiles.For(game, "winmm.dll");
        Assert.Equal("winmm.dll", profile.ProxyDll);
        Assert.Contains(profile.Notices, x => x.Contains("winmm.dll"));
    }

    [Fact]
    public async Task EldenRingProfileDoesNotWriteOrImportWithoutVerifiedModules()
    {
        var game = new GameEntry { Id = new("elden-safe"), DisplayName = "ELDEN RING", InstallPath = _root,
            ExecutablePath = Path.Combine(_root, "eldenring.exe") };
        File.WriteAllText(Path.Combine(_root, "ERSS.dll"), "not a DLL");
        File.WriteAllText(Path.Combine(_root, "OptiScaler.ini"), "[DLSSG]\nAdaMfgWrapperOnly=false\n");
        var service = new EldenRingCompatibilityService(Path.Combine(_root, "profile-backups"));
        Assert.False(service.Inspect(game).CanApply);
        Assert.False((await service.SetUnlockAsync(game, true)).Success);
        Assert.False(service.ImportRtxMfg(game, Path.Combine(_root, "ERSS.dll")).Success);
        Assert.Equal("false", IniDocument.Load(Path.Combine(_root, "OptiScaler.ini")).Get("DLSSG", "AdaMfgWrapperOnly"));
        Assert.False(File.Exists(Path.Combine(_root, "dinput8.dll")));
    }

    [Fact]
    public void SekiroAutomaticallyEnablesExistingReshadeChain()
    {
        var game = new GameEntry { Id = new("sekiro"), DisplayName = "Sekiro", InstallPath = _root, ExecutablePath = Path.Combine(_root, "sekiro.exe") };
        File.WriteAllText(Path.Combine(_root, "ReShade64.dll"), "reshade");
        var profile = KnownGameInstallProfiles.For(game, "winmm.dll");
        Assert.Equal("dxgi.dll", profile.ProxyDll);
        Assert.Contains(profile.IniOverrides, x => x.Section == "Plugins" && x.Key == "LoadReshade" && x.Value == "true");
    }

    [Fact]
    public async Task BridgeInstallIsReversibleAndRefusesForeignProxy()
    {
        var gameDir = Path.Combine(_root, "game"); var sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(gameDir); Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(gameDir, "game.exe"), "game");
        foreach (var name in new[] { "ReShade64.dll", "dlss5-bridge.addon64", "renodx-dlss5.addon64", "nvngx_dlss.dll", "nvngx_dlssnr.dll" })
            File.WriteAllText(Path.Combine(sourceDir, name), name);
        var game = new GameEntry { Id = new("fake-id"), DisplayName = "Game", InstallPath = gameDir, ExecutablePath = Path.Combine(gameDir, "game.exe") };
        var service = new ReShadeBridgeService(Path.Combine(_root, "state"));
        File.WriteAllText(Path.Combine(gameDir, "dxgi.dll"), "foreign");
        Assert.False(service.Preview(game, sourceDir).CanProceed);
        Assert.Contains("foreign", File.ReadAllText(Path.Combine(gameDir, "dxgi.dll")));
        File.Delete(Path.Combine(gameDir, "dxgi.dll"));
        Assert.True(service.Preview(game, sourceDir).CanProceed);
        Assert.True((await service.InstallAsync(game, sourceDir)).Success);
        Assert.True(service.IsManaged(game));
        Assert.Equal("ReShade64.dll", File.ReadAllText(Path.Combine(gameDir, "dxgi.dll")));
        Assert.False(service.Preview(game, sourceDir).CanProceed);
        File.WriteAllText(Path.Combine(gameDir, "dxgi.dll"), "user-change");
        Assert.False((await service.UninstallAsync(game)).Success);
        Assert.Equal("user-change", File.ReadAllText(Path.Combine(gameDir, "dxgi.dll")));
        File.WriteAllText(Path.Combine(gameDir, "dxgi.dll"), "ReShade64.dll");
        Assert.True((await service.UninstallAsync(game)).Success);
        Assert.False(File.Exists(Path.Combine(gameDir, "dxgi.dll")));
        Assert.True(File.Exists(Path.Combine(gameDir, "game.exe")));
    }

    [Fact]
    public async Task BridgeInstallSkipsMatchingExistingAddonAndKeepsItOnUninstall()
    {
        var gameDir = Path.Combine(_root, "matching-game"); var sourceDir = Path.Combine(_root, "matching-source");
        Directory.CreateDirectory(gameDir); Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(gameDir, "game.exe"), "game");
        foreach (var name in new[] { "ReShade64.dll", "dlss5-bridge.addon64", "renodx-dlss5.addon64", "nvngx_dlss.dll", "nvngx_dlssnr.dll" })
            File.WriteAllText(Path.Combine(sourceDir, name), name);
        File.Copy(Path.Combine(sourceDir, "renodx-dlss5.addon64"), Path.Combine(gameDir, "renodx-dlss5.addon64"));
        var game = new GameEntry { Id = new("matching"), DisplayName = "Game", InstallPath = gameDir, ExecutablePath = Path.Combine(gameDir, "game.exe") };
        var service = new ReShadeBridgeService(Path.Combine(_root, "state-matching"));
        var plan = service.Preview(game, sourceDir);
        Assert.True(plan.CanProceed, string.Join(";", plan.Warnings));
        Assert.Equal(4, plan.Files.Count);
        Assert.True((await service.InstallAsync(game, sourceDir)).Success);
        Assert.True((await service.UninstallAsync(game)).Success);
        Assert.True(File.Exists(Path.Combine(gameDir, "renodx-dlss5.addon64")));
        Assert.False(File.Exists(Path.Combine(gameDir, "dlss5-bridge.addon64")));
    }

    [Fact]
    public void BridgeInstallRefusesDifferentExistingAddon()
    {
        var gameDir = Path.Combine(_root, "different-game"); var sourceDir = Path.Combine(_root, "different-source");
        Directory.CreateDirectory(gameDir); Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(gameDir, "game.exe"), "game");
        foreach (var name in new[] { "ReShade64.dll", "dlss5-bridge.addon64", "renodx-dlss5.addon64", "nvngx_dlss.dll", "nvngx_dlssnr.dll" })
            File.WriteAllText(Path.Combine(sourceDir, name), name);
        File.WriteAllText(Path.Combine(gameDir, "renodx-dlss5.addon64"), "other version");
        var game = new GameEntry { Id = new("different"), DisplayName = "Game", InstallPath = gameDir, ExecutablePath = Path.Combine(gameDir, "game.exe") };
        var service = new ReShadeBridgeService(Path.Combine(_root, "state-different"));
        var plan = service.Preview(game, sourceDir);
        Assert.False(plan.CanProceed);
        Assert.Contains(plan.Warnings, warning => warning.Contains("renodx-dlss5.addon64"));
    }

    [Fact]
    public async Task GameSpecificIniOverrideIsIncludedInManagedManifest()
    {
        var gameDir = Path.Combine(_root, "sekiro-game"); var package = Path.Combine(_root, "opti-package");
        Directory.CreateDirectory(gameDir); Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(gameDir, "sekiro.exe"), "game");
        File.WriteAllText(Path.Combine(gameDir, "ReShade64.dll"), "existing reshade");
        File.WriteAllText(Path.Combine(package, "OptiScaler.dll"), "opti");
        File.WriteAllText(Path.Combine(package, "OptiScaler.ini"), "[Plugins]\nLoadReshade=auto\n");
        var game = new GameEntry { Id = new("sekiro"), DisplayName = "Sekiro", InstallPath = gameDir, ExecutablePath = Path.Combine(gameDir, "sekiro.exe") };
        var profile = KnownGameInstallProfiles.For(game, "winmm.dll");
        var installer = new InstallationService(new SteamDiscoveryService());
        var result = await installer.InstallAsync(game, new(package, profile.ProxyDll, false, profile.IniOverrides));
        Assert.True(result.Success, result.Message + " " + string.Join(";", result.Errors));
        Assert.Equal("true", IniDocument.Load(Path.Combine(gameDir, "OptiScaler.ini")).Get("Plugins", "LoadReshade"));
        Assert.True((await installer.VerifyAsync(game)).Success);
        Assert.True((await installer.UninstallAsync(game)).Success);
        Assert.True(File.Exists(Path.Combine(gameDir, "ReShade64.dll")));
    }
}
