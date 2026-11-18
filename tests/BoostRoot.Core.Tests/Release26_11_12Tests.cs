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
    public void SettingsCode_CarriesBoostChoices_PerGameLevels_AndCrosshair()
    {
        var s = AppSettings.Default with
        {
            PureBoost = true, EcoBackground = true, NetBoost = true, Theme = "oled", OverlayCorner = "tc",
            GameLevels = new() { ["valorant"] = "max", ["cs2"] = "balanced" }, StretchGames = ["valorant"], LastGameId = "valorant",
            CrosshairDesign = new BoostRoot.Core.Crosshair.CrosshairDesign("x", "#00FFFF", Inner: new(true, 1, 2, 0, 1)),
        };
        var back = SettingsCode.Decode(SettingsCode.Encode(s), AppSettings.Default with { PureBoost = false })!;
        Assert.True(back.PureBoost);
        Assert.True(back.EcoBackground);
        Assert.True(back.NetBoost);
        Assert.Equal("oled", back.Theme);
        Assert.Equal("tc", back.OverlayCorner);
        Assert.Equal("max", back.GameLevels!["valorant"]);
        Assert.Equal("balanced", back.GameLevels["cs2"]);
        Assert.True(back.IsStretched("valorant"));
        Assert.Equal("valorant", back.LastGameId);
        Assert.Equal("#00FFFF", back.CrosshairDesign!.Color);
        Assert.Equal(2, back.CrosshairDesign.InnerLines.Length);
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

public class GameDoctorTests
{
    private static BoostRoot.Core.Tools.DoctorFacts Healthy(string source = "steam") =>
        new("Game", source, true, true, true, false, 100, 100, true, null, false, false, false, false, 30, false);

    [Fact]
    public void HealthyGame_SaysAllGood_PlusOptionalVerify()
    {
        var f = BoostRoot.Core.Tools.GameDoctor.Evaluate(Healthy());
        Assert.Equal("all-good", f[0].Id);
        Assert.Contains(f, x => x.Id == "steam-verify" && x.CanAutoFix);
    }

    [Fact]
    public void Problems_HaveAFixOrManualSteps()
    {
        var facts = Healthy("riot") with { VcRedistInstalled = false, VanguardRunning = false, RiotClientBroken = true, GameRunningButNoWindow = true, HasCompatFlags = true, DriverAgeDays = 400, PendingReboot = true, SystemDiskFreeGb = 3 };
        var f = BoostRoot.Core.Tools.GameDoctor.Evaluate(facts);
        Assert.DoesNotContain(f, x => x.Id == "all-good");
        foreach (var id in new[] { "vcredist", "vanguard", "riot-client", "stuck-process", "compat-flags", "driver-old", "reboot", "disk-space" }) Assert.Contains(f, x => x.Id == id);
        Assert.All(f, x => Assert.True(x.CanAutoFix || x.Manual is not null, x.Id));
    }

    [Fact]
    public void Asar_DetectsProtobufInMainJs()
    {
        static MemoryStream Asar(string mainJs)
        {
            var body = System.Text.Encoding.UTF8.GetBytes(mainJs);
            var json = System.Text.Encoding.UTF8.GetBytes("{\"files\":{\"dist\":{\"files\":{\"main\":{\"files\":{\"main.js\":{\"size\":" + body.Length + ",\"offset\":\"0\"}}}}}}}");
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(4); w.Write(json.Length + 8); w.Write(json.Length + 4); w.Write(json.Length);
            w.Write(json); w.Write(body);
            ms.Position = 0;
            return ms;
        }
        Assert.True(BoostRoot.Core.Tools.AsarProbe.MainJsLooksBroken(Asar("option optimize_for = LITE_RUNTIME;\nmessage X {}")));
        Assert.False(BoostRoot.Core.Tools.AsarProbe.MainJsLooksBroken(Asar("\"use strict\";const a=require('electron');")));
    }
}

public class WeeklyReportTests
{
    [Fact]
    public void Week_SumsLastSevenDays_AndPicksTopGame()
    {
        var now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        PlaySession[] s = [new("v", "VALORANT", now.AddDays(-1), now.AddDays(-1).AddHours(2)), new("c", "CS2", now.AddDays(-2), now.AddDays(-2).AddMinutes(30)),
            new("v", "VALORANT", now.AddDays(-10), now.AddDays(-10).AddHours(1))];
        FpsRun[] r = [new("v", "VALORANT", now.AddDays(-1), "auto", new(1000, 10, 200, 120, 90, 5, 0)), new("v", "VALORANT", now.AddDays(-9), "auto", new(1000, 10, 100, 60, 50, 9, 0))];
        var w = BoostRoot.Core.Session.WeeklyReport.Build(s, r, now);
        Assert.Equal(TimeSpan.FromMinutes(150), w.Played);
        Assert.Equal(2, w.Sessions);
        Assert.Equal("VALORANT", w.TopGame);
        Assert.Equal(200, w.AvgFps);
        Assert.Equal(TimeSpan.FromHours(1), w.PreviousWeek);
    }
}

public class FpsEstimatorTests
{
    [Fact]
    public void GpuNames_MatchTheRightModel()
    {
        Assert.Equal(115, BoostRoot.Core.Fps.FpsEstimator.GpuScore("NVIDIA GeForce RTX 4060"));
        Assert.Equal(140, BoostRoot.Core.Fps.FpsEstimator.GpuScore("NVIDIA GeForce RTX 4060 Ti"));
        Assert.Equal(110, BoostRoot.Core.Fps.FpsEstimator.GpuScore("AMD Radeon RX 6600 XT"));
        Assert.Equal(270, BoostRoot.Core.Fps.FpsEstimator.GpuScore("AMD Radeon RX 9070 XT"));
        Assert.Null(BoostRoot.Core.Fps.FpsEstimator.GpuScore("Microsoft Basic Display Adapter"));
    }

    [Fact]
    public void Estimate_IsARange_AndRespectsCaps()
    {
        var v = BoostRoot.Core.Fps.FpsEstimator.Estimate("valorant", "AMD Radeon RX 6700 XT", "Intel(R) Core(TM) i5-12400F")!;
        Assert.True(v.Low < v.High);
        Assert.Equal("cpu", v.Limit);
        var e = BoostRoot.Core.Fps.FpsEstimator.Estimate("elden-ring", "NVIDIA GeForce RTX 4090", "AMD Ryzen 7 7800X3D 8-Core Processor")!;
        Assert.Equal(60, e.High);
        Assert.Null(BoostRoot.Core.Fps.FpsEstimator.Estimate("unknown-game", "NVIDIA GeForce RTX 4060", "x"));
    }
}
