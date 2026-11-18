using System.Text.RegularExpressions;

namespace BoostRoot.Core.Tools;

/// <summary>Minecraft Java memory (-Xmx): too little = lag spikes / out of memory, too much = long garbage-collection freezes.</summary>
public static partial class MinecraftRam
{
    /// <summary>What the official launcher uses when a profile has no custom JVM arguments.</summary>
    public const string LauncherDefaultArgs = "-Xmx2G -XX:+UnlockExperimentalVMOptions -XX:+UseG1GC -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M";

    [GeneratedRegex(@"-Xmx(\d+)([gGmMkK]?)")]
    private static partial Regex Xmx();

    /// <summary>-Xmx in MB, or null if the arguments do not set one.</summary>
    public static int? ParseXmxMb(string? args)
    {
        if (string.IsNullOrWhiteSpace(args) || Xmx().Match(args) is not { Success: true } m || !long.TryParse(m.Groups[1].Value, out var n)) return null;
        return (int)Math.Min(int.MaxValue, m.Groups[2].Value.ToLowerInvariant() switch { "g" => n * 1024, "m" => n, "k" => n / 1024, _ => n / (1024 * 1024) });
    }

    /// <summary>Same arguments with -Xmx set to <paramref name="gb"/> (added in front if missing).</summary>
    public static string WithXmx(string? args, int gb)
    {
        var a = string.IsNullOrWhiteSpace(args) ? LauncherDefaultArgs : args.Trim();
        return Xmx().IsMatch(a) ? Xmx().Replace(a, $"-Xmx{gb}G", 1) : $"-Xmx{gb}G {a}";
    }

    /// <summary>A sane amount for this PC: 4 GB on 8–15 GB RAM, 6 GB on 16–31 GB, 8 GB on 32 GB+ (heavy modpacks).</summary>
    public static int RecommendedGb(double totalRamGb) => totalRamGb >= 31 ? 8 : totalRamGb >= 15 ? 6 : totalRamGb >= 7 ? 4 : 2;

    /// <summary>"low", "high" or null when the setting is fine.</summary>
    public static string? Verdict(int xmxMb, double totalRamGb)
    {
        if (totalRamGb <= 0) return null;
        if (xmxMb < 3 * 1024 && totalRamGb >= 7) return "low";
        if (xmxMb > totalRamGb * 1024 * 0.6) return "high";
        return null;
    }
}
