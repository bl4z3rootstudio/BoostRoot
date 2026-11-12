using System.Text.RegularExpressions;
using BoostRoot.Core.Config;
using BoostRoot.Core.Safety;

namespace BoostRoot.Core.Profiles;

public static partial class ProfileValidator
{
    private static readonly string[] AntiCheats = ["none", "vanguard", "eac", "battleye", "vac", "ricochet", "javelin", "other"];
    private static readonly string[] Picks = ["newest", "all", "first"];

    [GeneratedRegex("^[a-z0-9-]{2,40}$")]
    private static partial Regex IdPattern();

    public static IReadOnlyList<string> Validate(GameProfile p)
    {
        var e = new List<string>();
        if (!IdPattern().IsMatch(p.Id ?? "")) e.Add("id must match ^[a-z0-9-]{2,40}$");
        if (string.IsNullOrWhiteSpace(p.Name)) e.Add("name is required");
        if (p.SchemaVersion != 1) e.Add("schemaVersion must be 1");
        if (!AntiCheats.Contains(p.AntiCheat)) e.Add($"unknown antiCheat '{p.AntiCheat}'");
        if (p.Group is not ("A" or "B")) e.Add("group must be A or B");
        if (p.Group == "A" && p.Configs.Count == 0) e.Add("group A needs at least one config target");

        var configIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in p.Configs)
        {
            if (!configIds.Add(c.Id)) e.Add($"duplicate config id '{c.Id}'");
            if (!ConfigFormats.Names.Contains(c.Format)) e.Add($"config '{c.Id}' has unknown format '{c.Format}'");
            if (!Picks.Contains(c.Pick)) e.Add($"config '{c.Id}' has unknown pick '{c.Pick}'");
            if (string.IsNullOrWhiteSpace(c.Path)) e.Add($"config '{c.Id}' has no path");
        }

        NeverTouchGuard? guard = null;
        try { guard = new NeverTouchGuard(p.NeverTouch); }
        catch (ArgumentException ex) { e.Add(ex.Message); }

        foreach (var level in Enum.GetValues<BoostLevel>())
        {
            if (!p.Levels.TryGetValue(level, out var spec)) { e.Add($"missing level '{level}'"); continue; }
            foreach (var key in spec.Set.Keys)
            {
                var (cid, k) = GameProfile.SplitSetKey(key);
                if (cid.Length == 0) e.Add($"set key '{key}' must be 'configId:key'");
                else if (!configIds.Contains(cid)) e.Add($"set key '{key}' references unknown config '{cid}'");
                if (k.Contains("fov", StringComparison.OrdinalIgnoreCase)) e.Add($"set key '{key}': FOV is player preference");
                if (guard is not null && cid.Length > 0 && guard.IsProtected(cid, k)) e.Add($"set key '{key}' is protected");
            }
            if (p.Competitive && spec.Gpu?.Upscaler == "quality") e.Add($"competitive profile must not enable upscaler ({level})");
            if (p.Competitive && level == BoostLevel.Competitive && spec.Gpu?.RadeonBoost == true) e.Add("Radeon Boost must be off for competitive level");
        }

        if (p.Recommend.Count == 0 || p.Recommend[^1].IfScoreBelow is not null) e.Add("recommend must end with a default rule (no ifScoreBelow)");
        foreach (var sk in p.StretchKeys ?? [])
        {
            var (cid, _) = GameProfile.SplitSetKey(sk);
            if (!configIds.Contains(cid)) e.Add($"stretch key '{sk}' references unknown config '{cid}'");
        }
        return e;
    }
}
