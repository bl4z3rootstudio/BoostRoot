using System.Collections.ObjectModel;
using BoostRoot.Core.Monitoring;
using BoostRoot.Core.Stress;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public partial class ChoiceViewModel<T>(T value, string key) : ObservableObject
{
    public T Value { get; } = value;
    public string Title => Loc.I[key];
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

public sealed class KindChoice(StressKind v, string key) : ChoiceViewModel<StressKind>(v, key);
public sealed class MinutesChoice(int v, string key) : ChoiceViewModel<int>(v, key);

public sealed partial class StressViewModel(IAppServices services) : ObservableObject
{
    public const int ChartPoints = 300;
    public ObservableCollection<KindChoice> Kinds { get; } =
        [new(StressKind.Cpu, "stress.cpu") { IsSelected = true }, new(StressKind.Gpu, "stress.gpu"), new(StressKind.Ram, "stress.ram"), new(StressKind.All, "stress.all")];
    public ObservableCollection<MinutesChoice> Presets { get; } =
        [new(5, "stress.min5") { IsSelected = true }, new(15, "stress.min15"), new(60, "stress.min60"), new(0, "stress.custom")];

    public ObservableCollection<double> TempSeries { get; } = [];
    public ObservableCollection<double> LoadSeries { get; } = [];
    public ObservableCollection<double> RateSeries { get; } = [];

    [ObservableProperty] public partial int CustomMinutes { get; set; } = 10;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial string Elapsed { get; set; } = "00:00";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string CpuText { get; set; } = "—";
    [ObservableProperty] public partial string GpuText { get; set; } = "—";
    [ObservableProperty] public partial string RamText { get; set; } = "—";
    [ObservableProperty] public partial string? ResultTitle { get; set; }
    [ObservableProperty] public partial string? ResultDetail { get; set; }
    [ObservableProperty] public partial bool ResultOk { get; set; }

    private CancellationTokenSource? _cts;
    private double _rateMax;

    [RelayCommand] public void ChooseKind(KindChoice k) { foreach (var x in Kinds) x.IsSelected = x == k; }
    [RelayCommand] public void ChoosePreset(MinutesChoice p) { foreach (var x in Presets) x.IsSelected = x == p; }

    public StressKind SelectedKind => Kinds.First(k => k.IsSelected).Value;
    public TimeSpan Duration => TimeSpan.FromMinutes(Presets.First(p => p.IsSelected).Value is var m && m > 0 ? m : Math.Clamp(CustomMinutes, 1, 600));

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartAsync() => await RunAsync(Duration, TimeSpan.FromSeconds(1));

    /// <summary>Exposed for tests (short duration + tick).</summary>
    public async Task RunAsync(TimeSpan duration, TimeSpan tick)
    {
        var kind = SelectedKind;
        var loads = new List<IStressWorkload>();
        if (kind.HasFlag(StressKind.Cpu)) loads.Add(new CpuStress(kind == StressKind.All ? Math.Max(1, Environment.ProcessorCount - 1) : null));
        if (kind.HasFlag(StressKind.Ram)) loads.Add(new RamStress(services.RamStressBytes()));
        if (kind.HasFlag(StressKind.Gpu) && services.CreateGpuStress() is { } gpu) loads.Add(gpu);
        if (loads.Count == 0) return;

        IsRunning = true;
        ResultTitle = ResultDetail = null;
        TempSeries.Clear(); LoadSeries.Clear(); RateSeries.Clear();
        _rateMax = 0;
        _cts = new CancellationTokenSource();
        var ui = SynchronizationContext.Current;
        var runner = new StressRunner(services.Sensors, StressLimits.Default);
        runner.Sample += s => { if (ui is null) OnSample(s, duration); else ui.Post(_ => OnSample(s, duration), null); };
        try
        {
            var r = await runner.RunAsync(loads, duration, _cts.Token, tick);
            ShowResult(r);
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private bool CanStop() => IsRunning;
    [RelayCommand(CanExecute = nameof(CanStop))] public void Stop() => _cts?.Cancel();

    private static void Push(ObservableCollection<double> c, double v) { c.Add(v); if (c.Count > ChartPoints) c.RemoveAt(0); }

    private void OnSample(StressSample s, TimeSpan duration)
    {
        Elapsed = $"{(int)s.T.TotalMinutes:00}:{s.T.Seconds:00}";
        Progress = Math.Clamp(s.T / duration, 0, 1);
        var x = s.Sensors;
        CpuText = $"{Fmt(x.CpuLoad, "%")} · {Fmt(x.CpuMhz, " MHz")}{(x.CpuTempC is { } ct ? $" · {ct:0}°C" : "")}{(x.CpuThrottled ? " · ⚠" : "")}";
        GpuText = $"{Fmt(x.GpuLoad, "%")} · {Fmt(x.GpuTempC, "°C")}{(x.GpuHotspotC is { } h ? $" (hotspot {h:0}°C)" : "")} · {Fmt(x.GpuClockMhz, " MHz")}";
        RamText = Fmt(x.RamUsedPct, "%");
        Push(TempSeries, x.GpuTempC ?? x.CpuTempC ?? 0);
        Push(LoadSeries, SelectedKind == StressKind.Gpu ? x.GpuLoad ?? 0 : x.CpuLoad ?? 0);
        var rate = s.CpuRate + s.GpuRate + s.RamRate / 1e8;
        _rateMax = Math.Max(_rateMax, rate);
        Push(RateSeries, _rateMax > 0 ? rate / _rateMax * 100 : 0);
    }

    private static string Fmt(double? v, string unit) => v is { } d ? $"{d:0}{unit}" : "—";

    private void ShowResult(StressResult r)
    {
        ResultOk = r.Passed;
        ResultTitle = r.Passed ? Loc.I["stress.passed"]
            : r.StopReason is { } k && k != "stress.stop.errors" ? Loc.I[k]
            : Loc.I["stress.failed"];
        var parts = new List<string> { Loc.I.Format("stress.ran", $"{(int)r.Ran.TotalMinutes:00}:{r.Ran.Seconds:00}") };
        if (r.CpuErrors + r.GpuErrors + r.RamErrors > 0) parts.Add(Loc.I.Format("stress.errors", r.CpuErrors, r.GpuErrors, r.RamErrors));
        if (r.MaxGpuTemp is { } g) parts.Add(Loc.I.Format("stress.maxGpu", $"{g:0}"));
        if (r.MaxCpuTemp is { } c) parts.Add(Loc.I.Format("stress.maxCpu", $"{c:0}"));
        if (r.ThrottleSeen || r.CpuDropPercent >= 10) parts.Add(Loc.I.Format("stress.throttle", $"{r.CpuDropPercent:0}"));
        ResultDetail = string.Join("\n", parts);
    }
}
