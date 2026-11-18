using System.Text.RegularExpressions;

namespace BoostRoot.Core.Fps;

/// <param name="Low">Low end of the estimated range (1080p, the game's fast/competitive settings).</param>
/// <param name="Limit">"gpu" or "cpu": which part holds the FPS back.</param>
public sealed record FpsEstimate(int Low, int High, string Limit, int? Cap);

/// <summary>Rough FPS range for a game on this PC, from built-in relative performance tables (no internet, no guessing per game
/// beyond a published-benchmark-like baseline). Always shown as an estimate next to the real measurement.</summary>
public static partial class FpsEstimator
{
    // Relative 1080p raster performance, RTX 3060 = 100 (rounded, from public benchmark averages).
    private static readonly (string Key, int Score)[] Gpus =
    [
        ("5090", 420), ("5080", 300), ("5070 ti", 250), ("5070", 200), ("5060 ti", 150), ("5060", 130),
        ("4090", 330), ("4080", 270), ("4070 ti", 215), ("4070 super", 200), ("4070", 180), ("4060 ti", 140), ("4060", 115),
        ("3090", 220), ("3080 ti", 215), ("3080", 200), ("3070 ti", 165), ("3070", 155), ("3060 ti", 135), ("3060", 100), ("3050", 70),
        ("2080 ti", 160), ("2080 super", 140), ("2080", 130), ("2070 super", 125), ("2070", 110), ("2060 super", 105), ("2060", 90),
        ("1660 super", 75), ("1660 ti", 75), ("1660", 70), ("1650 super", 60), ("1650", 50), ("1080 ti", 120), ("1080", 95), ("1070 ti", 88), ("1070", 80), ("1060", 60), ("1050 ti", 38),
        ("9070 xt", 270), ("9070", 240), ("9060 xt", 150),
        ("7900 xtx", 280), ("7900 xt", 240), ("7900 gre", 205), ("7800 xt", 190), ("7700 xt", 165), ("7600 xt", 120), ("7600", 115),
        ("6950 xt", 220), ("6900 xt", 205), ("6800 xt", 195), ("6800", 170), ("6750 xt", 145), ("6700 xt", 140), ("6700", 125),
        ("6650 xt", 115), ("6600 xt", 110), ("6600", 100), ("6500 xt", 55), ("6400", 42), ("5700 xt", 105), ("5700", 95), ("5600 xt", 85), ("5500 xt", 60),
        ("rx 590", 60), ("rx 580", 55), ("rx 570", 50), ("arc b580", 125), ("arc a770", 115), ("arc a750", 105), ("arc a580", 90),
    ];

    /// <summary>Baseline FPS at RTX 3060 / Ryzen 5 5600-class CPU, 1080p, the profile's competitive/low settings: (GPU-bound, CPU-bound, cap).</summary>
    private static readonly Dictionary<string, (int Gpu, int Cpu, int? Cap)> Games = new()
    {
        ["valorant"] = (480, 380, null), ["cs2"] = (260, 300, null), ["fortnite"] = (220, 260, null), ["league-of-legends"] = (500, 450, null),
        ["apex-legends"] = (180, 230, 300), ["rainbow-six-siege"] = (320, 360, null), ["rocket-league"] = (360, 420, null), ["pubg"] = (170, 200, null),
        ["gta-v"] = (150, 170, 187), ["red-dead-redemption-2"] = (75, 140, null), ["cyberpunk-2077"] = (70, 130, null), ["elden-ring"] = (95, 130, 60),
        ["minecraft"] = (300, 260, null), ["roblox"] = (300, 280, null), ["palworld"] = (90, 140, null), ["hogwarts-legacy"] = (75, 110, null),
        ["forza-horizon-5"] = (120, 190, null), ["rust"] = (100, 110, null), ["valheim"] = (110, 150, null), ["sons-of-the-forest"] = (85, 120, null),
        ["euro-truck-simulator-2"] = (140, 150, null), ["phasmophobia"] = (140, 190, null), ["lethal-company"] = (180, 220, null),
        ["the-last-of-us-part-1"] = (70, 110, null), ["the-last-of-us-part-2"] = (80, 120, null), ["7-days-to-die"] = (70, 100, null),
        ["stranded-deep"] = (120, 160, null), ["peak"] = (140, 180, null), ["ranch-simulator"] = (110, 150, null), ["banker-simulator"] = (130, 170, null),
    };

    /// <summary>How much of the low-settings GPU speed is left at each preset (typical across modern games).</summary>
    public static readonly (string Key, double Gpu)[] Presets = [("low", 1.0), ("medium", 0.72), ("high", 0.55), ("ultra", 0.42)];

    /// <summary>One range per graphics preset. Unknown games use a genre-wide baseline (flagged as general).</summary>
    public static IReadOnlyList<(string Preset, int Low, int High)> ByPreset(string profileId, bool competitive, string gpuName, string cpuName, out bool general)
    {
        general = !Games.ContainsKey(profileId);
        (int Gpu, int Cpu, int? Cap) g = Games.TryGetValue(profileId, out var known) ? known : competitive ? (260, 280, null) : (100, 140, null);
        if (GpuScore(gpuName) is not { } gs) return [];
        var cpuFps = g.Cpu * CpuFactor(cpuName);
        var list = new List<(string, int, int)>();
        foreach (var (key, mul) in Presets)
        {
            var mid = Math.Min(g.Gpu * gs / 100.0 * mul, cpuFps);
            int low = (int)Math.Round(mid * 0.8 / 5) * 5, high = (int)Math.Round(mid * 1.2 / 5) * 5;
            if (g.Cap is { } cap) { low = Math.Min(low, cap); high = Math.Min(high, cap); }
            list.Add((key, Math.Max(low, 10), Math.Max(high, 15)));
        }
        return list;
    }

    /// <summary>Free, well-known ways to get more FPS in specific games (shown as advice; BoostRoot does not install mods).</summary>
    public static string? TipKey(string profileId) => profileId switch
    {
        "minecraft" => "fps.tipMinecraft",
        "valorant" => "fps.tipValorant",
        "cs2" => "fps.tipCs2",
        "fortnite" => "fps.tipFortnite",
        "gta-v" => "fps.tipGta",
        _ => null,
    };

    public static int? GpuScore(string gpuName)
    {
        var n = gpuName.ToLowerInvariant().Replace("geforce", "").Replace("radeon", "").Replace("nvidia", "").Replace("amd", "");
        n = Spaces().Replace(n, " ").Trim();
        foreach (var (key, score) in Gpus)
            if (n.Contains(key, StringComparison.Ordinal)) return score;
        return null;
    }

    /// <summary>CPU gaming speed vs a Ryzen 5 5600 (1.0), from the model generation in the name.</summary>
    public static double CpuFactor(string cpuName)
    {
        var n = cpuName.ToLowerInvariant();
        if (X3d9k().IsMatch(n)) return 1.45;
        if (X3d7k().IsMatch(n)) return 1.35;
        if (Ryzen9k().IsMatch(n) || Ryzen7k().IsMatch(n)) return 1.2;
        if (X3d5k().IsMatch(n)) return 1.2;
        if (Ryzen5k().IsMatch(n)) return 1.0;
        if (Ryzen3k().IsMatch(n)) return 0.8;
        if (Ryzen12k().IsMatch(n)) return 0.62;
        if (IntelUltra().IsMatch(n)) return 1.15;
        if (Intel1214().IsMatch(n)) return 1.15;
        if (Intel11().IsMatch(n)) return 0.92;
        if (Intel10().IsMatch(n)) return 0.85;
        if (Intel89().IsMatch(n)) return 0.75;
        return 0.6;
    }

    public static FpsEstimate? Estimate(string profileId, string gpuName, string cpuName, bool competitive = false, bool allowGeneral = false)
    {
        (int Gpu, int Cpu, int? Cap) g;
        if (Games.TryGetValue(profileId, out var known)) g = known;
        else if (allowGeneral) g = competitive ? (260, 280, null) : (100, 140, null);
        else return null;
        if (GpuScore(gpuName) is not { } gs) return null;
        var gpuFps = g.Gpu * gs / 100.0;
        var cpuFps = g.Cpu * CpuFactor(cpuName);
        var mid = Math.Min(gpuFps, cpuFps);
        var limit = gpuFps <= cpuFps ? "gpu" : "cpu";
        int low = (int)Math.Round(mid * 0.8 / 5) * 5, high = (int)Math.Round(mid * 1.2 / 5) * 5;
        if (g.Cap is { } cap) { low = Math.Min(low, cap); high = Math.Min(high, cap); }
        return new FpsEstimate(Math.Max(low, 10), Math.Max(high, 15), limit, g.Cap);
    }

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"ryzen.*9\d{3}x3d")] private static partial Regex X3d9k();
    [GeneratedRegex(@"ryzen.*7\d{3}x3d")] private static partial Regex X3d7k();
    [GeneratedRegex(@"ryzen.*5\d{3}x3d")] private static partial Regex X3d5k();
    [GeneratedRegex(@"ryzen \d+ 9\d{3}")] private static partial Regex Ryzen9k();
    [GeneratedRegex(@"ryzen \d+ 7\d{3}")] private static partial Regex Ryzen7k();
    [GeneratedRegex(@"ryzen \d+ 5\d{3}")] private static partial Regex Ryzen5k();
    [GeneratedRegex(@"ryzen \d+ 3\d{3}")] private static partial Regex Ryzen3k();
    [GeneratedRegex(@"ryzen \d+ [12]\d{3}")] private static partial Regex Ryzen12k();
    [GeneratedRegex(@"core\(tm\) ultra|core ultra")] private static partial Regex IntelUltra();
    [GeneratedRegex(@"i[3579]-1[234]\d{3}")] private static partial Regex Intel1214();
    [GeneratedRegex(@"i[3579]-11\d{3}")] private static partial Regex Intel11();
    [GeneratedRegex(@"i[3579]-10\d{3}")] private static partial Regex Intel10();
    [GeneratedRegex(@"i[3579]-[89]\d{3}")] private static partial Regex Intel89();
}
