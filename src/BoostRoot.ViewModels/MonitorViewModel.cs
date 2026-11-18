using System.Collections.ObjectModel;
using BoostRoot.Core.Monitoring;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed partial class ProcessRowViewModel(ProcessUsage p) : ObservableObject
{
    public ProcessUsage P { get; } = p;
    public string Name => P.Name;
    public string Cpu => $"{P.CpuPercent:0.#}%";
    public string Ram => P.RamMb >= 1024 ? $"{P.RamMb / 1024:0.0} GB" : $"{P.RamMb:0} MB";
    public double CpuShare => Math.Clamp(P.CpuPercent / 100, 0, 1);
}

/// <summary>Live system monitor (like a gamer's task manager): charts + busiest processes + "ready to play?" checks.</summary>
public sealed partial class MonitorViewModel(IAppServices services, Func<AppSettings> settings) : ObservableObject
{
    /// <summary>Live monitoring on/off (saved). Off = the page reads nothing, even while it is open.</summary>
    public bool Live
    {
        get => settings().MonitorLive;
        set
        {
            if (settings().MonitorLive == value) return;
            LiveChanged?.Invoke(value);
            OnPropertyChanged();
        }
    }
    public Action<bool>? LiveChanged { get; set; }
    public ObservableCollection<double> CpuSeries { get; } = [];
    public ObservableCollection<double> GpuSeries { get; } = [];
    public ObservableCollection<double> TempSeries { get; } = [];
    public ObservableCollection<double> RamSeries { get; } = [];
    public ObservableCollection<double> PingSeries { get; } = [];
    public IReadOnlyList<string> PingHosts { get; } = ["1.1.1.1", "8.8.8.8", "9.9.9.9"];
    [ObservableProperty] public partial string PingHost { get; set; } = "1.1.1.1";
    [ObservableProperty] public partial string PingText { get; set; } = "—";
    [ObservableProperty] public partial string PingInfo { get; set; } = "";
    [ObservableProperty] public partial bool PingBad { get; set; }
    private readonly Queue<double?> _pings = new();
    private bool _pinging;

    partial void OnPingHostChanged(string value) { _pings.Clear(); PingSeries.Clear(); }

    /// <summary>Live ping: one echo per tick, last 60 kept for loss / jitter.</summary>
    public async Task PingTickAsync()
    {
        if (_pinging) return;
        _pinging = true;
        try
        {
            var ms = await services.PingOnceAsync(PingHost);
            _pings.Enqueue(ms);
            while (_pings.Count > 60) _pings.Dequeue();
            PingSeries.Add(ms ?? 0); if (PingSeries.Count > 120) PingSeries.RemoveAt(0);
            var ok = _pings.Where(p => p is not null).Select(p => p!.Value).ToList();
            var loss = 100.0 * (_pings.Count - ok.Count) / _pings.Count;
            var jitter = ok.Count > 1 ? ok.Zip(ok.Skip(1), (a, b) => Math.Abs(a - b)).Average() : 0;
            PingText = ms is { } v ? $"{v:0} ms" : Loc.I["mon.pingLost"];
            PingInfo = Loc.I.Format("mon.pingInfo", $"{jitter:0}", $"{loss:0}");
            PingBad = ms is null or > 100 || loss >= 2 || jitter > 20;
        }
        finally { _pinging = false; }
    }
    public ObservableCollection<ProcessRowViewModel> Processes { get; } = [];
    public ObservableCollection<PreflightItem> Checks { get; } = [];
    [ObservableProperty] public partial string CpuText { get; set; } = "—";
    [ObservableProperty] public partial string GpuText { get; set; } = "—";
    [ObservableProperty] public partial string TempText { get; set; } = "—";
    [ObservableProperty] public partial string RamText { get; set; } = "—";
    /// <summary>Everything else the sensors know, as small tiles (updated with the charts).</summary>
    public ObservableCollection<DetailTile> Details { get; } = [];
    private long _rx = -1, _tx = -1;
    private DateTime _netAt;
    private int _procCount;
    [ObservableProperty] public partial bool AllOk { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    private int _ticks;

    /// <summary>Called every second by the view while visible.</summary>
    public void Tick()
    {
        var s = services.Sensors.Read();
        static void Push(ObservableCollection<double> c, double v) { c.Add(v); if (c.Count > 120) c.RemoveAt(0); }
        Push(CpuSeries, s.CpuLoad ?? 0);
        Push(GpuSeries, s.GpuLoad ?? 0);
        Push(TempSeries, s.GpuTempC ?? s.CpuTempC ?? 0);
        Push(RamSeries, s.RamUsedPct ?? 0);
        CpuText = s.CpuLoad is { } c ? $"{c:0}%{(s.CpuMhz is { } m ? $" · {m:0} MHz" : "")}" : "—";
        GpuText = s.GpuLoad is { } g ? $"{g:0}%{(s.GpuClockMhz is { } gm ? $" · {gm:0} MHz" : "")}" : "—";
        TempText = s.GpuTempC is { } t ? $"{t:0}°C{(s.GpuHotspotC is { } h ? $" / {h:0}°C" : "")}" : "—";
        RamText = s.RamUsedPct is { } r ? $"{r:0}%" : "—";
        UpdateDetails(s);
        if (_ticks++ % 3 == 0) { RefreshProcesses(); try { _procCount = System.Diagnostics.Process.GetProcesses().Length; } catch (Exception) { } }
    }

    private void UpdateDetails(BoostRoot.Core.Monitoring.SensorSnapshot s)
    {
        static string N(double? v, string u) => v is { } d ? $"{d:0}{u}" : "—";
        var (down, up) = NetSpeed();
        string disk;
        try { var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"); disk = d.IsReady ? $"{d.AvailableFreeSpace / 1_073_741_824.0:0} GB" : "—"; } catch (Exception) { disk = "—"; }
        var up24 = TimeSpan.FromMilliseconds(Environment.TickCount64);
        DetailTile[] tiles =
        [
            new(Loc.I["mon.cpuTemp"], N(s.CpuTempC, "°C"), s.CpuTempC >= 90),
            new(Loc.I["mon.cpuClock"], N(s.CpuMhz, " MHz"), false),
            new(Loc.I["mon.throttle"], s.CpuThrottled ? Loc.I["mon.yes"] : Loc.I["mon.no"], s.CpuThrottled),
            new(Loc.I["mon.gpuTemp"], N(s.GpuTempC, "°C"), s.GpuTempC >= 85),
            new(Loc.I["mon.gpuPower"], N(s.GpuPowerW, " W"), false),
            new("VRAM", N(s.VramUsedMb, " MB"), false),
            new(Loc.I["mon.down"], down, false),
            new(Loc.I["mon.up"], up, false),
            new(Loc.I["mon.diskFree"], disk, false),
            new(Loc.I["mon.uptime"], $"{(int)up24.TotalHours} sa {up24.Minutes} dk", up24.TotalDays > 7),
            new(Loc.I["mon.procs"], _procCount > 0 ? _procCount.ToString(System.Globalization.CultureInfo.CurrentCulture) : "—", false),
        ];
        // Hotspot only on cards that really report one.
        if (s.GpuHotspotC is { } hot) tiles = [.. tiles[..4], new(Loc.I["mon.hotspot"], N(hot, "°C"), hot >= 100), .. tiles[4..]];
        if (Details.Count != tiles.Length) { Details.Clear(); foreach (var t in tiles) Details.Add(t); }
        else for (var i = 0; i < tiles.Length; i++) if (Details[i] != tiles[i]) Details[i] = tiles[i];
    }

    /// <summary>Download/upload speed of all active adapters since the last tick.</summary>
    private (string Down, string Up) NetSpeed()
    {
        try
        {
            long rx = 0, tx = 0;
            foreach (var n in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                var st = n.GetIPStatistics();
                rx += st.BytesReceived; tx += st.BytesSent;
            }
            var now = DateTime.UtcNow;
            var secs = (now - _netAt).TotalSeconds;
            var res = _rx < 0 || secs <= 0 ? ("—", "—") : (Rate((rx - _rx) / secs), Rate((tx - _tx) / secs));
            (_rx, _tx, _netAt) = (rx, tx, now);
            return res;
        }
        catch (Exception) { return ("—", "—"); }
        static string Rate(double bytesPerSec) => bytesPerSec * 8 >= 1_000_000 ? $"{bytesPerSec * 8 / 1_000_000:0.0} Mbps" : $"{bytesPerSec * 8 / 1000:0} Kbps";
    }

    [RelayCommand]
    public void RefreshProcesses()
    {
        Processes.Clear();
        foreach (var p in services.TopProcesses(12)) Processes.Add(new ProcessRowViewModel(p));
    }

    [RelayCommand]
    public void Kill(ProcessRowViewModel row)
    {
        Status = services.KillProcess(row.P.Pid) ? Loc.I.Format("mon.killed", row.Name) : Loc.I.Format("mon.killFailed", row.Name);
        RefreshProcesses();
    }

    [RelayCommand]
    public void RunChecks()
    {
        Checks.Clear();
        foreach (var c in services.Preflight(settings())) Checks.Add(c);
        AllOk = Checks.All(c => c.Ok);
    }

    [RelayCommand]
    public async Task FixAllAsync()
    {
        var fixes = Checks.Where(c => !c.Ok).Select(c => c.Fix).ToHashSet();
        if (fixes.Contains("ram")) await Task.Run(services.CleanRam);
        if (fixes.Contains("hz")) services.SetMaxRefresh();
        if (fixes.Contains("close-apps"))
            foreach (var p in services.TopProcesses(200).Where(p => settings().CloseList.Contains(p.Name, StringComparer.OrdinalIgnoreCase)))
                services.KillProcess(p.Pid);
        if (fixes.Contains("driver") && services.CheckDriver()?.DownloadUrl is { } url) services.OpenUrl(url);
        RunChecks();
        Status = Loc.I["mon.fixed"];
    }
}

public sealed record DetailTile(string Label, string Value, bool Warn);
