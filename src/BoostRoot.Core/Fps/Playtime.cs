using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoostRoot.Core.IO;

namespace BoostRoot.Core.Fps;

public sealed record PlaySession(string GameId, string GameName, DateTime Start, DateTime End)
{
    [JsonIgnore] public TimeSpan Length => End - Start;
}

[JsonSerializable(typeof(PlaySession))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class PlaytimeJsonContext : JsonSerializerContext;

/// <summary>Playtime per game, recorded by the game session (one JSON line per session).</summary>
public sealed class PlaytimeLog(IFileSystem fs, string path)
{
    public IReadOnlyList<PlaySession> All()
    {
        if (!fs.FileExists(path)) return [];
        var list = new List<PlaySession>();
        foreach (var line in TextFile.Decode(fs.ReadAllBytes(path)).Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            try { if (JsonSerializer.Deserialize(line, PlaytimeJsonContext.Default.PlaySession) is { } s) list.Add(s); } catch (JsonException) { }
        return list;
    }

    public void Add(PlaySession s)
    {
        if (s.Length < TimeSpan.FromMinutes(1)) return;
        var old = fs.FileExists(path) ? fs.ReadAllBytes(path) : [];
        fs.WriteAllBytesAtomic(path, [.. old, .. Encoding.UTF8.GetBytes(JsonSerializer.Serialize(s, PlaytimeJsonContext.Default.PlaySession) + "\n")]);
    }

    public TimeSpan Total(string gameId) => All().Where(s => s.GameId == gameId).Aggregate(TimeSpan.Zero, (a, s) => a + s.Length);

    public TimeSpan Since(DateTime fromUtc) => All().Where(s => s.End >= fromUtc).Aggregate(TimeSpan.Zero, (a, s) => a + (s.Start < fromUtc ? s.End - fromUtc : s.Length));

    public static string Format(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} sa {t.Minutes} dk" : $"{t.Minutes} dk";
}
