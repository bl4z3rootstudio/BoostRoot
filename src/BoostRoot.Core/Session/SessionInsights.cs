using BoostRoot.Core.Fps;
using BoostRoot.Core.Monitoring;
using BoostRoot.Core.Profiles;

namespace BoostRoot.Core.Session;

/// <summary>Guesses why a stutter happened from the sensor snapshot taken right after it. Cheap: no extra polling.</summary>
public static class StutterAnalyzer
{
    public const string GpuHot = "gpu-hot", CpuHot = "cpu-hot", CpuThrottled = "cpu-throttle", RamFull = "ram-full", CpuFull = "cpu-full", GpuFull = "gpu-full", Background = "background";

    public static string Cause(SensorSnapshot s) =>
        s.GpuTempC >= 85 || s.GpuHotspotC >= 100 ? GpuHot
        : s.CpuTempC >= 90 ? CpuHot
        : s.CpuThrottled ? CpuThrottled
        : s.RamUsedPct >= 90 ? RamFull
        : s.CpuLoad >= 95 ? CpuFull
        : s.GpuLoad >= 98 ? GpuFull
        : Background;

    public static Localized Describe(string cause) => cause switch
    {
        GpuHot => new("Ekran kartı çok ısındı: kasanın havalandırmasını ve fanları kontrol et.", "The graphics card got too hot: check case airflow and fans."),
        CpuHot => new("İşlemci çok ısındı: soğutucuyu ve termal macunu kontrol et.", "The CPU got too hot: check the cooler and thermal paste."),
        CpuThrottled => new("İşlemci hızını düşürdü (güç/ısı sınırı): güç planını Yüksek performans yap.", "The CPU slowed down (power/heat limit): use the High performance power plan."),
        RamFull => new("RAM doldu: oyundan önce tarayıcı gibi programları kapat.", "RAM was full: close browsers and other apps before playing."),
        CpuFull => new("İşlemci %100 doldu: arka plandaki programları kapat ya da FPS sınırı koy.", "The CPU hit 100%: close background apps or set an FPS cap."),
        GpuFull => new("Ekran kartı tam yükte: grafik ayarlarını bir kademe düşür ya da FPS sınırı koy.", "The graphics card was maxed out: lower graphics a notch or set an FPS cap."),
        _ => new("Belirgin bir donanım sebebi yok: arka plandaki bir program ya da internet olabilir.", "No clear hardware cause: probably a background app or the network."),
    };
}

/// <summary>What the end-of-game report shows.</summary>
public sealed record SessionSummary(string Game, TimeSpan Duration, FrameStats? Stats, int Stutters, string? MainCause, double? MaxGpuTemp, double? MaxCpuTemp)
{
    public static string MostCommon(IEnumerable<string> causes) =>
        causes.GroupBy(c => c).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? StutterAnalyzer.Background;

    /// <summary>A short multi-line report for the overlay toast; <paramref name="pick"/> chooses the language.</summary>
    public string Text(Func<Localized, string> pick)
    {
        var lines = new List<string> { $"{Game} · {(int)Duration.TotalMinutes} min" };
        if (Stats is { } s) lines.Add(pick(new($"Ortalama {s.AvgFps:0} FPS, en düşük %1 {s.Low1:0} FPS", $"Average {s.AvgFps:0} FPS, 1% low {s.Low1:0} FPS")));
        if (MaxGpuTemp is { } g) lines.Add(pick(new($"En yüksek ekran kartı sıcaklığı {g:0}°C", $"Max GPU temperature {g:0}°C")));
        lines.Add(Stutters == 0 ? pick(new("Takılma yok", "No stutters")) : pick(new($"{Stutters} takılma", $"{Stutters} stutters")));
        if (Stutters > 0 && MainCause is { } c) lines.Add(pick(StutterAnalyzer.Describe(c)));
        return string.Join("\n", lines);
    }
}

public sealed record GameStatRow(string GameId, string GameName, int Sessions, TimeSpan Total, double? AvgFps, double? BestLow1, DateTime Last);

/// <summary>All-time numbers per game from the playtime log and the automatic FPS runs.</summary>
public static class GameStats
{
    public static IReadOnlyList<GameStatRow> Build(IEnumerable<PlaySession> sessions, IEnumerable<FpsRun> runs)
    {
        var auto = runs.Where(r => r.Label == "auto").GroupBy(r => r.GameId).ToDictionary(g => g.Key, g => g.ToList());
        return [.. sessions.GroupBy(s => s.GameId).Select(g =>
        {
            auto.TryGetValue(g.Key, out var fr);
            return new GameStatRow(g.Key, g.OrderBy(s => s.End).Last().GameName, g.Count(), g.Aggregate(TimeSpan.Zero, (a, s) => a + s.Length),
                fr is { Count: > 0 } ? Math.Round(fr.Average(r => r.Stats.AvgFps), 0) : null,
                fr is { Count: > 0 } ? fr.Max(r => r.Stats.Low1) : null,
                g.Max(s => s.End));
        }).OrderByDescending(r => r.Total)];
    }

    /// <summary>Average FPS of the most recent automatic runs, oldest first (for the chart).</summary>
    public static IReadOnlyList<FpsRun> Recent(IEnumerable<FpsRun> runs, int count = 20) =>
        [.. runs.Where(r => r.Label == "auto").OrderByDescending(r => r.At).Take(count).Reverse()];

    /// <summary>Suggested FPS cap: a few frames above the monitor refresh rate keeps input lag low and the PC cooler.</summary>
    public static int SuggestedCap(int monitorHz) => monitorHz <= 0 ? 0 : monitorHz + Math.Max(3, monitorHz / 20);
}
