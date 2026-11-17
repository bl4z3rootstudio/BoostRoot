using BoostRoot.Core.Fps;
using BoostRoot.Core.Monitoring;
using BoostRoot.Core.Session;
using BoostRoot.ViewModels;

namespace BoostRoot.Core.Tests;

public class Release26_11_12Tests
{
    [Fact]
    public void SettingsCode_RoundTripsGameSettings_AndRejectsGarbage()
    {
        var s = AppSettings.Default with { FpsCap = 165, OverlayColor = "#22D3EE", OverlayScale = 130, PauseDownloads = true, SessionReport = false, OverlayMetrics = ["fps", "ping"] };
        var code = SettingsCode.Encode(s);
        Assert.StartsWith("BR1-", code);
        var back = SettingsCode.Decode(code, AppSettings.Default)!;
        Assert.Equal(165, back.FpsCap);
        Assert.Equal("#22D3EE", back.OverlayColor);
        Assert.Equal(130, back.OverlayScale);
        Assert.True(back.PauseDownloads);
        Assert.False(back.SessionReport);
        Assert.Equal(["fps", "ping"], back.Metrics);
        Assert.Null(SettingsCode.Decode("hello", AppSettings.Default));
        Assert.Null(SettingsCode.Decode("BR1-!!!", AppSettings.Default));
    }

    [Fact]
    public void Stutter_CauseFollowsTheWorstSensor()
    {
        Assert.Equal(StutterAnalyzer.GpuHot, StutterAnalyzer.Cause(new SensorSnapshot(DateTime.UtcNow, GpuTempC: 88)));
        Assert.Equal(StutterAnalyzer.RamFull, StutterAnalyzer.Cause(new SensorSnapshot(DateTime.UtcNow, RamUsedPct: 95, GpuTempC: 60)));
        Assert.Equal(StutterAnalyzer.Background, StutterAnalyzer.Cause(new SensorSnapshot(DateTime.UtcNow, CpuLoad: 40)));
        Assert.Equal("ram-full", SessionSummary.MostCommon(["ram-full", "gpu-hot", "ram-full"]));
    }

    [Fact]
    public void GameStats_SumsPlaytimeAndAveragesAutoRuns()
    {
        var t = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc);
        PlaySession[] sessions = [new("val", "VALORANT", t, t.AddHours(1)), new("val", "VALORANT", t.AddDays(1), t.AddDays(1).AddMinutes(30)), new("cs2", "CS2", t, t.AddMinutes(20))];
        FpsRun[] runs = [new("val", "VALORANT", t, "auto", new(1000, 10, 200, 120, 90, 5, 0)), new("val", "VALORANT", t.AddDays(1), "auto", new(1000, 10, 240, 150, 90, 4, 0)),
            new("val", "VALORANT", t, "before", new(1000, 10, 50, 20, 10, 20, 0))];
        var rows = GameStats.Build(sessions, runs);
        Assert.Equal("val", rows[0].GameId);
        Assert.Equal(2, rows[0].Sessions);
        Assert.Equal(TimeSpan.FromMinutes(90), rows[0].Total);
        Assert.Equal(220, rows[0].AvgFps);
        Assert.Equal(150, rows[0].BestLow1);
        Assert.Null(rows[1].AvgFps);
        Assert.Equal(2, GameStats.Recent(runs).Count);
        Assert.Equal(151, GameStats.SuggestedCap(144));
        Assert.Equal(0, GameStats.SuggestedCap(0));
    }

    [Fact]
    public void SessionReport_TextMentionsFpsAndCause()
    {
        var r = new SessionSummary("VALORANT", TimeSpan.FromMinutes(42), new FrameStats(1000, 10, 230, 140, 90, 4, 3), 3, StutterAnalyzer.RamFull, 71, 80);
        var en = r.Text(l => l.En);
        Assert.Contains("230 FPS", en);
        Assert.Contains("3 stutters", en);
        Assert.Contains("RAM", en);
    }

    [Fact]
    public void NvidiaFpsCap_IsWrittenOnlyWhenSet()
    {
        var on = BoostRoot.Core.Gpu.NvidiaSettings.From(new BoostRoot.Core.Profiles.GpuPrefs(FpsCap: 165));
        Assert.Equal(165u, on[BoostRoot.Core.Gpu.NvidiaSettings.FrameRateLimit]);
        Assert.False(BoostRoot.Core.Gpu.NvidiaSettings.From(new BoostRoot.Core.Profiles.GpuPrefs()).ContainsKey(BoostRoot.Core.Gpu.NvidiaSettings.FrameRateLimit));
    }

    [Fact]
    public void ManualGame_LaunchesItsExe_AndSurvivesSettingsCode()
    {
        var g = new BoostRoot.Games.InstalledGame("manual", "manual-mygame", "My Game", @"C:\Games\My", new Dictionary<string, string> { ["exe"] = @"C:\Games\My\game.exe" });
        Assert.Equal(@"C:\Games\My\game.exe", BoostRoot.Games.LaunchCommands.For(g)!.FileName);
        var s = AppSettings.Default with { ManualGames = [new("My Game", @"C:\Games\My\game.exe")] };
        Assert.Single(SettingsCode.Decode(SettingsCode.Encode(s), s)!.ManualGames!); // code leaves personal paths alone
    }
}
