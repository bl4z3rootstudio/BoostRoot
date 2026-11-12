using System.Text.Json.Serialization;

namespace BoostRoot.Core.Safety;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(JournalEntry))]
[JsonSerializable(typeof(RevertMark))]
[JsonSerializable(typeof(RegistryBackup))]
internal sealed partial class SafetyJsonContext : JsonSerializerContext;

internal sealed record RevertMark(string EntryId, DateTimeOffset At);
