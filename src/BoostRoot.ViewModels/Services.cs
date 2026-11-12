using BoostRoot.Core.Boost;
using BoostRoot.Core.Hardware;
using BoostRoot.Core.Scoring;
using BoostRoot.Core.Net;
using BoostRoot.Core.Tools;
using BoostRoot.Core.Monitoring;
using BoostRoot.Core.Stress;
using BoostRoot.Core.Fps;
using BoostRoot.Core.Clips;
using BoostRoot.Core.Tweaks;
using BoostRoot.Core.Profiles;
using BoostRoot.Games;

namespace BoostRoot.ViewModels;

public sealed record AppSettings(
    string Language,
    bool Sounds,
    bool Animations,
    bool Offline,
    IReadOnlyList<string> CloseList,
    bool WaitForGame,
    IReadOnlyList<string>? OverlayMetrics = null,
    string OverlayCorner = "tl",
    CrosshairSettings? Crosshair = null,
    IReadOnlyList<string>? StretchGames = null,
    ClipSettings? Clips = null,
    bool FirstRunDone = false)
{
    public static AppSettings Default { get; } = new("tr", true, true, false, ["chrome", "msedge", "opera", "firefox", "spotify", "steamwebhelper", "epicgameslauncher"], false);
    public static IReadOnlyList<string> AllMetrics { get; } = ["fps", "low1", "cpu", "cputemp", "gpu", "gputemp", "vram", "ram", "clock"];
    public IReadOnlyList<string> Metrics => OverlayMetrics ?? ["fps", "low1", "cpu", "gpu", "gputemp", "ram"];
    public CrosshairSettings CrosshairOrDefault => Crosshair ?? new();
    public ClipSettings ClipsOrDefault => Clips ?? new();
    public bool IsStretched(string profileId) => StretchGames?.Contains(profileId) == true;
}

public sealed record CrosshairSettings(bool Enabled = false, string Color = "#00FF66", int Size = 8, int Thickness = 2, int Gap = 3, bool Dot = false, bool Outline = true);

/// <param name="Audio">none | system | system+mic</param>
public sealed record ClipSettings(bool ReplayEnabled = false, int ReplaySeconds = 30, int Height = 1080, int Fps = 60, string? Folder = null, string Audio = "system", string? DiscordWebhook = null);

/// <summary>Everything the UI needs from the machine. Windows implementation lives in the app; tests use fakes.</summary>
public interface IAppServices
{
    IReadOnlyList<CatalogEntry> LoadCatalog();
    int PcScore { get; }
    BoostResult Boost(CatalogEntry entry, BoostLevel level, AppSettings settings);
    void Undo(string operationId);
    /// <summary>Starts the game through its launcher; false when BoostRoot doesn't know how.</summary>
    bool Launch(CatalogEntry entry);
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    /// <summary>Most recent un-reverted boost operation for the profile, if any.</summary>
    string? LastBoostOperation(string profileId);
    string? CoverPath(CatalogEntry entry);
    /// <summary>Scans hardware; also updates <see cref="PcScore"/>.</summary>
    HardwareReport ScanHardware();

    // Internet (measurement only)
    IReadOnlyList<NetInfo> NetworkAdapters();
    Task<PingStats> PingAsync(string host, CancellationToken ct);
    Task<SpeedResult> SpeedTestAsync(IProgress<double>? progress, CancellationToken ct);

    // Tools
    IReadOnlyList<JunkScan> ScanJunk();
    long CleanJunk(IEnumerable<JunkScan> scans);
    IReadOnlyList<StartupItem> StartupItems();
    void SetStartupEnabled(StartupItem item, bool enabled);
    DriverStatus? CheckDriver();
    void OpenUrl(string url);

    // Monitoring + stress
    ISensors Sensors { get; }
    /// <summary>Opens the GPU load window; null if the GPU can't be stressed here.</summary>
    IStressWorkload? CreateGpuStress();
    long RamStressBytes();

    // FPS test
    IFrameSource StartFrames(CatalogEntry entry);
    FpsHistory FpsHistory { get; }
    /// <summary>Renders the share card PNG into Pictures\BoostRoot and returns its path.</summary>
    string? SaveShareCard(ShareCard card, bool story);

    // Clips
    bool FfmpegInstalled { get; }
    Task InstallFfmpegAsync(IProgress<double>? progress, CancellationToken ct);
    string ClipsFolder { get; }
    IReadOnlyList<ClipFile> ListClips();
    Task<string?> ThumbnailAsync(ClipFile clip);
    Task<double?> ClipDurationAsync(string path);
    /// <summary>Returns the new file's path, or null on failure.</summary>
    Task<string?> TrimClipAsync(string path, double start, double end);
    Task<string?> VerticalClipAsync(string path);
    /// <summary>Uploads to a Discord webhook (shrinks to 10 MB first when needed).</summary>
    Task<bool> SendToDiscordAsync(string webhook, string path, CancellationToken ct);

    // Covers + advanced tweaks
    /// <summary>Downloads missing cover art (Steam CDN) unless offline; returns how many were added.</summary>
    Task<int> PrefetchCoversAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken ct);
    IReadOnlyList<WindowsTweak> AdvancedTweaks { get; }
    /// <summary>tweak id → operation id of tweaks currently applied by BoostRoot.</summary>
    IReadOnlyDictionary<string, string> AppliedTweaks();
    void SetTweak(string id, bool on);
}

public sealed record ShareCard(string Game, FrameStats Before, FrameStats After, double Gain, string Cpu, string Gpu);

public sealed record HardwareReport(HardwareInfo Info, IReadOnlyList<HealthIssue> Issues, PcScore Score);
