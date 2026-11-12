using BoostRoot.Core.Profiles;
using BoostRoot.Core.Session;
using BoostRoot.ViewModels;

namespace BoostRoot.Core.Tests;

public class SessionTests
{
    [Fact]
    public void Stretch_ComputesFourByThree() { Assert.Equal((1440, 1080), StretchPlan.For(1080)); Assert.Equal((1920, 1440), StretchPlan.For(1440)); Assert.Equal((1024, 768), StretchPlan.For(768)); }

    [Fact]
    public void Stretch_ValuesForValorantKeys()
    {
        var p = RealProfileTests.Valorant;
        var v = StretchPlan.Values(p, 1440, 1080);
        Assert.Equal("1440", v["gus:/Script/ShooterGame.ShooterGameUserSettings/ResolutionSizeX"]);
        Assert.Equal("1080", v["gus:/Script/ShooterGame.ShooterGameUserSettings/LastUserConfirmedResolutionSizeY"]);
        Assert.Equal("False", v["gus:/Script/ShooterGame.ShooterGameUserSettings/bShouldLetterbox"]);
        Assert.Equal("0", v["gus:/Script/ShooterGame.ShooterGameUserSettings/PreferredFullscreenMode"]);
        Assert.Equal(p.StretchKeys!.Count, v.Count);
    }

    [Fact]
    public void Watcher_WaitsRunsEndsOrTimesOut()
    {
        var running = false;
        var w = new GameWatcher(_ => running, ["game.exe"], TimeSpan.FromSeconds(10));
        Assert.Equal(GameWatcher.State.Waiting, w.Tick(TimeSpan.FromSeconds(2)));
        running = true;
        Assert.Equal(GameWatcher.State.Running, w.Tick(TimeSpan.FromSeconds(2)));
        running = false;
        Assert.Equal(GameWatcher.State.Ended, w.Tick(TimeSpan.FromSeconds(2)));
        var t = new GameWatcher(_ => false, ["x"], TimeSpan.FromSeconds(3));
        t.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal(GameWatcher.State.TimedOut, t.Tick(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Settings_KeepsAllFields_AndStretchToggleSaves()
    {
        var svc = ViewModelTests.Services();
        var main = new MainViewModel(svc);
        main.Settings.CrosshairEnabled = true;
        main.Settings.Metrics.First(m => m.Key == "vram").IsOn = true;
        main.Settings.ReplaySeconds = 60;
        Assert.True(svc.Saved.CrosshairOrDefault.Enabled);
        Assert.Contains("vram", svc.Saved.Metrics);
        Assert.Equal(60, svc.Saved.ClipsOrDefault.ReplaySeconds);
        main.Settings.Sounds = !main.Settings.Sounds; // unrelated change must not drop the others
        Assert.True(svc.Saved.CrosshairOrDefault.Enabled);
        main.Home.Select(main.Home.Games.First(g => g.IsInstalled));
        main.Home.IsStretched = true;
        Assert.True(svc.Saved.IsStretched(main.Home.Selected!.Entry.Profile!.Id));
        Assert.True(svc.Saved.CrosshairOrDefault.Enabled);
        main.Home.IsStretched = false;
        Assert.False(svc.Saved.IsStretched(main.Home.Selected!.Entry.Profile!.Id));
    }
}
