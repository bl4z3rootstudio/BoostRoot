using System.Text.Json.Serialization;

namespace BoostRoot.Core.Safety;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JournalEntry))]
[JsonSerializable(typeof(RevertMark))]
internal sealed partial class SafetyJsonContext : JsonSerializerContext;

internal sealed record RevertMark(string EntryId, DateTimeOffset At);
