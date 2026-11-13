namespace BoostRoot.Core.Ui;

/// <summary>Events-per-second over a sliding window (mouse polling rate, monitor refresh rate).</summary>
public sealed class RateMeter(double windowSeconds = 1.0)
{
    private readonly Queue<double> _t = new();
    public double Peak { get; private set; }

    public void Add(double seconds, int count = 1)
    {
        for (var i = 0; i < count; i++) _t.Enqueue(seconds);
        while (_t.Count > 0 && seconds - _t.Peek() > windowSeconds) _t.Dequeue();
        var r = Rate;
        if (r > Peak) Peak = r;
    }

    /// <summary>Rate over the events still inside the window; 0 until there are enough to be meaningful.</summary>
    public double Rate
    {
        get
        {
            if (_t.Count < 5) return 0;
            var span = _t.Last() - _t.Peek();
            return span <= 0 ? 0 : (_t.Count - 1) / span;
        }
    }

    public void Reset() { _t.Clear(); Peak = 0; }

    /// <summary>Snaps a measured refresh rate to the usual monitor rates (59.9 → 60, 143.7 → 144).</summary>
    public static int SnapHz(double hz)
    {
        int[] common = [30, 50, 60, 75, 90, 100, 120, 144, 165, 170, 180, 200, 240, 280, 360, 480, 540];
        var best = common.MinBy(c => Math.Abs(c - hz));
        return Math.Abs(best - hz) <= best * 0.04 ? best : (int)Math.Round(hz);
    }
}
