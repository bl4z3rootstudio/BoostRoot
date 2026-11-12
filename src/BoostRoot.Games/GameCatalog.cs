using BoostRoot.Core.Profiles;

namespace BoostRoot.Games;

public static class GameCatalog
{
    /// <summary>Joins profiles with installed games via the profile's "detect" map. Installed first, then by name.</summary>
    public static IReadOnlyList<CatalogEntry> Match(IEnumerable<GameProfile> profiles, IEnumerable<InstalledGame> installed)
    {
        var games = installed.ToList();
        var used = new HashSet<InstalledGame>();
        var entries = new List<CatalogEntry>();
        foreach (var p in profiles)
        {
            var g = games.FirstOrDefault(g => !used.Contains(g) && Matches(p, g));
            if (g is not null) used.Add(g);
            entries.Add(new CatalogEntry(p, g));
        }
        return [.. entries.OrderByDescending(e => e.IsInstalled).ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public static bool Matches(GameProfile p, InstalledGame g)
    {
        if (p.Detect.TryGetValue(g.Source, out var id) && string.Equals(id, g.SourceId, StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Detect.TryGetValue("exe", out var exe) && g.Extra.TryGetValue("exe", out var gexe) &&
            string.Equals(Path.GetFileName(gexe.Replace('\\', '/')), exe, StringComparison.OrdinalIgnoreCase)) return true;
        return p.Detect.TryGetValue("name", out var name) && string.Equals(name, g.Name, StringComparison.OrdinalIgnoreCase);
    }
}

public static class LaunchCommands
{
    /// <summary>How to start the game through its own launcher; null means "the user starts it".</summary>
    public static LaunchCommand? For(InstalledGame g) => g.Source switch
    {
        "steam" => new LaunchCommand($"steam://rungameid/{g.SourceId}", null, true),
        "epic" when g.Extra.TryGetValue("namespace", out var ns) && ns.Length > 0 && g.Extra.TryGetValue("itemId", out var item) && item.Length > 0 =>
            new LaunchCommand($"com.epicgames.launcher://apps/{Uri.EscapeDataString(ns)}%3A{Uri.EscapeDataString(item)}%3A{Uri.EscapeDataString(g.SourceId)}?action=launch&silent=true", null, true),
        "riot" when g.Extra.TryGetValue("client", out var c) && c.Length > 0 =>
            new LaunchCommand(c.Replace('/', '\\'), $"--launch-product={g.SourceId} --launch-patchline=live", false),
        _ => null,
    };
}
