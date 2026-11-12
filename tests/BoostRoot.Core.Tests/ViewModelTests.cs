using BoostRoot.Core.Boost;
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
    public void Main_Navigate_SwitchesPage()
    {
        var vm = new MainViewModel(Services());
        vm.Navigate(vm.Nav.Single(n => n.Key == "settings"));
        Assert.IsType<SettingsViewModel>(vm.CurrentPage);
        Assert.True(vm.Nav.Single(n => n.Key == "settings").IsActive);
        Assert.False(vm.Nav[0].IsActive);
    }
}
