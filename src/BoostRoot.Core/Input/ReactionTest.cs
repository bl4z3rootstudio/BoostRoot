namespace BoostRoot.Core.Input;

public enum ReactionState { Idle, Waiting, Go, Result, TooEarly }

/// <summary>"Click when it turns green" test. Pure state machine: the caller supplies the clock (ms) and the random delay.</summary>
public sealed class ReactionTest
{
    private readonly List<double> _results = [];
    private double _goAt;

    public ReactionState State { get; private set; } = ReactionState.Idle;
    public double? Last { get; private set; }
    public IReadOnlyList<double> Results => _results;
    public double? Average => _results.Count > 0 ? _results.Average() : null;
    public double? Best => _results.Count > 0 ? _results.Min() : null;
    /// <summary>When the screen should turn green (ms on the caller's clock), while waiting.</summary>
    public double GoAt => _goAt;

    /// <summary>Starts a round: the screen turns green <paramref name="delayMs"/> after <paramref name="nowMs"/>.</summary>
    public void Start(double nowMs, double delayMs) { _goAt = nowMs + delayMs; State = ReactionState.Waiting; }

    /// <summary>Call from a timer; flips to Go once the delay has passed.</summary>
    public bool Tick(double nowMs)
    {
        if (State != ReactionState.Waiting || nowMs < _goAt) return false;
        State = ReactionState.Go;
        return true;
    }

    /// <summary>The player clicked. Returns the new state.</summary>
    public ReactionState Click(double nowMs)
    {
        Tick(nowMs);
        switch (State)
        {
            case ReactionState.Waiting: State = ReactionState.TooEarly; break;
            case ReactionState.Go:
                Last = nowMs - _goAt;
                _results.Add(Last.Value);
                if (_results.Count > 5) _results.RemoveAt(0);
                State = ReactionState.Result;
                break;
        }
        return State;
    }

    /// <summary>Rough rating key for a reaction time in ms (human average ~250 ms on a 60 Hz screen).</summary>
    public static string Rating(double ms) => ms < 180 ? "pro" : ms < 230 ? "good" : ms < 290 ? "avg" : "slow";
}

/// <summary>
/// Spots mouse switch "chatter": one physical press reported as two clicks. A real double-click needs ~70 ms or more
/// between presses; anything faster is almost certainly a worn switch.
/// </summary>
public sealed class ClickChatter(double thresholdMs = 45)
{
    private double? _last;
    public int Presses { get; private set; }
    public int Chatters { get; private set; }

    /// <summary>Records a left press; returns true if it looks like chatter.</summary>
    public bool Press(double nowMs)
    {
        Presses++;
        var bad = _last is { } l && nowMs - l >= 0 && nowMs - l < thresholdMs;
        if (bad) Chatters++;
        _last = nowMs;
        return bad;
    }

    public void Reset() { _last = null; Presses = 0; Chatters = 0; }
}
