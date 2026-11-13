using BoostRoot.Games;

namespace BoostRoot.ViewModels;

/// <summary>
/// "Auto Boost": notices when an installed game starts outside BoostRoot and attaches the light, game-safe parts
/// (session: high priority, timer resolution, overlay hotkeys, FPS log; closing the chosen background apps).
/// Game settings files are never touched here — those only change while the game is closed.
/// </summary>
public sealed class AutoBoost
{
    private readonly HashSet<string> _handled = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ProcessNames(CatalogEntry e) =>
        e.Profile?.Detect.TryGetValue("process", out var p) == true
            ? [.. p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(n => !n.StartsWith("dir:", StringComparison.Ordinal)).Select(n => Path.GetFileNameWithoutExtension(n))]
            : [];

    /// <summary>The newly started game, if any. A game is reported once per run; it is re-armed after it closes.</summary>
    public CatalogEntry? Check(IEnumerable<CatalogEntry> installed, IReadOnlyCollection<string> running)
    {
        var set = running.Select(r => Path.GetFileNameWithoutExtension(r)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        CatalogEntry? hit = null;
        foreach (var e in installed)
        {
            if (!e.IsInstalled || e.Profile is not { } p) continue;
            var names = ProcessNames(e);
            var isRunning = names.Count > 0 && names.Any(set.Contains);
            if (!isRunning) { _handled.Remove(p.Id); continue; }
            if (_handled.Add(p.Id)) hit ??= e;
        }
        return hit;
    }

    /// <summary>Marks a game as already handled (e.g. it was started with the Boost button).</summary>
    public void MarkHandled(string profileId) => _handled.Add(profileId);
}
