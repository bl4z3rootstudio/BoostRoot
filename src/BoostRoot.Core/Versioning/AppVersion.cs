using System.Globalization;
using System.Reflection;

namespace BoostRoot.Core.Versioning;

/// <summary>BoostRoot version in the form YY.MM.N (year, month, release within that month).</summary>
public readonly record struct AppVersion(int Year, int Month, int Release) : IComparable<AppVersion>
{
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        var parts = s.Split('.');
        if (parts.Length != 3) return false;
        if (!TryInt(parts[0], out var y) || !TryInt(parts[1], out var m) || !TryInt(parts[2], out var r)) return false;
        if (y is < 0 or > 99 || m is < 1 or > 12 || r < 1) return false;
        version = new AppVersion(y, m, r);
        return true;
    }

    public static AppVersion Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"Invalid BoostRoot version '{text}'. Expected YY.MM.N.");

    private static bool TryInt(string s, out int value) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    public int CompareTo(AppVersion other)
    {
        var c = Year.CompareTo(other.Year);
        if (c != 0) return c;
        c = Month.CompareTo(other.Month);
        return c != 0 ? c : Release.CompareTo(other.Release);
    }

    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Year}.{Month}.{Release}");

    public string ToFileVersion() => string.Create(CultureInfo.InvariantCulture, $"{Year}.{Month}.{Release}.0");

    /// <summary>The version this assembly was built as.</summary>
    public static AppVersion Current { get; } = Parse(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? "26.11.1");
}
