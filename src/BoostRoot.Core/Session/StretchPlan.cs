using BoostRoot.Core.Profiles;

namespace BoostRoot.Core.Session;

/// <summary>4:3 stretched mode (opt-in per game) — the only feature allowed to touch resolution keys.</summary>
public static class StretchPlan
{
    /// <summary>4:3 width for the panel height, rounded to even (1080 → 1440, 1440 → 1920).</summary>
    public static (int Width, int Height) For(int panelHeight) => ((int)Math.Round(panelHeight * 4 / 3.0 / 2) * 2, panelHeight);

    /// <summary>Values for the profile's stretch keys, chosen by key name.</summary>
    public static IReadOnlyDictionary<string, string> Values(GameProfile profile, int width, int height)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var k in profile.StretchKeys ?? [])
        {
            var key = k[(k.LastIndexOf('/') + 1)..].ToLowerInvariant();
            string? v = key switch
            {
                _ when key.EndsWith("sizex", StringComparison.Ordinal) || key.EndsWith("width", StringComparison.Ordinal) => width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ when key.EndsWith("sizey", StringComparison.Ordinal) || key.EndsWith("height", StringComparison.Ordinal) => height.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ when key.Contains("letterbox", StringComparison.Ordinal) => "False",
                _ when key.Contains("fullscreenmode", StringComparison.Ordinal) => "0",
                _ => null,
            };
            if (v is not null) d[k] = v;
        }
        return d;
    }
}

/// <summary>Watches for the game to start and to exit; owner decides what to do in between.</summary>
public sealed class GameWatcher(Func<IReadOnlyList<string>, bool> isRunning, IReadOnlyList<string> processNames, TimeSpan startTimeout)
{
    public enum State { Waiting, Running, Ended, TimedOut }
    public State Current { get; private set; } = State.Waiting;
    private TimeSpan _waited;

    /// <summary>Call periodically with the elapsed time since the last tick; returns the new state.</summary>
    public State Tick(TimeSpan elapsed)
    {
        var running = isRunning(processNames);
        Current = Current switch
        {
            State.Waiting when running => State.Running,
            State.Waiting when (_waited += elapsed) >= startTimeout => State.TimedOut,
            State.Running when !running => State.Ended,
            var s => s,
        };
        return Current;
    }
}
