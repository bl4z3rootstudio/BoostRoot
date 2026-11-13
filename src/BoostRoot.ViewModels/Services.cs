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
using BoostRoot.Core.Updates;
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
    bool FirstRunDone = false,
    IReadOnlyList<string>? Favorites = null,
    bool AutoUndo = false,
    BoostRoot.Core.Crosshair.CrosshairDesign? CrosshairDesign = null,
    string Accent = "purple",
    bool TrayIcon = true,
    bool AutoBoost = false,
    bool StartWithWindows = false)
{
    /// <summary>What the overlay draws: the Studio design if one was chosen, else the simple settings.</summary>
    public BoostRoot.Core.Crosshair.CrosshairDesign OverlayCrosshair => CrosshairDesign ?? new(Color: CrosshairOrDefault.Color, Outline: CrosshairOrDefault.Outline, Dot: CrosshairOrDefault.Dot,
        Inner: new(CrosshairOrDefault.Size > 0, CrosshairOrDefault.Thickness, CrosshairOrDefault.Size, CrosshairOrDefault.Gap, 1));
    public bool IsFavorite(string profileId) => Favorites?.Contains(profileId) == true;
    public static AppSettings Default { get; } = new("tr", true, true, false, ["chrome", "msedge", "opera", "firefox", "spotify", "steamwebhelper", "epicgameslauncher"], false);
    public static IReadOnlyList<string> AllMetrics { get; } = ["fps", "low1", "cpu", "cputemp", "gpu", "gputemp", "vram", "ram", "clock", "ping", "time"];
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
    IReadOnlyList<DnsServer> SystemDns();
    Task<DnsResult> MeasureDnsAsync(DnsServer server, CancellationToken ct);

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
    bool FpsToolInstalled { get; }
    Task InstallFpsToolAsync(CancellationToken ct);
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
    /// <summary>Finds/downloads cover art for one game; null when none exists (a generated cover is shown).</summary>
    Task<string?> FetchCoverAsync(CatalogEntry entry, CancellationToken ct);
    IReadOnlyList<WindowsTweak> AdvancedTweaks { get; }
    /// <summary>tweak id → operation id of tweaks currently applied by BoostRoot.</summary>
    IReadOnlyDictionary<string, string> AppliedTweaks();
    void SetTweak(string id, bool on);

    // Updates (GitHub Releases)
    Task<UpdateInfo?> CheckUpdateAsync(CancellationToken ct);
    /// <summary>Downloads + verifies the installer, starts it and asks the app to exit. False on failure.</summary>
    Task<bool> InstallUpdateAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct);

    // Crosshair studio + misc
    void CopyToClipboard(string text);
    PlaytimeLog Playtime { get; }
    /// <summary>Frees RAM without closing anything; returns bytes freed.</summary>
    long CleanRam();
    /// <summary>Visible apps running right now (process names), for the "close before playing" picker.</summary>
    IReadOnlyList<string> RunningApps();
    /// <summary>Full exe paths of running programs (Auto Boost matches them against game install folders).</summary>
    IReadOnlyList<string> RunningProcessPaths() => [];
    /// <summary>Start BoostRoot (hidden in the tray) when the user signs in. Returns false if it could not be set.</summary>
    bool SetStartWithWindows(bool on) => false;
    /// <summary>Install size of each installed game (slow: walks folders).</summary>
    Task<IReadOnlyList<(string Name, string Path, long Bytes)>> GameSizesAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken ct);
    /// <summary>Best/last benchmark score (null when never run).</summary>
    int? LastBenchmark { get; set; }
    /// <summary>Busiest processes right now (CPU % since the previous call, RAM).</summary>
    IReadOnlyList<ProcessUsage> TopProcesses(int count);
    bool KillProcess(int pid);
    /// <summary>"Ready to play?" checks.</summary>
    IReadOnlyList<PreflightItem> Preflight(AppSettings settings);
    /// <summary>Short chime after a successful boost (only called when Sounds is on).</summary>
    void PlayBoostSound() { }
    /// <summary>One ICMP echo; null when lost/timed out. Measure only — never changes network settings.</summary>
    async Task<double?> PingOnceAsync(string host)
    {
        try
        {
            using var p = new System.Net.NetworkInformation.Ping();
            var r = await p.SendPingAsync(host, 1000);
            return r.Status == System.Net.NetworkInformation.IPStatus.Success ? r.RoundtripTime : null;
        }
        catch (Exception) { return null; }
    }

    // Update Center
    Task<IReadOnlyList<AppUpdate>> ScanAppUpdatesAsync(CancellationToken ct);
    /// <summary>True when the app was upgraded.</summary>
    Task<bool> UpgradeAppAsync(AppUpdate app, CancellationToken ct);
    Task<IReadOnlyList<WinUpdate>> ScanWindowsUpdatesAsync(CancellationToken ct);
    Task<(bool Ok, bool Reboot)> InstallWindowsUpdatesAsync(IReadOnlyList<WinUpdate> updates, CancellationToken ct);
    Task HideWindowsUpdateAsync(WinUpdate update, CancellationToken ct);
    Task<IReadOnlyList<BloatApp>> ScanBloatAsync(CancellationToken ct);
    Task<bool> RemoveBloatAsync(BloatApp app, CancellationToken ct);
    /// <summary>Opens a pre-filled GitHub issue with logs/system info (nothing is sent automatically).</summary>
    void ReportBug(string context);
    IReadOnlyList<BoostRoot.Core.Crosshair.CrosshairDesign> LoadCrosshairs();
    void SaveCrosshairs(IReadOnlyList<BoostRoot.Core.Crosshair.CrosshairDesign> designs);
    /// <summary>Writes the design into CS2's own crosshair settings (all Steam accounts). Returns accounts changed; CS2 must be closed.</summary>
    int ApplyCs2Crosshair(BoostRoot.Core.Crosshair.CrosshairDesign design);
}

public sealed record ProcessUsage(int Pid, string Name, double CpuPercent, double RamMb);

/// <param name="Fix">fix id understood by the shell: "close-apps", "ram", "power", "driver", "" = info only</param>
public sealed record PreflightItem(string Key, bool Ok, string Detail, string Fix = "");

public sealed record ShareCard(string Game, FrameStats Before, FrameStats After, double Gain, string Cpu, string Gpu);

public sealed record HardwareReport(HardwareInfo Info, IReadOnlyList<HealthIssue> Issues, PcScore Score);
