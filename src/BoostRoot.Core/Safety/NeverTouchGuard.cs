using System.Text.RegularExpressions;

namespace BoostRoot.Core.Safety;

/// <summary>
/// Refuses writes to settings that belong to the player (resolution, window mode, keybinds, sensitivity,
/// crosshair, language, volume). Built-in patterns apply to any path segment; profile patterns are "configId:glob".
/// </summary>
public sealed class NeverTouchGuard
{
    public static IReadOnlyList<string> BuiltInKeyPatterns { get; } =
    [
        "resolution*", "*resolutionsize*", "*screenres*", "*displayres*", "*windowedres*", "*fullscreenres*",
        "*screenwidth*", "*screenheight*", "*sensitivity*", "*sens", "*crosshair*", "*keybind*", "*binding*", "*actionmapping*",
        "*axismapping*", "*fullscreenmode*", "*windowmode*", "*displaymode*", "*language*", "*culture*",
        "*volume", "volume", "*mousespeed*",
    ];

    private static readonly Regex[] BuiltIn = [.. BuiltInKeyPatterns.Select(ToRegex)];
    private readonly List<(Regex Config, Regex Key)> _profile;
    private readonly HashSet<string> _exempt;

    public NeverTouchGuard(IEnumerable<string> profilePatterns) : this(Parse(profilePatterns), []) { }

    private NeverTouchGuard(List<(Regex, Regex)> profile, HashSet<string> exempt)
    {
        _profile = profile;
        _exempt = exempt;
    }

    private static List<(Regex, Regex)> Parse(IEnumerable<string> patterns)
    {
        var list = new List<(Regex, Regex)>();
        foreach (var p in patterns)
        {
            var colon = p.IndexOf(':');
            if (colon <= 0) throw new ArgumentException($"Never-touch pattern '{p}' must be 'configId:glob'.", nameof(patterns));
            list.Add((ToRegex(p[..colon]), ToRegex(p[(colon + 1)..])));
        }
        return list;
    }

    private static Regex ToRegex(string glob) =>
        new("^" + Regex.Escape(glob).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Returns a guard that additionally allows the exact "configId:key" entries (4:3 stretched mode only).</summary>
    public NeverTouchGuard WithExemptions(IEnumerable<string> exactConfigKeys) =>
        new(_profile, new HashSet<string>(_exempt.Concat(exactConfigKeys), StringComparer.OrdinalIgnoreCase));

    public bool IsProtected(string configId, string key)
    {
        if (_exempt.Contains(configId + ":" + key)) return false;
        foreach (var segment in key.Split('/', '@', '.'))
            foreach (var r in BuiltIn)
                if (r.IsMatch(segment)) return true;
        foreach (var (c, k) in _profile)
            if (c.IsMatch(configId) && k.IsMatch(key)) return true;
        return false;
    }

    public void EnsureAllowed(string configId, string key)
    {
        if (IsProtected(configId, key)) throw new ProtectedSettingException(configId, key);
    }
}
