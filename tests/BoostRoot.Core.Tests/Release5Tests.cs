using BoostRoot.Core.Fps;
using BoostRoot.Core.Ui;

namespace BoostRoot.Core.Tests;

public class Release5Tests
{
    [Fact]
    public void RateMeter_MeasuresThousandHz()
    {
        var m = new RateMeter(0.5);
        for (var i = 0; i < 2000; i++) m.Add(i / 1000.0);
        Assert.InRange(m.Rate, 990, 1010);
        Assert.InRange(m.Peak, 990, 1010);
        m.Reset();
        Assert.Equal(0, m.Rate);
    }

    [Theory]
    [InlineData(59.94, 60)]
    [InlineData(143.6, 144)]
    [InlineData(239.2, 240)]
    [InlineData(110, 110)]
    public void SnapHz_RoundsToCommonRates(double hz, int expected) => Assert.Equal(expected, RateMeter.SnapHz(hz));

    [Fact]
    public void Downsample_KeepsWorstFramePerBucket()
    {
        var ms = Enumerable.Repeat(7.0, 3000).ToArray();
        ms[1234] = 80;
        var d = FrameTimeline.Downsample(ms, 300);
        Assert.Equal(300, d.Count);
        Assert.Equal(80, d.Max());
        Assert.Equal(100, FrameTimeline.Ceiling(d));
        Assert.Equal(10, FrameTimeline.Ceiling([7.0, 8.3]));
    }

    [Fact]
    public void LatestPerGame_SortsByFps()
    {
        FrameStats S(double fps) => new(100, 1, fps, fps / 2, fps / 3, 1000 / fps, 0);
        var t = DateTime.UtcNow;
        var runs = new[]
        {
            new FpsRun("a", "A", t, "before", S(100)), new FpsRun("a", "A", t.AddMinutes(1), "after", S(140)),
            new FpsRun("b", "B", t, "auto", S(200)),
        };
        var r = FrameTimeline.LatestPerGame(runs);
        Assert.Equal(["B", "A"], r.Select(x => x.GameName));
        Assert.Equal(140, r[1].Stats.AvgFps);
    }
}

public class AutoBoostTests
{
    private static Profiles.GameProfile Profile(string id, string proc) =>
        Profiles.ProfileLoader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "_example.json"))) is var p
            ? p with { Id = id, Name = id, Detect = new Dictionary<string, string> { ["process"] = proc } } : null!;

    [Fact]
    public void ReportsGameOncePerRun_AndRearmsAfterClose()
    {
        var game = new BoostRoot.Games.InstalledGame("steam", "x", "X", "C:/x", new Dictionary<string, string>());
        var cs = new BoostRoot.Games.CatalogEntry(Profile("cs2", "cs2.exe"), game);
        var notInstalled = new BoostRoot.Games.CatalogEntry(Profile("val", "VALORANT-Win64-Shipping.exe"), null);
        var auto = new ViewModels.AutoBoost();
        BoostRoot.Games.CatalogEntry[] all = [cs, notInstalled];
        Assert.Null(auto.Check(all, ["chrome", "VALORANT-Win64-Shipping"]));
        Assert.Same(cs, auto.Check(all, ["CS2"]));
        Assert.Null(auto.Check(all, ["cs2"]));      // still running → not again
        Assert.Null(auto.Check(all, ["chrome"]));   // closed → re-armed
        Assert.Same(cs, auto.Check(all, ["cs2.exe"]));
        auto.MarkHandled("cs2");
        Assert.Null(auto.Check(all, ["cs2"]));
    }
}

public class AutoBoostPathTests
{
    [Fact]
    public void LibraryGameWithoutExeName_IsFoundByInstallFolder()
    {
        var prof = BoostRoot.Core.Profiles.ProfileLoader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "_example.json")))
            with { Id = "lib-gtfo", Name = "GTFO", Detect = new Dictionary<string, string>() };
        var game = new BoostRoot.Games.InstalledGame("steam", "493520", "GTFO", @"D:\SteamLibrary\steamapps\common\GTFO", new Dictionary<string, string>());
        var e = new BoostRoot.Games.CatalogEntry(prof, game);
        var auto = new ViewModels.AutoBoost();
        Assert.Null(auto.Check([e], ["GTFO"], [@"C:\Windows\explorer.exe"]));
        Assert.Same(e, auto.Check([e], ["GTFO"], [@"d:\steamlibrary\steamapps\common\gtfo\GTFO.exe"]));
        Assert.False(ViewModels.AutoBoost.RunsFrom(@"C:\", [@"C:\Windows\explorer.exe"]));
        Assert.False(ViewModels.AutoBoost.RunsFrom(@"D:\Games", [@"D:\Games\x\a.exe"]));
        Assert.False(ViewModels.AutoBoost.RunsFrom(@"D:\Games\X", [@"D:\Games\X\CrashReporter.exe"]));
    }
}
