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
