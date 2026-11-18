using Avalonia.Threading;
using BoostRoot.App.Overlay;
using BoostRoot.Core.Fps;
using BoostRoot.Core.Monitoring;
using BoostRoot.Core.Session;
using BoostRoot.Games;
using BoostRoot.Platform.Windows;
using BoostRoot.ViewModels;
using BoostRoot.ViewModels.Localization;

namespace BoostRoot.App;

/// <summary>Lives from "Boost" until the game closes: 4:3 desktop mode, hotkeys, overlay, crosshair, clips.
/// Nothing keeps running after the game exits (no background service).</summary>
public sealed class GameSession : IDisposable
{
    public const int HkOverlay = 1, HkCrosshair = 2, HkSaveReplay = 3, HkRecord = 4, HkScreenshot = 5;
    private const uint VK_O = 0x4F, VK_C = 0x43, VK_F10 = 0x79, VK_F9 = 0x78, VK_F1 = 0x70;

    private readonly CatalogEntry _entry;
    private readonly AppSettings _settings;
    private readonly ISensors _sensors;
    private readonly GameWatcher _watcher;
    private readonly DispatcherTimer _timer;
    private readonly RollingFps _fps = new();
    private readonly bool _stretched;
    private HotkeyService? _hotkeys;
    private IFrameSource? _frames;
    private StatsOverlay? _overlay;
    private CrosshairOverlay? _crosshair;
    private readonly IClipRecorder? _clips;
    public event Action? Ended;
    public static GameSession? Current { get; private set; }
    /// <summary>Report of the last finished game (shown on the Stats page too).</summary>
    public static SessionSummary? LastSummary { get; private set; }

    private DateTime? _startedAt;
    private bool _timerRes;
    /// <summary>Optional hooks set by the app.</summary>
    public Action<string, string, DateTime, DateTime>? RecordPlaytime { get; set; }
    /// <summary>Automatic FPS logging: the whole session's frame stats are stored as an "auto" run.</summary>
    public Action<BoostRoot.Core.Fps.FpsRun>? RecordFps { get; set; }
    private readonly List<double> _log = [];
    // Stutters, temperatures and packet loss: sampled once per 2 s tick (no extra timers).
    private int _spikes, _stutters;
    private readonly List<string> _causes = [];
    private double? _maxGpu, _maxCpu;
    private readonly Queue<bool> _pingOk = new();
    private DateTime _lastLossAlert, _lastHeatAlert;
    private readonly BoostRoot.Core.Net.IcmpPinger _pinger = new();
    private IReadOnlyList<string> _pausedServices = [];
    public Func<IReadOnlyList<string>, bool>? RaisePriority { get; set; }
    private readonly IReadOnlyList<string> _names;

    public GameSession(CatalogEntry entry, AppSettings settings, ISensors sensors, Func<IReadOnlyList<string>, bool> isRunning, Func<IReadOnlyList<string>, IFrameSource> frames, IClipRecorder? clips)
    {
        Current?.Dispose();
        Current = this;
        _entry = entry;
        _settings = settings;
        _sensors = sensors;
        _clips = clips;
        var names = entry.Profile?.Detect.TryGetValue("process", out var p) == true ? p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
        _names = names;
        _watcher = new GameWatcher(isRunning, names, TimeSpan.FromMinutes(10));
        if (entry.Profile is { } prof && settings.IsStretched(prof.Id) && DisplayChanger.Current() is { } cur)
        {
            var (w, h) = StretchPlan.For(cur.Height);
            _stretched = DisplayChanger.SetStretched(w, h);
        }
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Start();
        _startFrames = () => frames(names);
    }

    private readonly Func<IFrameSource> _startFrames;

    /// <summary>Apps that upload/download in the background and make ping jumpy (sync clients, torrents). Never game launchers or voice chat.</summary>
    private static readonly string[] BandwidthHogs = ["OneDrive", "Dropbox", "GoogleDriveFS", "qbittorrent", "uTorrent", "BitTorrent", "Creative Cloud", "EpicWebHelper"];

    private void NetBoostStart()
    {
        foreach (var name in BandwidthHogs)
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                using (p) try { if (!p.CloseMainWindow() || !p.WaitForExit(1500)) p.Kill(); } catch (Exception) { }
        DnsChanger.FlushDns();
    }
    private bool _gameStarted;

    private void Sample()
    {
        var spikes = Interlocked.Exchange(ref _spikes, 0);
        _sampleTick++;
        if (!_settings.StutterDetect && !_settings.SessionReport && !_settings.Notifications && !_settings.PacketLossAlert) return;
        // Light by design: sensors only every 10 s, or right after a hitch (to name its cause).
        if (spikes == 0 && _sampleTick % 5 != 0) { if (_settings.PacketLossAlert && _sampleTick % 3 == 0) PingOnce(); return; }
        BoostRoot.Core.Monitoring.SensorSnapshot x;
        try { x = _sensors.Read(); } catch (Exception) { return; }
        if (x.GpuTempC is { } g) _maxGpu = Math.Max(_maxGpu ?? 0, g);
        if (x.CpuTempC is { } c) _maxCpu = Math.Max(_maxCpu ?? 0, c);
        if (spikes > 0 && _settings.StutterDetect) { _stutters++; _causes.Add(StutterAnalyzer.Cause(x)); }
        var now = DateTime.UtcNow;
        if (_settings.Notifications && (x.GpuTempC >= 88 || x.CpuTempC >= 92) && now - _lastHeatAlert > TimeSpan.FromMinutes(5))
        {
            _lastHeatAlert = now;
            ToastOverlay.Show(Loc.I.Pick(new($"Sıcaklık yüksek: ekran kartı {x.GpuTempC:0}°C, işlemci {x.CpuTempC:0}°C", $"Running hot: GPU {x.GpuTempC:0}°C, CPU {x.CpuTempC:0}°C")), 4);
        }
        if (_settings.PacketLossAlert && _sampleTick % 3 == 0) PingOnce();
    }

    private int _sampleTick;

    private void PingOnce()
    {
            _ = Task.Run(async () =>
            {
                var r = await _pinger.PingAsync("1.1.1.1", TimeSpan.FromMilliseconds(900), CancellationToken.None);
                lock (_pingOk) { _pingOk.Enqueue(r is not null); while (_pingOk.Count > 10) _pingOk.Dequeue(); }
                int lost; lock (_pingOk) lost = _pingOk.Count(ok => !ok);
                if (lost >= 3 && DateTime.UtcNow - _lastLossAlert > TimeSpan.FromMinutes(2))
                {
                    _lastLossAlert = DateTime.UtcNow;
                    Dispatcher.UIThread.Post(() => ToastOverlay.Show(Loc.I.Pick(new("Paket kaybı var: internet bağlantın dalgalanıyor", "Packet loss: your connection is unstable")), 4));
                }
            });
    }

    private void Tick()
    {
        if (_startedAt is not null && !_settings.PureBoost) Sample();
        switch (_watcher.Tick(TimeSpan.FromSeconds(2)))
        {
            case GameWatcher.State.Running when !_gameStarted:
                OnGameStarted();
                break;
            case GameWatcher.State.Ended or GameWatcher.State.TimedOut:
                Dispose();
                break;
        }
    }

    private void OnGameStarted()
    {
        _startedAt = DateTime.UtcNow;
        _timerRes = TimerResolution.Set();
        try { RaisePriority?.Invoke(_names); } catch (Exception) { }
        if (_settings.PauseDownloads || _settings.NetBoost) _ = Task.Run(() => _pausedServices = BackgroundDownloads.Pause());
        if (_settings.NetBoost) _ = Task.Run(NetBoostStart);
        _gameStarted = true;
        // Pure boost: nothing keeps running while you play except the cheap "is the game still open" check (for auto undo).
        if (_settings.PureBoost) return;
        _frames = _startFrames();
        _frames.Frame += _fps.Add;
        _frames.Frame += ms =>
        {
            lock (_log) if (_log.Count < 2_000_000) _log.Add(ms);
            // A frame 3x slower than the current average (and visibly slow) counts as a hitch.
            if (_fps.Fps is { } avg && avg > 0 && ms > 25 && ms > 3000 / avg) Interlocked.Increment(ref _spikes);
        };
        if (_settings.Hotkeys) _hotkeys = new HotkeyService([
            (HkOverlay, HotkeyService.Alt, VK_O), (HkCrosshair, HotkeyService.Alt, VK_C),
            (HkSaveReplay, HotkeyService.Alt, VK_F10), (HkRecord, HotkeyService.Alt, VK_F9), (HkScreenshot, HotkeyService.Alt, VK_F1)]);
        if (_hotkeys is not null) _hotkeys.Pressed += id => Dispatcher.UIThread.Post(() => OnHotkey(id));
        if (_settings.CrosshairOrDefault.Enabled) ToggleCrosshair();
        if (_settings.ClipsOrDefault.ReplayEnabled) _clips?.StartReplay(_entry.DisplayName);
    }

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case HkOverlay:
                if (_overlay is null) { _overlay = new StatsOverlay(_sensors, _fps, _settings); _overlay.Show(); }
                else { _overlay.Close(); _overlay = null; }
                break;
            case HkCrosshair: ToggleCrosshair(); break;
            case HkSaveReplay: _clips?.SaveReplay(_entry.DisplayName); break;
            case HkRecord: _clips?.ToggleRecording(_entry.DisplayName); break;
            case HkScreenshot: _clips?.Screenshot(_entry.DisplayName); break;
        }
    }

    private void ToggleCrosshair()
    {
        if (_crosshair is null) { _crosshair = new CrosshairOverlay(_settings.OverlayCrosshair); _crosshair.Show(); }
        else { _crosshair.Close(); _crosshair = null; }
    }

    public void Dispose()
    {
        if (Current != this) return;
        Current = null;
        _timer.Stop();
        _hotkeys?.Dispose();
        _frames?.Dispose();
        _overlay?.Close();
        _crosshair?.Close();
        _clips?.StopAll();
        if (_stretched) DisplayChanger.Restore();
        if (_timerRes) TimerResolution.Release();
        if (_pausedServices.Count > 0) { var ps = _pausedServices; _ = Task.Run(() => BackgroundDownloads.Resume(ps)); }
        if (_startedAt is { } st && _entry.Profile is { } pr && !_settings.PureBoost)
        {
            RecordPlaytime?.Invoke(pr.Id, _entry.DisplayName, st, DateTime.UtcNow);
            List<double> log;
            lock (_log) { log = [.. _log]; _log.Clear(); }
            // Loading screens and alt-tab produce 250 ms+ frames that are not gameplay; leaving them in made "1% low" look terrible.
            var stats = log.Count > 600 ? BoostRoot.Core.Fps.FrameStats.From([.. log.Where(ms => ms < 250)]) : null;
            if (stats is not null)
                try { RecordFps?.Invoke(new(pr.Id, _entry.DisplayName, DateTime.UtcNow, "auto", stats)); } catch (Exception) { }
            var length = DateTime.UtcNow - st;
            if (_settings.SessionReport && length > TimeSpan.FromMinutes(2))
            {
                var summary = new SessionSummary(_entry.DisplayName, length, stats, _stutters, _causes.Count > 0 ? SessionSummary.MostCommon(_causes) : null, _maxGpu, _maxCpu);
                LastSummary = summary;
                try { ToastOverlay.Show(summary.Text(Loc.I.Pick), 12); } catch (Exception) { }
            }
        }
        Ended?.Invoke();
    }
}

/// <summary>Clip features used by the session (implemented with ffmpeg in Plan 09).</summary>
public interface IClipRecorder
{
    void StartReplay(string game);
    void SaveReplay(string game);
    void ToggleRecording(string game);
    void Screenshot(string game);
    void StopAll();
}
