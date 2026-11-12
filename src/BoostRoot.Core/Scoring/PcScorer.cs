namespace BoostRoot.Core.Scoring;

/// <param name="GpuScore">0–100 from the hardware tables.</param>
/// <param name="CpuScore">0–100 from the hardware tables.</param>
/// <param name="RamSpeedRatio">Running speed / supported speed; 1 when unknown.</param>
public sealed record PcSpecs(int GpuScore, int CpuScore, double RamGb, double RamSpeedRatio, bool SystemDiskIsSsd, double SystemDiskFreePercent, int HealthIssueCount);

public sealed record PcScore(int Total, int Gpu, int Cpu, int Ram, int Disk, int Health);

public static class PcScorer
{
    public static PcScore Score(PcSpecs s)
    {
        var gpu = Clamp(s.GpuScore);
        var cpu = Clamp(s.CpuScore);
        var ram = Clamp(Round(Math.Min(Math.Max(s.RamGb, 0), 32) / 32 * 100 * (s.RamSpeedRatio < 0.95 ? 0.85 : 1)));
        var disk = Clamp((s.SystemDiskIsSsd ? 100 : 40) - (s.SystemDiskFreePercent < 10 ? 30 : 0));
        var health = Clamp(100 - 20 * Math.Max(0, s.HealthIssueCount));
        var total = Clamp(Round(0.45 * gpu + 0.25 * cpu + 0.15 * ram + 0.05 * disk + 0.10 * health));
        return new PcScore(total, gpu, cpu, ram, disk, health);
    }

    private static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
    private static int Clamp(int v) => Math.Clamp(v, 0, 100);
}
