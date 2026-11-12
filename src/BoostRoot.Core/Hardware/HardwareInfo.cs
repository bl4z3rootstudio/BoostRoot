using BoostRoot.Core.Profiles;

namespace BoostRoot.Core.Hardware;

public enum GpuVendor { Unknown, Nvidia, Amd, Intel }

public sealed record GpuInfo(string Name, GpuVendor Vendor, long VramMb, string? DriverVersion, DateOnly? DriverDate);

/// <param name="SpeedMts">Rated (max supported) speed.</param>
/// <param name="ConfiguredMts">Speed it actually runs at; 0 when unknown.</param>
public sealed record RamModule(int SizeMb, int SpeedMts, int ConfiguredMts);

public sealed record DisplayInfo(string Name, int Width, int Height, int CurrentHz, int MaxHz);

public sealed record NetInfo(string Name, bool IsWireless, long SpeedMbps);

public sealed record HardwareInfo(
    string CpuName,
    int LogicalCores,
    IReadOnlyList<GpuInfo> Gpus,
    double RamGb,
    IReadOnlyList<RamModule> RamModules,
    IReadOnlyList<DisplayInfo> Displays,
    double SystemDiskFreePercent,
    bool? SystemDiskIsSsd,
    IReadOnlyList<NetInfo> Networks,
    int WindowsBuild)
{
    /// <summary>The discrete GPU if there is one, else the first.</summary>
    public GpuInfo? MainGpu => Gpus.OrderByDescending(g => g.VramMb).FirstOrDefault();

    /// <summary>Configured / rated RAM speed (1 when unknown).</summary>
    public double RamSpeedRatio
    {
        get
        {
            var m = RamModules.Where(r => r.SpeedMts > 0 && r.ConfiguredMts > 0).ToList();
            return m.Count == 0 ? 1 : m.Min(r => (double)r.ConfiguredMts / r.SpeedMts);
        }
    }
}

public enum IssueSeverity { Info, Warning }

public sealed record HealthIssue(string Id, IssueSeverity Severity, Localized Title, Localized Fix);
