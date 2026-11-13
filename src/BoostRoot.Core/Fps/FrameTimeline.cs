namespace BoostRoot.Core.Fps;

/// <summary>Frame-time graph data + cross-game FPS table.</summary>
public static class FrameTimeline
{
    /// <summary>Squeezes many frame times into at most <paramref name="points"/> buckets, keeping each bucket's worst frame so stutters stay visible.</summary>
    public static IReadOnlyList<double> Downsample(IReadOnlyList<double> frameMs, int points = 300)
    {
        if (frameMs.Count <= points) return [.. frameMs];
        var r = new double[points];
        for (var i = 0; i < points; i++)
        {
            int a = (int)((long)i * frameMs.Count / points), b = (int)((long)(i + 1) * frameMs.Count / points);
            var m = 0.0;
            for (var j = a; j < b; j++) if (frameMs[j] > m) m = frameMs[j];
            r[i] = m;
        }
        return r;
    }

    /// <summary>Nice upper bound for the graph (e.g. 8.3 → 10, 23 → 25, 41 → 50 ms).</summary>
    public static double Ceiling(IReadOnlyList<double> v)
    {
        var max = v.Count == 0 ? 16.7 : v.Max();
        foreach (var c in new[] { 5.0, 10, 20, 25, 33.4, 50, 100, 200 }) if (max <= c) return c;
        return 500;
    }

    /// <summary>Latest run of each game (any label), best FPS first.</summary>
    public static IReadOnlyList<FpsRun> LatestPerGame(IEnumerable<FpsRun> runs) =>
        [.. runs.GroupBy(r => r.GameId).Select(g => g.OrderBy(r => r.At).Last()).OrderByDescending(r => r.Stats.AvgFps)];
}
