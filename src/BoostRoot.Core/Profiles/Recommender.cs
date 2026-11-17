namespace BoostRoot.Core.Profiles;

public static class Recommender
{
    /// <param name="gpu">On NVIDIA/AMD, competitive games default to the Competitive level so the driver profile (low latency, max performance) is applied too.</param>
    public static BoostLevel Recommend(GameProfile profile, int pcTotalScore, BoostRoot.Core.Hardware.GpuVendor gpu = BoostRoot.Core.Hardware.GpuVendor.Unknown)
    {
        if (gpu is BoostRoot.Core.Hardware.GpuVendor.Nvidia or BoostRoot.Core.Hardware.GpuVendor.Amd && profile.Competitive
            && profile.Levels.TryGetValue(BoostLevel.Competitive, out var comp) && comp.Gpu is not null)
            return BoostLevel.Competitive;
        foreach (var rule in profile.Recommend)
            if (rule.IfScoreBelow is null || pcTotalScore < rule.IfScoreBelow) return rule.Level;
        return BoostLevel.Balanced;
    }
}
