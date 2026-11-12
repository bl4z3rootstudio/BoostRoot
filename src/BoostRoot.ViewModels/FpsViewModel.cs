using System.Collections.ObjectModel;
using BoostRoot.Core.Fps;
using BoostRoot.Games;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed record FpsHistoryRow(string When, string Game, string Label, string Avg, string Low1);

public sealed partial class FpsViewModel : ObservableObject
{
    private readonly IAppServices _services;
    public ObservableCollection<CatalogEntry> Games { get; } = [];
    public ObservableCollection<MinutesChoice> Durations { get; } = [new(30, "fps.s30"), new(60, "fps.s60") { IsSelected = true }, new(120, "fps.s120")];
    public ObservableCollection<FpsHistoryRow> History { get; } = [];

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(MeasureCommand))] public partial CatalogEntry? Selected { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(MeasureCommand), nameof(CancelCommand))] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial string? Phase { get; set; }
    [ObservableProperty] public partial string LiveFps { get; set; } = "—";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial FrameStats? Before { get; set; }
    [ObservableProperty] public partial FrameStats? After { get; set; }
    [ObservableProperty] public partial string? GainText { get; set; }
    [ObservableProperty] public partial bool GainPositive { get; set; }
    [ObservableProperty] public partial string? SavedPath { get; set; }

    public string Cpu { get; set; } = "";
    public string Gpu { get; set; } = "";
    private CancellationTokenSource? _cts;

    public FpsViewModel(IAppServices services)
    {
        _services = services;
        Reload();
    }

    public void Reload()
    {
        Games.Clear();
        foreach (var g in _services.LoadCatalog().Where(e => e.IsInstalled && e.Profile is not null)) Games.Add(g);
        Selected ??= Games.FirstOrDefault();
        LoadHistory();
    }

    partial void OnSelectedChanged(CatalogEntry? value) => LoadComparison();

    [RelayCommand] public void ChooseDuration(MinutesChoice d) { foreach (var x in Durations) x.IsSelected = x == d; }

    private void LoadHistory()
    {
        History.Clear();
        foreach (var r in _services.FpsHistory.All().TakeLast(20).Reverse())
            History.Add(new(r.At.ToLocalTime().ToString("dd.MM HH:mm"), r.GameName, Loc.I["fps." + r.Label], $"{r.Stats.AvgFps:0}", $"{r.Stats.Low1:0}"));
        LoadComparison();
    }

    private void LoadComparison()
    {
        Before = After = null; GainText = null;
        if (Selected?.Profile is not { } p) return;
        var runs = _services.FpsHistory.All().Where(r => r.GameId == p.Id).ToList();
        Before = runs.LastOrDefault(r => r.Label == FpsHistory.Before)?.Stats;
        if (_services.FpsHistory.Comparison(p.Id) is { } c)
        {
            After = c.After.Stats;
            Before = c.Before.Stats;
            var g = FrameStats.Gain(c.Before.Stats, c.After.Stats);
            GainPositive = g >= 0;
            GainText = Loc.I.Format("fps.gain", (g >= 0 ? "+" : "") + g.ToString("0.#"));
        }
    }

    private bool CanMeasure() => Selected is not null && !IsRunning;

    /// <param name="label">"before" or "after"</param>
    [RelayCommand(CanExecute = nameof(CanMeasure))]
    public Task MeasureAsync(string label) => RunAsync(label, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(Durations.First(d => d.IsSelected).Value));

    public async Task RunAsync(string label, TimeSpan countdown, TimeSpan duration)
    {
        var entry = Selected!;
        IsRunning = true;
        Error = null; SavedPath = null;
        _cts = new CancellationTokenSource();
        var frames = new List<double>();
        var rolling = new RollingFps();
        var capturing = false;
        using var src = _services.StartFrames(entry);
        if (src.Error is { } err) { Error = Loc.I["fps.err." + err]; IsRunning = false; return; }
        src.Frame += ms => { rolling.Add(ms); if (capturing) lock (frames) frames.Add(ms); };
        try
        {
            for (var t = countdown; t > TimeSpan.Zero; t -= TimeSpan.FromSeconds(1))
            {
                Phase = Loc.I.Format("fps.countdown", (int)Math.Ceiling(t.TotalSeconds));
                LiveFps = rolling.Fps is { } f0 ? $"{f0:0}" : "—";
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(1, t.TotalSeconds)), _cts.Token);
            }
            capturing = true;
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < duration)
            {
                Phase = Loc.I["fps.measuring"];
                Progress = (DateTime.UtcNow - start) / duration;
                LiveFps = rolling.Fps is { } f ? $"{f:0}" : "—";
                await Task.Delay(Math.Min(250, (int)duration.TotalMilliseconds), _cts.Token);
            }
            capturing = false;
            List<double> copy;
            lock (frames) copy = [.. frames];
            if (FrameStats.From(copy) is not { } stats) { Error = Loc.I["fps.err.noFrames"]; return; }
            _services.FpsHistory.Add(new FpsRun(entry.Profile!.Id, entry.DisplayName, DateTime.UtcNow, label, stats));
            LoadHistory();
            Phase = Loc.I["fps.done"];
        }
        catch (OperationCanceledException) { Phase = Loc.I["fps.cancelled"]; }
        finally
        {
            Progress = 0;
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private bool CanCancel() => IsRunning;
    [RelayCommand(CanExecute = nameof(CanCancel))] public void Cancel() => _cts?.Cancel();

    [RelayCommand]
    public void Share(string format)
    {
        if (Before is null || After is null || Selected is null) return;
        SavedPath = _services.SaveShareCard(new ShareCard(Selected.DisplayName, Before, After, FrameStats.Gain(Before, After), Cpu, Gpu), format == "story");
        if (SavedPath is not null) _services.OpenUrl(SavedPath);
    }
}
