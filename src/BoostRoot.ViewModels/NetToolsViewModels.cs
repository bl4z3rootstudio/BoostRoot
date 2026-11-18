using System.Collections.ObjectModel;
using BoostRoot.Core.Net;
using BoostRoot.Core.Tools;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed record PingRow(string Name, string Region, string Avg, string Jitter, string Loss, bool Ok);

public sealed record DnsRow(string Name, string Address, string Ms, bool Best, bool Current);

public sealed partial class NetViewModel(IAppServices services) : ObservableObject
{
    public ObservableCollection<DnsRow> Dns { get; } = [];
    [ObservableProperty] public partial bool DnsRunning { get; set; }
    [ObservableProperty] public partial string? DnsAdvice { get; set; }

    // ---- "İnternet'i iyileştir": everything that really helps, nothing that pretends to lower physical ping ----
    public ObservableCollection<string> ImproveSteps { get; } = [];
    [ObservableProperty] public partial bool Improving { get; set; }
    [ObservableProperty] public partial DnsServer? SuggestedDns { get; set; }
    [ObservableProperty] public partial string? DnsQuestion { get; set; }
    [ObservableProperty] public partial bool DnsChanged { get; set; } = services.DnsChangedByBoostRoot;
    private DnsServer? _bestSwitchable;

    [RelayCommand]
    public async Task ImproveAsync(CancellationToken ct)
    {
        if (Improving) return;
        Improving = true;
        ImproveSteps.Clear();
        SuggestedDns = null;
        DnsQuestion = null;
        try
        {
            ImproveSteps.Add(Loc.I["improve.measuring"]);
            var before = await LineQualityAsync(ct);
            ImproveSteps[^1] = Loc.I.Format("improve.before", before.Text);
            foreach (var id in new[] { "net-throttle-off" })
                if (!services.AppliedTweaks().ContainsKey(id))
                    try { services.SetTweak(id, true); } catch (Exception) { }
            ImproveSteps.Add(Loc.I["improve.throttle"]);
            await Task.Run(services.FlushDns, ct);
            ImproveSteps.Add(Loc.I["improve.flush"]);
            ImproveSteps.Add(Loc.I["improve.downloads"]);
            await RunDnsAsync(ct);
            // Only resolvers that do not filter or log for ads are offered for switching.
            var current = services.SystemDns();
            var rows = Dns.Where(d => DnsSwitchable.Contains(d.Address)).ToList();
            var best = rows.Where(r => r.Ms != "—").OrderBy(r => double.Parse(r.Ms.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault();
            var cur = Dns.Where(d => d.Current && d.Ms != "—").Select(d => double.Parse(d.Ms.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(double.MaxValue).Min();
            if (best is not null && current.All(c => c.Address != best.Address) && cur - double.Parse(best.Ms.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture) > 8)
            {
                _bestSwitchable = new DnsServer(best.Name, best.Address);
                SuggestedDns = _bestSwitchable;
                DnsQuestion = Loc.I.Format("improve.askDns", best.Name, best.Address);
            }
            else ImproveSteps.Add(Loc.I["dns.fine"]);
            var after = await LineQualityAsync(ct);
            ImproveSteps.Add(Loc.I.Format("improve.after", after.Text));
            ImproveSteps.Add(Loc.I["improve.honest"]);
        }
        finally { Improving = false; }
    }

    private static readonly HashSet<string> DnsSwitchable = ["1.1.1.1", "8.8.8.8", "9.9.9.9"];

    /// <summary>20 pings, 150 ms apart: average, jitter (spread) and loss — the numbers the "before/after" lines show.</summary>
    private static async Task<(double? Avg, double Jitter, int LossPct, string Text)> LineQualityAsync(CancellationToken ct)
    {
        var pinger = new BoostRoot.Core.Net.IcmpPinger();
        var res = new List<double?>();
        for (var i = 0; i < 20; i++)
        {
            res.Add(await pinger.PingAsync("1.1.1.1", TimeSpan.FromMilliseconds(1000), ct));
            await Task.Delay(150, ct);
        }
        var ok = res.Where(r => r is not null).Select(r => r!.Value).ToList();
        var loss = (int)Math.Round(100.0 * (res.Count - ok.Count) / res.Count);
        double? avg = ok.Count > 0 ? ok.Average() : null;
        var jitter = ok.Count > 1 ? ok.Zip(ok.Skip(1), (a, b) => Math.Abs(a - b)).Average() : 0;
        return (avg, jitter, loss, avg is { } a2 ? $"{a2:0} ms, ±{jitter:0} ms, %{loss}" : "—");
    }

    [RelayCommand]
    public void ApplyDns()
    {
        if (_bestSwitchable is not { } s) return;
        var ok = services.SetDns(s.Address);
        ImproveSteps.Add(ok ? Loc.I.Format("improve.dnsDone", s.Name) : Loc.I["improve.dnsFail"]);
        DnsQuestion = null;
        DnsChanged = services.DnsChangedByBoostRoot;
    }

    [RelayCommand] public void SkipDns() => DnsQuestion = null;

    [RelayCommand]
    public void RestoreDns()
    {
        var ok = services.RestoreDns();
        ImproveSteps.Add(ok ? Loc.I["improve.dnsRestored"] : Loc.I["improve.dnsFail"]);
        DnsChanged = services.DnsChangedByBoostRoot;
    }

    [RelayCommand]
    public async Task RunDnsAsync(CancellationToken ct)
    {
        if (DnsRunning) return;
        DnsRunning = true;
        Dns.Clear();
        DnsAdvice = null;
        try
        {
            var current = services.SystemDns();
            var servers = current.Concat(DnsBenchmark.Public.Where(p => current.All(c => c.Address != p.Address))).ToList();
            var results = new List<DnsResult>();
            foreach (var s in servers) results.Add(await services.MeasureDnsAsync(s, ct));
            var best = results.Where(r => r.MedianMs is not null).OrderBy(r => r.MedianMs).FirstOrDefault();
            foreach (var r in results.OrderBy(r => r.MedianMs ?? double.MaxValue))
                Dns.Add(new(r.Server.Name, r.Server.Address, r.MedianMs is { } m ? $"{m:0} ms" : "—", r == best, current.Any(c => c.Address == r.Server.Address)));
            var cur = results.Where(r => current.Any(c => c.Address == r.Server.Address) && r.MedianMs is not null).OrderBy(r => r.MedianMs).FirstOrDefault();
            DnsAdvice = best is null ? null
                : cur is null || cur.MedianMs - best.MedianMs <= 8 ? Loc.I["dns.fine"]
                : Loc.I.Format("dns.faster", best.Server.Name, best.Server.Address, $"{cur.MedianMs - best.MedianMs:0}");
        }
        catch (OperationCanceledException) { }
        finally { DnsRunning = false; }
    }
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
            foreach (var a in NetAnalysis.Advice(services.NetworkAdapters(), stats, speed)) Advice.Add(Loc.I.Pick(a));
            IsRunning = false;
        }
    }
}

public sealed partial class JunkRowViewModel(JunkScan scan) : ObservableObject
{
    public JunkScan Scan { get; } = scan;
    public string Name => Loc.I.Pick(Scan.Category.Name);
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
    public string Name => Loc.I.Pick(tweak.Name);
    public string Description => Loc.I.Pick(tweak.Description);
    public string? Warning => tweak.Warning is { } w ? Loc.I.Pick(w) : null;
    public bool HasWarning => tweak.Warning is not null;
    public bool RequiresRestart => tweak.RequiresRestart;
    [ObservableProperty] public partial bool IsOn { get; set; } = on;
    partial void OnIsOnChanged(bool value) => services.SetTweak(tweak.Id, value);
}

public sealed record GameSizeRow(string Name, string Size, string Path, double Share);

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
                    : Loc.I.Format(d.IsOld ? "upd.gpuOld" : "upd.gpuOk", d.Gpu.Name, d.AgeDays);
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

    [ObservableProperty] public partial string? RamResult { get; set; }
    public ObservableCollection<GameSizeRow> Sizes { get; } = [];
    [ObservableProperty] public partial bool SizesBusy { get; set; }

    [RelayCommand]
    public async Task LoadSizesAsync(CancellationToken ct)
    {
        SizesBusy = true;
        try
        {
            var list = await services.GameSizesAsync([.. services.LoadCatalog().Where(e => e.IsInstalled)], ct);
            Sizes.Clear();
            var max = Math.Max(1, list.Count == 0 ? 1 : list.Max(x => x.Bytes));
            foreach (var g in list.OrderByDescending(x => x.Bytes)) Sizes.Add(new(g.Name, Bytes(g.Bytes), g.Path, g.Bytes / (double)max));
        }
        finally { SizesBusy = false; }
    }

    [RelayCommand] public void OpenFolder(GameSizeRow r) => services.OpenUrl(r.Path);

    [RelayCommand]
    public async Task CleanRamAsync()
    {
        IsBusy = true;
        try { var freed = await Task.Run(services.CleanRam); RamResult = Loc.I.Format("tools.ramFreed", Bytes(freed)); }
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
