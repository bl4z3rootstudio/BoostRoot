using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoostRoot.Core.IO;

namespace BoostRoot.Core.Fps;

/// <param name="Low1">"1% low": average FPS of the slowest 1% of frames.</param>
/// <param name="Stutters">Frames that took more than 2.5× the median frame time.</param>
public sealed record FrameStats(int Frames, double Seconds, double AvgFps, double Low1, double Low01, double MedianMs, int Stutters)
{
    public static FrameStats? From(IReadOnlyList<double> frameMs)
    {
        var ft = frameMs.Where(f => f is > 0 and < 1000).ToArray();
        if (ft.Length < 10) return null;
        var total = ft.Sum();
        var sorted = ft.OrderByDescending(f => f).ToArray(); // slowest first
        double LowAvg(double frac) => 1000.0 / sorted.Take(Math.Max(1, (int)Math.Ceiling(sorted.Length * frac))).Average();
        var median = sorted[sorted.Length / 2];
        return new FrameStats(ft.Length, Math.Round(total / 1000, 1), Math.Round(ft.Length / (total / 1000), 1),
            Math.Round(LowAvg(0.01), 1), Math.Round(LowAvg(0.001), 1), Math.Round(median, 2), ft.Count(f => f > median * 2.5));
    }

    /// <summary>Percentage change of average FPS (after vs before).</summary>
    public static double Gain(FrameStats before, FrameStats after) => Math.Round((after.AvgFps - before.AvgFps) / before.AvgFps * 100, 1);
}

/// <summary>Reads PresentMon CSV (v1 "MsBetweenPresents" or v2 "FrameTime" columns), line by line.</summary>
public sealed class PresentMonCsv
{
    private int _col = -1, _appCol = -1;

    /// <summary>Returns the frame time in ms, or null for headers/other lines.</summary>
    public double? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var cells = line.Split(',');
        if (_col < 0)
        {
            _col = Array.FindIndex(cells, c => c.Trim() is "MsBetweenPresents" or "FrameTime");
            _appCol = Array.FindIndex(cells, c => c.Trim() == "Application");
            return null;
        }
        if (_col >= cells.Length) return null;
        return double.TryParse(cells[_col], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
    }
}

/// <summary>Live frame-time stream for one game (PresentMon on Windows).</summary>
public interface IFrameSource : IDisposable
{
    event Action<double>? Frame;
    /// <summary>Set when the source could not start (e.g. PresentMon missing).</summary>
    string? Error { get; }
}

/// <summary>Rolling FPS over the last second, for overlays.</summary>
public sealed class RollingFps
{
    private readonly Queue<double> _q = new();
    private double _sum;
    private readonly Lock _lock = new();

    public void Add(double ms)
    {
        lock (_lock)
        {
            _q.Enqueue(ms); _sum += ms;
            while (_sum > 1000 && _q.Count > 1) _sum -= _q.Dequeue();
        }
    }

    public double? Fps { get { lock (_lock) return _q.Count == 0 ? null : Math.Round(_q.Count / (_sum / 1000)); } }
}

public sealed record FpsRun(string GameId, string GameName, DateTime At, string Label, FrameStats Stats);

[JsonSerializable(typeof(FpsRun))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class FpsJsonContext : JsonSerializerContext;

/// <summary>Append-only FPS history (one JSON object per line).</summary>
public sealed class FpsHistory(IFileSystem fs, string path)
{
    public const string Before = "before", After = "after";

    public IReadOnlyList<FpsRun> All()
    {
        if (!fs.FileExists(path)) return [];
        var list = new List<FpsRun>();
        foreach (var line in TextFile.Decode(fs.ReadAllBytes(path)).Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try { if (JsonSerializer.Deserialize(line, FpsJsonContext.Default.FpsRun) is { } r) list.Add(r); }
            catch (JsonException) { /* skip damaged line */ }
        }
        return list;
    }

    public void Add(FpsRun run)
    {
        var old = fs.FileExists(path) ? fs.ReadAllBytes(path) : [];
        var line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(run, FpsJsonContext.Default.FpsRun) + "\n");
        fs.WriteAllBytesAtomic(path, [.. old, .. line]);
    }

    /// <summary>Latest before/after pair for a game (after must be newer than before).</summary>
    public (FpsRun Before, FpsRun After)? Comparison(string gameId)
    {
        var runs = All().Where(r => r.GameId == gameId).ToList();
        var after = runs.LastOrDefault(r => r.Label == After);
        var before = after is null ? null : runs.LastOrDefault(r => r.Label == Before && r.At <= after.At);
        return before is not null && after is not null ? (before, after) : null;
    }
}
