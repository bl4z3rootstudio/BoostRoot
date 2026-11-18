namespace BoostRoot.Core.Monitoring;

/// <summary>One reading of every sensor BoostRoot knows. Null = not available on this PC.</summary>
public sealed record SensorSnapshot(
    DateTime At,
    double? CpuLoad = null,
    double? CpuMhz = null,
    bool CpuThrottled = false,
    double? CpuTempC = null,
    double? GpuLoad = null,
    double? GpuTempC = null,
    double? GpuHotspotC = null,
    double? GpuClockMhz = null,
    double? GpuPowerW = null,
    double? VramUsedMb = null,
    double? RamUsedPct = null,
    double? Fps = null)
{
    public static SensorSnapshot Empty(DateTime at) => new(at);
}

public interface ISensors : IDisposable
{
    /// <summary>Cheap: safe to call once per second.</summary>
    SensorSnapshot Read();
}

/// <summary>Temperature limits that stop a stress test immediately.</summary>
public sealed record StressLimits(double GpuEdgeMaxC = 90, double GpuHotspotMaxC = 105, double CpuMaxC = 95)
{
    public static StressLimits Default { get; } = new();

    /// <summary>Returns a stop reason key (localized by the UI) or null when it is safe to continue.</summary>
    public string? Check(SensorSnapshot s) =>
        s.GpuTempC >= GpuEdgeMaxC ? "stress.stop.gpuTemp"
        : s.GpuHotspotC >= GpuHotspotMaxC ? "stress.stop.gpuHotspot"
        : s.CpuTempC >= CpuMaxC ? "stress.stop.cpuTemp"
        : null;
}

/// <summary>
/// Some drivers report no real hotspot and just repeat the edge temperature (or return junk). A real hotspot is
/// always above the edge reading, so it is shown only after it has been seen clearly higher at least once.
/// Works the same for every NVIDIA / AMD card: cards without a real sensor simply never show it.
/// </summary>
public sealed class HotspotFilter
{
    private bool _real;
    public double? Apply(double? edgeC, double? hotspotC)
    {
        if (hotspotC is not { } h || h <= 0 || h > 150) return null;
        if (edgeC is { } e)
        {
            if (h < e - 0.5) return null;          // below the edge: not a hotspot reading
            if (h > e + 1.5) _real = true;          // clearly hotter than the edge once → a real sensor
        }
        return _real ? h : null;
    }
}
