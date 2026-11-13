using System.Text.Json;
using System.Text.Json.Serialization;
using BoostRoot.Core.Versioning;

namespace BoostRoot.Core.Updates;

public sealed record ReleaseAsset([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("browser_download_url")] string Url, [property: JsonPropertyName("size")] long Size);

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string Tag,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] IReadOnlyList<ReleaseAsset> Assets);

[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;

/// <param name="Setup">Installer asset; <param name="Sha256">expected hash from SHA256SUMS.txt.</param></param>
public sealed record UpdateInfo(AppVersion Version, string Notes, ReleaseAsset Setup, string? Sha256);

/// <summary>GitHub Releases based updates: tag "v26.11.2" (or "26.11.2"), asset "BoostRoot-Setup-26.11.2.exe",
/// and "SHA256SUMS.txt" listing every asset's hash.</summary>
public static class Updater
{
    public const string Repo = "bl4z3rootstudio/BoostRoot";
    public const string LatestUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";

    public static GitHubRelease? ParseRelease(string json)
    {
        try { return JsonSerializer.Deserialize(json, UpdateJsonContext.Default.GitHubRelease); }
        catch (JsonException) { return null; }
    }

    /// <summary>Returns the update if the release is newer than <paramref name="current"/>.</summary>
    public static UpdateInfo? Evaluate(GitHubRelease r, AppVersion current, string? sums)
    {
        if (r.Draft || r.Prerelease || !AppVersion.TryParse(r.Tag.TrimStart('v', 'V'), out var v) || v.CompareTo(current) <= 0) return null;
        var setup = r.Assets.FirstOrDefault(a => a.Name.StartsWith("BoostRoot-Setup", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        return setup is null ? null : new UpdateInfo(v, r.Body ?? "", setup, sums is null ? null : HashFor(sums, setup.Name));
    }

    /// <summary>Reads "hash  filename" lines (sha256sum format).</summary>
    public static string? HashFor(string sums, string file) =>
        sums.Split('\n').Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(p => p.Length == 2 && p[1].TrimStart('*', ' ') == file)?[0].ToLowerInvariant();

    public static string? SumsUrl(GitHubRelease r) => r.Assets.FirstOrDefault(a => a.Name == "SHA256SUMS.txt")?.Url;
}
