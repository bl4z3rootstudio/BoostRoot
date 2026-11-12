using System.Collections.ObjectModel;
using BoostRoot.Core.Net;
using BoostRoot.Core.Tools;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed record PingRow(string Name, string Region, string Avg, string Jitter, string Loss, bool Ok);

public sealed partial class NetViewModel(IAppServices services) : ObservableObject
{
    public ObservableCollection<PingRow> Pings { get; } = [];
    public ObservableCollection<string> Advice { get; } = [];
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string Download { get; set; } = "—";
    [ObservableProperty] public partial string Upload { get; set; } = "—";
    [ObservableProperty] public partial string Latency { get; set; } = "—";
    [ObservableProperty] public partial string? Error { get; set; }

    [RelayCommand]
    public async Task RunAsync(CancellationToken ct)
    {
        if (IsRunning) return;
        IsRunning = true;
        Error = null;
        Pings.Clear();
        Advice.Clear();
        var stats = new List<PingStats>();
        SpeedResult? speed = null;
        try
        {
            foreach (var s in NetAnalysis.DefaultServers)
            {
                var r = await services.PingAsync(s.Host, ct);
                stats.Add(r);
                Pings.Add(new(s.Game, s.Region, r.Received == 0 ? "—" : $"{r.AvgMs:0} ms", $"{r.JitterMs:0} ms", $"%{r.LossPercent:0}", r.Received > 0 && r.LossPercent < 2 && r.JitterMs < 15));
            }
            speed = await services.SpeedTestAsync(new Progress<double>(p => Progress = p), ct);
            Download = $"{speed.DownloadMbps:0.#} Mbps";
            Upload = $"{speed.UploadMbps:0.#} Mbps";
            Latency = $"{speed.IdleLatencyMs:0} → {speed.LoadedLatencyMs:0} ms";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TaskCanceledException)
        {
            Error = ex.Message;
        }
        finally
        {
            var tr = Loc.I.Language == "tr";
            foreach (var a in NetAnalysis.Advice(services.NetworkAdapters(), stats, speed)) Advice.Add(tr ? a.Tr : a.En);
            IsRunning = false;
        }
    }
}

public sealed partial class JunkRowViewModel(JunkScan scan) : ObservableObject
{
    public JunkScan Scan { get; } = scan;
    public string Name => Loc.I.Language == "tr" ? Scan.Category.Name.Tr : Scan.Category.Name.En;
    public string Size => ToolsViewModel.Bytes(Scan.Bytes);
    [ObservableProperty] public partial bool IsChecked { get; set; } = !scan.Category.RequiresConfirm;
}

public sealed partial class StartupRowViewModel(StartupItem item, IAppServices services) : ObservableObject
{
    public StartupItem Item { get; private set; } = item;
    public string Name => Item.Name;
    public string Command => Item.Command;
    [ObservableProperty] public partial bool Enabled { get; set; } = item.Enabled;
    partial void OnEnabledChanged(bool value)
    {
        if (value == Item.Enabled) return;
        services.SetStartupEnabled(Item, value);
        Item = Item with { Enabled = value };
    }
}

public sealed partial class TweakRowViewModel(BoostRoot.Core.Tweaks.WindowsTweak tweak, bool on, IAppServices services) : ObservableObject
{
    private static bool Tr => Loc.I.Language == "tr";
    public string Name => Tr ? tweak.Name.Tr : tweak.Name.En;
    public string Description => Tr ? tweak.Description.Tr : tweak.Description.En;
    public string? Warning => tweak.Warning is { } w ? (Tr ? w.Tr : w.En) : null;
    public bool HasWarning => tweak.Warning is not null;
    public bool RequiresRestart => tweak.RequiresRestart;
    [ObservableProperty] public partial bool IsOn { get; set; } = on;
    partial void OnIsOnChanged(bool value) => services.SetTweak(tweak.Id, value);
}

public sealed partial class ToolsViewModel(IAppServices services) : ObservableObject
{
    public ObservableCollection<TweakRowViewModel> Tweaks { get; } = [];
    public ObservableCollection<string> GpuSteps { get; } = [];
    [ObservableProperty] public partial string? GpuTitle { get; set; }
    private string? _gpuApp;
    public ObservableCollection<JunkRowViewModel> Junk { get; } = [];
    public ObservableCollection<StartupRowViewModel> Startup { get; } = [];
    [ObservableProperty] public partial string? JunkResult { get; set; }
    [ObservableProperty] public partial string? DriverText { get; set; }
    [ObservableProperty] public partial bool DriverIsOld { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    private string? _driverUrl;

    public static string Bytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.#} MB",
        _ => $"{b / 1024.0:0} KB",
    };

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var junk = await Task.Run(services.ScanJunk);
            Junk.Clear();
            foreach (var j in junk) Junk.Add(new JunkRowViewModel(j));
            JunkResult = null;
            Tweaks.Clear();
            var applied = services.AppliedTweaks();
            foreach (var t in services.AdvancedTweaks) Tweaks.Add(new TweakRowViewModel(t, applied.ContainsKey(t.Id), services));
            Startup.Clear();
            foreach (var s in services.StartupItems()) Startup.Add(new StartupRowViewModel(s, services));
            var tr = Loc.I.Language == "tr";
            var driver = services.CheckDriver();
            GpuSteps.Clear();
            var vendor = driver?.Gpu.Vendor ?? BoostRoot.Core.Hardware.GpuVendor.Unknown;
            var key = vendor switch { BoostRoot.Core.Hardware.GpuVendor.Amd => "amd", BoostRoot.Core.Hardware.GpuVendor.Nvidia => "nvidia", _ => null };
            if (key is not null)
            {
                GpuTitle = Loc.I["gpu." + key + ".title"];
                for (var i = 1; Loc.I.Has("gpu." + key + "." + i); i++) GpuSteps.Add(Loc.I["gpu." + key + "." + i]);
                _gpuApp = key == "amd" ? @"C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe" : "https://www.nvidia.com/en-us/software/nvidia-app/";
            }
            if (driver is { } d)
            {
                _driverUrl = d.DownloadUrl;
                DriverIsOld = d.IsOld;
                DriverText = d.AgeDays < 0 ? d.Gpu.Name
                    : tr ? $"{d.Gpu.Name} · sürücü {d.AgeDays} günlük{(d.IsOld ? " — güncelle" : " — güncel")}"
                         : $"{d.Gpu.Name} · driver is {d.AgeDays} days old{(d.IsOld ? " — update it" : " — up to date")}";
            }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task CleanAsync()
    {
        IsBusy = true;
        try
        {
            var picked = Junk.Where(j => j.IsChecked).Select(j => j.Scan).ToList();
            var freed = await Task.Run(() => services.CleanJunk(picked));
            JunkResult = (Loc.I.Language == "tr" ? "Temizlendi: " : "Freed: ") + Bytes(freed);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public void OpenGpuApp() { if (_gpuApp is not null) services.OpenUrl(_gpuApp); }

    [RelayCommand]
    public void OpenDriverPage()
    {
        if (_driverUrl is not null) services.OpenUrl(_driverUrl);
    }
}
