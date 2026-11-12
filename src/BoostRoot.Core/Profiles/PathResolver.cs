using System.Text.RegularExpressions;
using BoostRoot.Core.IO;

namespace BoostRoot.Core.Profiles;

/// <summary>Expands %VARS%, resolves '*'/'?' in any path segment and applies the target's pick rule.</summary>
public sealed class PathResolver(IFileSystem fs, Func<string, string> expandEnvironment)
{
    public IReadOnlyList<string> Resolve(ConfigTarget target)
    {
        var expanded = expandEnvironment(target.Path).Replace('\\', '/');
        var parts = expanded.Split('/');
        var candidates = new List<string> { parts[0] };
        for (var i = 1; i < parts.Length; i++)
        {
            var seg = parts[i];
            if (seg.Length == 0) continue;
            var last = i == parts.Length - 1;
            var wildcard = seg.Contains('*') || seg.Contains('?');
            var next = new List<string>();
            foreach (var c in candidates)
            {
                var dir = c.Length == 0 ? "/" : c;
                if (!wildcard)
                {
                    var p = Join(c, seg);
                    if (last ? fs.FileExists(p) : fs.DirectoryExists(p)) next.Add(p);
                }
                else if (last)
                {
                    next.AddRange(fs.EnumerateFiles(dir, seg, false).Select(Normalize));
                }
                else
                {
                    var rx = Glob(seg);
                    next.AddRange(fs.EnumerateDirectories(dir).Select(Normalize).Where(d => rx.IsMatch(Name(d))));
                }
            }
            candidates = next;
            if (candidates.Count == 0) return [];
        }

        var files = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return target.Pick switch
        {
            "all" => files.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            "first" => files.Order(StringComparer.OrdinalIgnoreCase).Take(1).ToList(),
            _ => files.OrderByDescending(fs.GetLastWriteTimeUtc).Take(1).ToList(),
        };
    }

    private static string Join(string a, string b) => a.Length == 0 ? "/" + b : a.TrimEnd('/') + "/" + b;
    private static string Normalize(string p) => p.Replace('\\', '/');
    private static string Name(string p) => p[(p.TrimEnd('/').LastIndexOf('/') + 1)..];

    private static Regex Glob(string g) =>
        new("^" + Regex.Escape(g).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
