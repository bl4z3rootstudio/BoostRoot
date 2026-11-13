using Avalonia.Threading;
using BoostRoot.App.Overlay;
using BoostRoot.Core.Fps;
using BoostRoot.Core.Monitoring;
using BoostRoot.Core.Session;
using BoostRoot.Games;
using BoostRoot.Platform.Windows;
using BoostRoot.ViewModels;

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

    private DateTime? _startedAt;
    private bool _timerRes;
    /// <summary>Optional hooks set by the app.</summary>
    public Action<string, string, DateTime, DateTime>? RecordPlaytime { get; set; }
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

    private void Tick()
    {
        switch (_watcher.Tick(TimeSpan.FromSeconds(2)))
        {
            case GameWatcher.State.Running when _hotkeys is null:
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
        _frames = _startFrames();
        _frames.Frame += _fps.Add;
        _hotkeys = new HotkeyService([
            (HkOverlay, HotkeyService.Alt, VK_O), (HkCrosshair, HotkeyService.Alt, VK_C),
            (HkSaveReplay, HotkeyService.Alt, VK_F10), (HkRecord, HotkeyService.Alt, VK_F9), (HkScreenshot, HotkeyService.Alt, VK_F1)]);
        _hotkeys.Pressed += id => Dispatcher.UIThread.Post(() => OnHotkey(id));
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
        if (_crosshair is null) { _crosshair = new CrosshairOverlay(_settings.CrosshairOrDefault); _crosshair.Show(); }
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
        if (_startedAt is { } st && _entry.Profile is { } pr) RecordPlaytime?.Invoke(pr.Id, _entry.DisplayName, st, DateTime.UtcNow);
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
