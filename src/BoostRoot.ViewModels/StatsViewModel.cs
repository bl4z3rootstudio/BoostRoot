using BoostRoot.Core.Fps;
using BoostRoot.Core.Session;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed record StatRowViewModel(string Game, string Sessions, string Time, string AvgFps, string BestLow);

/// <summary>One bar of the "average FPS per session" chart; Height is 0..1.</summary>
public sealed record FpsBarViewModel(string Game, double Fps, double Height, string Label);

/// <summary>All-time numbers per game, a chart of recent sessions and the last end-of-game report. Reads two small local files.</summary>
public sealed partial class StatsViewModel(IAppServices services) : ObservableObject
{
    [ObservableProperty] public partial IReadOnlyList<StatRowViewModel> Rows { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<FpsBarViewModel> Bars { get; set; } = [];
    [ObservableProperty] public partial string TotalTime { get; set; } = "—";
    [ObservableProperty] public partial string TotalSessions { get; set; } = "—";
    [ObservableProperty] public partial string AvgFps { get; set; } = "—";
    [ObservableProperty] public partial string WeekTime { get; set; } = "—";
    [ObservableProperty] public partial string? LastReport { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; } = true;

    [RelayCommand]
    public void Refresh()
    {
        IReadOnlyList<PlaySession> sessions;
        IReadOnlyList<FpsRun> runs;
        try { sessions = services.Playtime.All(); runs = services.FpsHistory.All(); }
        catch (Exception) { sessions = []; runs = []; }
        var rows = GameStats.Build(sessions, runs);
        Rows = [.. rows.Select(r => new StatRowViewModel(r.GameName, r.Sessions.ToString(System.Globalization.CultureInfo.CurrentCulture),
            PlaytimeLog.Format(r.Total), r.AvgFps is { } a ? $"{a:0}" : "—", r.BestLow1 is { } l ? $"{l:0}" : "—"))];
        var total = rows.Aggregate(TimeSpan.Zero, (t, r) => t + r.Total);
        TotalTime = total > TimeSpan.Zero ? PlaytimeLog.Format(total) : "—";
        TotalSessions = rows.Sum(r => r.Sessions).ToString(System.Globalization.CultureInfo.CurrentCulture);
        var auto = runs.Where(r => r.Label == "auto").ToList();
        AvgFps = auto.Count > 0 ? $"{auto.Average(r => r.Stats.AvgFps):0}" : "—";
        var week = services.Playtime.Since(DateTime.UtcNow.AddDays(-7));
        WeekTime = week > TimeSpan.Zero ? PlaytimeLog.Format(week) : "—";
        var recent = GameStats.Recent(runs);
        var max = recent.Count > 0 ? recent.Max(r => r.Stats.AvgFps) : 1;
        Bars = [.. recent.Select(r => new FpsBarViewModel(r.GameName, r.Stats.AvgFps, Math.Max(0.04, r.Stats.AvgFps / Math.Max(1, max)), $"{r.Stats.AvgFps:0}"))];
        LastReport = services.LastSessionReport;
        IsEmpty = rows.Count == 0;
    }
}

