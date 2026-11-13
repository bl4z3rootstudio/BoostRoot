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

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        Error = null;
        try
        {
            var r = await Task.Run(services.ScanHardware);
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
