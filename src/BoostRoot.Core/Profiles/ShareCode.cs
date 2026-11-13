using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoostRoot.Core.Profiles;

/// <summary>A friend-shareable boost setup: game, level, 4:3 and crosshair. "BR1-" + base64url(JSON).</summary>
public sealed record BoostShare(string Game, BoostLevel Level, bool Stretched, string? Crosshair = null);

[JsonSerializable(typeof(BoostShare))]
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
internal sealed partial class ShareJsonContext : JsonSerializerContext;

public static class ShareCode
{
    private const string Prefix = "BR1-";

    public static string Encode(BoostShare s) =>
        Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(s, ShareJsonContext.Default.BoostShare))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static BoostShare? Decode(string code)
    {
        code = code.Trim();
        if (!code.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var b = code[Prefix.Length..].Replace('-', '+').Replace('_', '/');
        b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
        try { return JsonSerializer.Deserialize(Convert.FromBase64String(b), ShareJsonContext.Default.BoostShare); }
        catch (Exception ex) when (ex is FormatException or JsonException) { return null; }
    }
}
