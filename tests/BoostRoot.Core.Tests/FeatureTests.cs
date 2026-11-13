using BoostRoot.Core.Net;
using BoostRoot.Core.Profiles;
using BoostRoot.ViewModels;

namespace BoostRoot.Core.Tests;

public class FeatureTests
{
    [Fact]
    public void Dns_QueryPacket_IsValid()
    {
        var q = DnsBenchmark.Query("www.google.com", 0x1234);
        Assert.Equal([0x12, 0x34, 0x01, 0x00, 0, 1], q[..6]);
        Assert.Equal(3, q[12]);
        Assert.Equal([0, 0, 1, 0, 1], q[^5..]);
        Assert.True(DnsBenchmark.IsAnswer([0x12, 0x34, 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0], 0x1234));
        Assert.False(DnsBenchmark.IsAnswer([0x12, 0x35, 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0], 0x1234));
    }

    [Fact]
    public async Task Net_DnsBenchmark_RanksAndAdvises()
    {
        var vm = new NetViewModel(ViewModelTests.Services());
        await vm.RunDnsCommand.ExecuteAsync(null);
        Assert.Equal("1.1.1.1", vm.Dns[0].Address);
        Assert.True(vm.Dns[0].Best);
        Assert.Contains("Cloudflare", vm.DnsAdvice);
    }

    [Fact]
    public void ShareCode_RoundTrips()
    {
        var c = ShareCode.Encode(new BoostShare("valorant", BoostLevel.Competitive, true, "0;P;c;5"));
        Assert.StartsWith("BR1-", c);
        Assert.Equal(new BoostShare("valorant", BoostLevel.Competitive, true, "0;P;c;5"), ShareCode.Decode(c));
        Assert.Null(ShareCode.Decode("BR1-!!!"));
        Assert.Null(ShareCode.Decode("hello"));
    }

    [Fact]
    public void Home_FavoritesShareAndImport()
    {
        var svc = ViewModelTests.Services();
        var main = new MainViewModel(svc);
        var home = main.Home;
        var card = home.Games.First(g => g.IsInstalled);
        home.ToggleFavorite(card);
        Assert.True(svc.Saved.IsFavorite(card.Entry.Profile!.Id));
        Assert.Same(card, home.Games[0]);
        home.Select(card);
        home.ChooseLevel(home.Levels.First(l => l.Level == BoostLevel.Quality));
        home.Share();
        Assert.StartsWith("BR1-", svc.Clipboard);
        home.ChooseLevel(home.Levels.First(l => l.Level == BoostLevel.Competitive));
        home.ShareInput = svc.Clipboard;
        home.ImportShare();
        Assert.Equal(BoostLevel.Quality, home.SelectedLevel);
    }

    [Fact]
    public void Settings_AutoUndoAndAppPicker()
    {
        var svc = ViewModelTests.Services();
        var main = new MainViewModel(svc);
        main.Settings.AutoUndo = true;
        Assert.True(svc.Saved.AutoUndo);
        main.Settings.LoadRunningApps();
        var discord = main.Settings.AppChoices.First(a => a.Process == "Discord");
        Assert.True(discord.IsRunning);
        Assert.Same(discord, main.Settings.AppChoices[0].IsRunning ? main.Settings.AppChoices.First(a => a.Process == "Discord") : null);
        discord.IsChecked = true;
        Assert.Contains("Discord", svc.Saved.CloseList);
        main.Settings.AppChoices.First(a => a.Process == "chrome").IsChecked = false;
        Assert.DoesNotContain("chrome", svc.Saved.CloseList);
    }

    [Fact]
    public async Task Tools_SizesAndBenchmarkScore()
    {
        var vm = new ToolsViewModel(ViewModelTests.Services());
        await vm.LoadSizesCommand.ExecuteAsync(null);
        Assert.Equal("VALORANT", vm.Sizes[0].Name);
        Assert.Equal(1, vm.Sizes[0].Share);
        Assert.True(StressViewModel.BenchmarkScore(1000, 200) > StressViewModel.BenchmarkScore(900, 150));
        var svc = ViewModelTests.Services();
        var s = new StressViewModel(svc);
        await s.RunBenchmarkAsync(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(50));
        Assert.NotNull(svc.LastBenchmark);
        Assert.Contains(svc.LastBenchmark!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), s.BenchText);
    }

    [Fact]
    public void Steps_ExplainGroupBGames()
    {
        var p = RealProfileTests.Valorant with { Group = "B", Configs = [] };
        var r = new BoostRoot.Core.Boost.BoostResult("op", true, null, [new("game-settings", BoostRoot.Core.Boost.StepStatus.Done), new("windows", BoostRoot.Core.Boost.StepStatus.Done, null, 3)], null);
        var rows = HomeViewModel.Describe(r, p).ToList();
        Assert.Contains(rows, x => x.Text.Contains("oyun içinden", StringComparison.Ordinal) || x.Text.Contains("in game", StringComparison.Ordinal));
        Assert.Contains(rows, x => x.Text.Contains('3'));
    }
}

public class MonitorTests
{
    [Fact]
    public async Task Monitor_TicksChecksAndFixes()
    {
        var svc = ViewModelTests.Services();
        var vm = new MonitorViewModel(svc, () => AppSettings.Default);
        vm.Tick();
        Assert.Single(vm.CpuSeries);
        Assert.Equal(2, vm.Processes.Count);
        vm.RunChecks();
        Assert.False(vm.AllOk);
        await vm.FixAllCommand.ExecuteAsync(null);
        Assert.Contains(10, svc.Killed);
        Assert.True(vm.AllOk);
        vm.Kill(vm.Processes[1]);
        Assert.Contains(11, svc.Killed);
    }
}
