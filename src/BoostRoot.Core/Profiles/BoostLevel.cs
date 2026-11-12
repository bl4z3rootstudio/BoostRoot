using System.Text.Json.Serialization;

namespace BoostRoot.Core.Profiles;

[JsonConverter(typeof(JsonStringEnumConverter<BoostLevel>))]
public enum BoostLevel
{
    [JsonStringEnumMemberName("quality")] Quality,
    [JsonStringEnumMemberName("balanced")] Balanced,
    [JsonStringEnumMemberName("maxfps")] MaxFps,
    [JsonStringEnumMemberName("competitive")] Competitive,
}
