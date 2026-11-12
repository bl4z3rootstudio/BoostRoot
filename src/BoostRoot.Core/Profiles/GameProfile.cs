namespace BoostRoot.Core.Profiles;

public sealed record Localized(string Tr, string En);

/// <param name="Pick">newest | all | first</param>
public sealed record ConfigTarget(string Id, string Format, string Path, string Pick = "newest", string? Separator = null);

/// <param name="Upscaler">null | "off" | "quality"</param>
public sealed record GpuPrefs(
    bool? LowLatency = null,
    bool? VSync = null,
    bool? RadeonBoost = null,
    bool? Chill = null,
    string? TextureFiltering = null,
    string? Upscaler = null);

/// <param name="FpsImpact">low | medium | high</param>
/// <param name="VisualImpact">low | medium | high</param>
public sealed record SettingNote(Localized Text, string FpsImpact, string VisualImpact);

/// <param name="Set">Keys are "configId:key".</param>
public sealed record LevelSpec(IReadOnlyDictionary<string, string> Set, GpuPrefs? Gpu = null);

/// <param name="IfScoreBelow">null marks the default (last) rule.</param>
public sealed record RecommendRule(int? IfScoreBelow, BoostLevel Level);

/// <param name="AntiCheat">none | vanguard | eac | battleye | vac | ricochet | javelin | other</param>
/// <param name="Group">A = game settings editable; B = driver/Windows only + guide card</param>
/// <param name="StretchKeys">"configId:key" entries the 4:3 stretched mode may write.</param>
public sealed record GameProfile(
    string Id,
    string Name,
    int SchemaVersion,
    string AntiCheat,
    bool Competitive,
    string Group,
    IReadOnlyDictionary<string, string> Detect,
    IReadOnlyDictionary<string, string> Launch,
    IReadOnlyList<ConfigTarget> Configs,
    IReadOnlyList<string> NeverTouch,
    IReadOnlyDictionary<BoostLevel, LevelSpec> Levels,
    IReadOnlyList<RecommendRule> Recommend,
    IReadOnlyDictionary<string, SettingNote> Notes,
    Localized? Tips,
    string VerifiedOn,
    IReadOnlyList<string>? StretchKeys = null)
{
    public bool HasAntiCheat => AntiCheat is not "none";

    internal static (string ConfigId, string Key) SplitSetKey(string configKey)
    {
        var colon = configKey.IndexOf(':');
        return colon <= 0 ? ("", configKey) : (configKey[..colon], configKey[(colon + 1)..]);
    }
}
