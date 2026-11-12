using BoostRoot.Core.Boost;
using BoostRoot.Core.Hardware;
using BoostRoot.Core.Scoring;
using BoostRoot.Core.Net;
using BoostRoot.Core.Tools;
using BoostRoot.Core.Profiles;
using BoostRoot.Games;

namespace BoostRoot.ViewModels;

public sealed record AppSettings(
    string Language,
    bool Sounds,
    bool Animations,
    bool Offline,
    IReadOnlyList<string> CloseList,
    bool WaitForGame)
{
    public static AppSettings Default { get; } = new("tr", true, true, false, ["chrome", "msedge", "opera", "firefox", "spotify", "steamwebhelper", "epicgameslauncher"], false);
}

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
}

public sealed record HardwareReport(HardwareInfo Info, IReadOnlyList<HealthIssue> Issues, PcScore Score);
