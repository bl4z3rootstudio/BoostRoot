using System.Text.Json;
using BoostRoot.Core.IO;

namespace BoostRoot.Core.Profiles;

public sealed class ProfileLoadException(string file, IReadOnlyList<string> errors)
    : Exception($"Invalid profile '{file}': {string.Join("; ", errors)}")
{
    public string File { get; } = file;
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class ProfileLoader
{
    public static GameProfile Parse(string json) =>
        JsonSerializer.Deserialize(json, ProfileJsonContext.Default.GameProfile)
        ?? throw new JsonException("Profile JSON is empty.");

    /// <summary>All-or-nothing: any invalid file aborts the load with <see cref="ProfileLoadException"/>.</summary>
    public static IReadOnlyList<GameProfile> LoadDirectory(IFileSystem fs, string directory)
    {
        var result = new List<GameProfile>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in fs.EnumerateFiles(directory, "*.json", false).Order(StringComparer.OrdinalIgnoreCase))
        {
            GameProfile profile;
            try { profile = Parse(TextFile.Decode(fs.ReadAllBytes(file)).Text); }
            catch (JsonException ex) { throw new ProfileLoadException(file, [ex.Message]); }

            var errors = ProfileValidator.Validate(profile).ToList();
            if (!ids.Add(profile.Id)) errors.Add($"duplicate id '{profile.Id}'");
            if (errors.Count > 0) throw new ProfileLoadException(file, errors);
            result.Add(profile);
        }
        return result;
    }
}
