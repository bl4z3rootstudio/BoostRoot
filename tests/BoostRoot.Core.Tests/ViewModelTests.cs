using BoostRoot.Core.Boost;
using BoostRoot.Core.Hardware;
using BoostRoot.Core.Scoring;
using BoostRoot.Core.Net;
using BoostRoot.Core.Tools;
using BoostRoot.Core.Monitoring;
using BoostRoot.Core.Stress;
using BoostRoot.Core.Profiles;
using BoostRoot.Games;
using BoostRoot.ViewModels;
using BoostRoot.ViewModels.Localization;

namespace BoostRoot.Core.Tests;

internal sealed class FakeAppServices : IAppServices
{
    public List<CatalogEntry> Catalog { get; } = [];
    public int PcScore { get; set; } = 72;
    public BoostResult NextResult { get; set; } = new("op1", true, null, [new StepResult("game-settings", StepStatus.Done, null, 3)], null);
    public List<(string Profile, BoostLevel Level)> Boosts { get; } = [];
    public List<string> Undone { get; } = [];
    public List<string> Launched { get; } = [];
    public AppSettings Saved { get; private set; } = AppSettings.Default;
    public string? LastOp { get; set; }

    public IReadOnlyList<CatalogEntry> LoadCatalog() => Catalog;
    public BoostResult Boost(CatalogEntry entry, BoostLevel level, AppSettings settings) { Boosts.Add((entry.Profile!.Id, level)); if (NextResult.Success) LastOp = NextResult.OperationId; return NextResult; }
    public void Undo(string operationId) { Undone.Add(operationId); LastOp = null; }
    public bool Launch(CatalogEntry entry) { Launched.Add(entry.DisplayName); return true; }
    public AppSettings LoadSettings() => Saved;
    public void SaveSettings(AppSettings settings) => Saved = settings;
    public string? LastBoostOperation(string profileId) => LastOp;
    public string? CoverPath(CatalogEntry entry) => null;
    public HardwareReport ScanHardware()
    {
        var hw = new HardwareInfo("Intel(R) Core(TM) i5-9400F", 6, [new GpuInfo("Radeon RX 580 Series", GpuVendor.Amd, 4096, "32", new DateOnly(2026, 5, 20))], 32,
            [new RamModule(16384, 2666, 2133)], [new DisplayInfo("Legion", 1440, 1080, 240, 240)], 13, null, [new NetInfo("Realtek", false, 1000)], 26300);
        var issues = HealthChecks.Evaluate(hw, new DateOnly(2026, 11, 12));
        var score = HealthChecks.Score(hw, issues);
        PcScore = score.Total;
        return new HardwareReport(hw, issues, score);
    }
    public IReadOnlyList<NetInfo> NetworkAdapters() => [new NetInfo("eth", false, 1000)];
    public Task<PingStats> PingAsync(string host, CancellationToken ct) => Task.FromResult(NetAnalysis.Summarize(host, 4, [12, 13, 12, 14]));
    public Task<SpeedResult> SpeedTestAsync(IProgress<double>? progress, CancellationToken ct) { progress?.Report(1); return Task.FromResult(new SpeedResult(95.5, 19.2, 12, 25)); }
    public long Cleaned { get; private set; }
    public IReadOnlyList<JunkScan> ScanJunk() => [new JunkScan(new JunkCategory("user-temp", new("Geçici", "Temp"), ["C:/T"], "*"), ["C:/T/a"], 5 << 20)];
    public long CleanJunk(IEnumerable<JunkScan> scans) => Cleaned = scans.Sum(s => s.Bytes);
    public List<(string, bool)> StartupChanges { get; } = [];
    public IReadOnlyList<StartupItem> StartupItems() => [new StartupItem("Spotify", "spotify.exe", BoostRoot.Core.Platform.RegistryHive.CurrentUser, "Run", true)];
    public void SetStartupEnabled(StartupItem item, bool enabled) => StartupChanges.Add((item.Name, enabled));
    public DriverStatus? CheckDriver() => null;
    public List<string> Opened { get; } = [];
    public void OpenUrl(string url) => Opened.Add(url);
    public ISensors Sensors { get; set; } = new FakeSensors();
    public IStressWorkload? CreateGpuStress() => null;
    public long RamStressBytes() => 4 << 20;
}

public sealed class FakeSensors : ISensors
{
    public double GpuTemp { get; set; } = 60;
    public SensorSnapshot Read() => new(DateTime.UtcNow, CpuLoad: 100, CpuMhz: 4000, GpuTempC: GpuTemp, RamUsedPct: 50);
    public void Dispose() { }
}

public class ViewModelTests
{
    private static GameProfile Example => ProfileLoader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "_example.json")));
    private static InstalledGame Game => new("riot", "valorant", "VALORANT", "C:/x", new Dictionary<string, string>());

    private static FakeAppServices Services()
    {
        var s = new FakeAppServices();
        s.Catalog.Add(new CatalogEntry(Example, Game));
        s.Catalog.Add(new CatalogEntry(Example with { Id = "other", Name = "Other" }, null));
        return s;
    }

    [Fact]
    public void Loc_Tables_HaveSameKeys()
    {
        var tr = Loc.Load("tr");
        var en = Loc.Load("en");
        Assert.Equal(tr.Keys.Order(), en.Keys.Order());
        Assert.All(tr.Values.Concat(en.Values), v => Assert.False(string.IsNullOrWhiteSpace(v)));
    }

    [Fact]
    public void Home_SelectsFirstInstalled_AndRecommendedLevel()
    {
        var vm = new HomeViewModel(Services());
        Assert.Equal("example-valorant", vm.Selected!.Entry.Profile!.Id);
        Assert.Equal(BoostLevel.Competitive, vm.SelectedLevel); // score 72 ≥ 40 → default rule
        Assert.True(vm.Levels.Single(l => l.Level == BoostLevel.Competitive).IsRecommended);
    }

    [Fact]
    public void Home_LowScore_RecommendsMaxFps()
    {
        var s = Services();
        s.PcScore = 30;
        Assert.Equal(BoostLevel.MaxFps, new HomeViewModel(s).SelectedLevel);
    }

    [Fact]
    public void Home_CannotSelectNotInstalled()
    {
        var vm = new HomeViewModel(Services());
        vm.Select(vm.Games.Single(g => !g.IsInstalled));
        Assert.Equal("example-valorant", vm.Selected!.Entry.Profile!.Id);
    }

    [Fact]
    public async Task Home_Boost_CallsServiceWithChosenLevel_ThenAsksLaunch_AndUndoWorks()
    {
        var s = Services();
        var vm = new HomeViewModel(s);
        vm.ChooseLevel(vm.Levels.Single(l => l.Level == BoostLevel.Balanced));
        await vm.BoostCommand.ExecuteAsync(null);
        Assert.Equal(("example-valorant", BoostLevel.Balanced), s.Boosts.Single());
        Assert.True(vm.AskLaunch);
        Assert.False(vm.StatusIsError);
        Assert.Equal("op1", vm.LastOperation);
        vm.LaunchCommand.Execute(null);
        Assert.Equal(["VALORANT (example)"], s.Launched);
        vm.UndoCommand.Execute(null);
        Assert.Equal(["op1"], s.Undone);
        Assert.Null(vm.LastOperation);
    }

    [Fact]
    public async Task Home_Boost_GameRunning_ShowsError_NoLaunchPrompt()
    {
        var s = Services();
        s.NextResult = new BoostResult("op2", false, BoostOrchestrator.GameRunningError, [], null);
        var vm = new HomeViewModel(s);
        await vm.BoostCommand.ExecuteAsync(null);
        Assert.True(vm.StatusIsError);
        Assert.False(vm.AskLaunch);
        Assert.Equal(Loc.I["boost.gameRunning"], vm.Status);
    }

    [Fact]
    public async Task Home_Boost_ConfigNotFound_ExplainsLaunchOnce()
    {
        var s = Services();
        s.NextResult = new BoostResult("op3", true, null, [], new ApplyReport("op3", "example-valorant", BoostLevel.Competitive, [new TargetResult("gus", null, TargetStatus.NotFound, 0, null)], true, false));
        var vm = new HomeViewModel(s);
        await vm.BoostCommand.ExecuteAsync(null);
        Assert.Equal(Loc.I["boost.notFound"], vm.Status);
        Assert.False(vm.AskLaunch);
    }

    [Fact]
    public void Settings_ChangeLanguage_SavesAndSwitches()
    {
        var s = Services();
        AppSettings? notified = null;
        var vm = new SettingsViewModel(s, x => notified = x);
        vm.Language = "en";
        Assert.Equal("en", s.Saved.Language);
        Assert.Equal("Settings", Loc.I["nav.settings"]);
        vm.CloseList = "chrome, spotify";
        Assert.Equal(["chrome", "spotify"], notified!.CloseList);
        vm.Language = "tr";
    }

    [Fact]
    public async Task Scan_ShowsScore_Parts_AndIssues()
    {
        var vm = new ScanViewModel(Services());
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.HasResult);
        Assert.Equal(56, vm.Score);
        Assert.Equal(6, vm.Parts.Count);
        Assert.False(vm.Parts.Single(p => p.Label == "RAM").Ok);
        Assert.Single(vm.Issues);
    }

    [Fact]
    public async Task Net_Run_FillsPingsSpeedAndAdvice()
    {
        var vm = new NetViewModel(Services());
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Equal(NetAnalysis.DefaultServers.Count, vm.Pings.Count);
        Assert.Equal("95.5 Mbps", vm.Download.Replace(',', '.'));
        Assert.Single(vm.Advice);
    }

    [Fact]
    public async Task Tools_Load_Clean_ToggleStartup()
    {
        var s = Services();
        var vm = new ToolsViewModel(s);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal("5 MB", vm.Junk.Single().Size.Replace(',', '.'));
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.Equal(5 << 20, s.Cleaned);
        vm.Startup.Single().Enabled = false;
        Assert.Equal([("Spotify", false)], s.StartupChanges);
    }

    [Fact]
    public void Main_Navigate_SwitchesPage()
    {
        var vm = new MainViewModel(Services());
        vm.Navigate(vm.Nav.Single(n => n.Key == "settings"));
        Assert.IsType<SettingsViewModel>(vm.CurrentPage);
        Assert.True(vm.Nav.Single(n => n.Key == "settings").IsActive);
        Assert.False(vm.Nav[0].IsActive);
    }
}
