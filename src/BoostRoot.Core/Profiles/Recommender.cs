namespace BoostRoot.Core.Profiles;

public static class Recommender
{
    public static BoostLevel Recommend(GameProfile profile, int pcTotalScore)
    {
        foreach (var rule in profile.Recommend)
            if (rule.IfScoreBelow is null || pcTotalScore < rule.IfScoreBelow) return rule.Level;
        return BoostLevel.Balanced;
    }
}
