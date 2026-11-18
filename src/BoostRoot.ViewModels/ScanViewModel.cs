using System.Collections.ObjectModel;
using BoostRoot.Core.Hardware;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed record PartRow(string Label, string Value, string Note, bool Ok);

public sealed record IssueRow(string Tag, string Title, string Fix);

public sealed partial class ScanViewModel(IAppServices services, Action<HardwareReport>? onScanned = null) : ObservableObject
{
    public ObservableCollection<PartRow> Parts { get; } = [];
    public ObservableCollection<IssueRow> Issues { get; } = [];

    [ObservableProperty] public partial bool IsScanning { get; set; }
    [ObservableProperty] public partial int Score { get; set; }
    [ObservableProperty] public partial string ScoreLabel { get; set; } = "";
    [ObservableProperty] public partial bool HasResult { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
    /// <summary>Measured (never estimated) FPS with and without BoostRoot, per game, from the FPS test's before/after runs.</summary>
    public ObservableCollection<FpsGainRow> Gains { get; } = [];
    [ObservableProperty] public partial bool HasGains { get; set; }
    /// <summary>Opens the FPS test page (set by the shell).</summary>
    public Action? OpenFpsTest { get; set; }
    [RelayCommand] public void GoFpsTest() => OpenFpsTest?.Invoke();

    public void RefreshGains()
    {
        Gains.Clear();
        try
        {
            var all = services.FpsHistory.All();
            foreach (var id in all.Select(r => r.GameId).Distinct())
                if (services.FpsHistory.Comparison(id) is var (b, a))
                    Gains.Add(new FpsGainRow(a.GameName, $"{b.Stats.AvgFps:0}", $"{a.Stats.AvgFps:0}",
                        $"{(BoostRoot.Core.Fps.FrameStats.Gain(b.Stats, a.Stats) >= 0 ? "+" : "")}{BoostRoot.Core.Fps.FrameStats.Gain(b.Stats, a.Stats):0.#}%",
                        $"{b.Stats.Low1:0} → {a.Stats.Low1:0}", a.At.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture)));
        }
        catch (Exception) { }
        HasGains = Gains.Count > 0;
    }

    [ObservableProperty] public partial string? Bottleneck { get; set; }

    /// <summary>Plain words: is the graphics card or the processor the weaker part for games?</summary>
    private static string? BottleneckText(BoostRoot.Core.Hardware.HardwareInfo hw)
    {
        if (hw.MainGpu is not { } g || BoostRoot.Core.Fps.FpsEstimator.GpuScore(g.Name) is not { } gs) return null;
        var cpu = BoostRoot.Core.Fps.FpsEstimator.CpuFactor(hw.CpuName) * 100; // same scale: 100 = mid-range
        var key = gs > cpu * 1.6 ? "scan.bnCpu" : cpu > gs * 1.6 ? "scan.bnGpu" : "scan.bnBalanced";
        return Loc.I.Format(key, g.Name, hw.CpuName.Trim());
    }

    /// <summary>Last scan result (null until the first scan finishes).</summary>
    public HardwareReport? Report { get; private set; }

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        Error = null;
        try
        {
            var r = await Task.Run(services.ScanHardware);
            Report = r;
            Bottleneck = BottleneckText(r.Info);
            RefreshGains();
            Show(r);
            onScanned?.Invoke(r);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Error = ex.Message;
        }
        finally { IsScanning = false; }
    }

    private void Show(HardwareReport r)
    {
        var tr = Loc.I.Language == "tr";
        string T(string a, string b) => Loc.I.Text(a, b);
        var hw = r.Info;
        Parts.Clear();
        var issueIds = r.Issues.Select(i => i.Id).ToHashSet();
        var gpu = hw.MainGpu;
        Parts.Add(new(T("Ekran kartı", "Graphics card"), gpu is null ? "—" : $"{gpu.Name} · {gpu.VramMb / 1024.0:0.#} GB", gpu?.DriverVersion is { } v ? T("Sürücü ", "Driver ") + v : "", !issueIds.Contains("gpu-driver-old")));
        Parts.Add(new(T("İşlemci", "Processor"), hw.CpuName, T($"{hw.LogicalCores} iş parçacığı", $"{hw.LogicalCores} threads"), true));
        var ram = hw.RamModules.FirstOrDefault(m => m.ConfiguredMts > 0);
        Parts.Add(new("RAM", $"{hw.RamGb:0} GB", ram is null ? "" : $"{ram.ConfiguredMts} / {ram.SpeedMts} MHz", !issueIds.Contains("ram-xmp")));
        Parts.Add(new(T("Disk", "Disk"), T($"%{100 - hw.SystemDiskFreePercent:0} dolu", $"{100 - hw.SystemDiskFreePercent:0}% used"), "", !issueIds.Contains("disk-full")));
        var d = hw.Displays.FirstOrDefault();
        Parts.Add(new(T("Monitör", "Monitor"), d is null ? "—" : $"{d.Width}×{d.Height} · {d.CurrentHz} Hz", d is null ? "" : T($"En fazla {d.MaxHz} Hz", $"Up to {d.MaxHz} Hz"), !issueIds.Contains("display-hz")));
        var n = hw.Networks.OrderByDescending(x => x.SpeedMbps).FirstOrDefault();
        Parts.Add(new(T("İnternet", "Network"), n is null ? "—" : (n.IsWireless ? "Wi-Fi" : T("Kablolu", "Wired")) + $" · {n.SpeedMbps} Mbps", n?.Name ?? "", !issueIds.Contains("net-100mbps") && !issueIds.Contains("net-wifi")));
        Issues.Clear();
        foreach (var i in r.Issues) Issues.Add(new(i.Id.Split('-')[0].ToUpperInvariant(), Loc.I.Pick(i.Title), Loc.I.Pick(i.Fix)));
        Score = r.Score.Total;
        ScoreLabel = Score switch
        {
            >= 80 => T("Güçlü oyun PC'si", "Strong gaming PC"),
            >= 60 => T("İyi oyun PC'si", "Good gaming PC"),
            >= 40 => T("Orta seviye oyun PC'si", "Mid-range gaming PC"),
            _ => T("Giriş seviyesi", "Entry level"),
        };
        HasResult = true;
    }
}

/// <summary>One measured game: average FPS before and after Boost, 1% lows, and when it was measured.</summary>
public sealed record FpsGainRow(string Game, string Without, string With, string Gain, string Lows, string Date);
