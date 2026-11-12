using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoostRoot.Core.Config;

/// <summary>JSON with key paths "a/b/0/c". Existing value types (number/bool/string) are preserved on write.</summary>
public sealed class JsonFormat : IConfigFormat
{
    public static JsonFormat Instance { get; } = new();
    public string Name => "json";
    public IConfigDocument Parse(string text) => new Doc(text);

    private sealed class Doc(string text) : IConfigDocument
    {
        private readonly JsonNode _root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? throw new FormatException("Empty JSON document.");
        private readonly bool _trailingNewline = text.EndsWith('\n');

        public IReadOnlyList<string> Keys
        {
            get
            {
                var list = new List<string>();
                Walk(_root, "", list);
                return list;
            }
        }

        private static void Walk(JsonNode? node, string prefix, List<string> list)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var kv in o) Walk(kv.Value, prefix.Length == 0 ? kv.Key : prefix + "/" + kv.Key, list);
                    break;
                case JsonArray a:
                    for (var i = 0; i < a.Count; i++) Walk(a[i], prefix + "/" + i.ToString(CultureInfo.InvariantCulture), list);
                    break;
                default:
                    list.Add(prefix);
                    break;
            }
        }

        private JsonNode? Find(string key)
        {
            JsonNode? node = _root;
            foreach (var part in key.Split('/'))
            {
                node = node switch
                {
                    JsonObject o => o.TryGetPropertyValue(part, out var child) ? child : null,
                    JsonArray a when int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var idx) && idx < a.Count => a[idx],
                    _ => null,
                };
                if (node is null) return null;
            }
            return node;
        }

        public bool Contains(string key) => Find(key) is JsonValue;

        public string? Get(string key) => Find(key) is JsonValue v ? ValueText(v) : null;

        private static string ValueText(JsonValue v) => v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => v.ToJsonString(),
        };

        public void Set(string key, string value)
        {
            var parts = key.Split('/');
            JsonNode parent = _root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var next = parent switch
                {
                    JsonObject o => o.TryGetPropertyValue(parts[i], out var c) ? c : null,
                    JsonArray a when int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var idx) && idx < a.Count => a[idx],
                    _ => throw new ArgumentException($"Cannot traverse JSON path '{key}'.", nameof(key)),
                };
                if (next is null)
                {
                    if (parent is not JsonObject po) throw new ArgumentException($"Cannot create JSON path '{key}'.", nameof(key));
                    next = new JsonObject();
                    po[parts[i]] = next;
                }
                parent = next;
            }

            var last = parts[^1];
            var existing = parent switch
            {
                JsonObject o => o.TryGetPropertyValue(last, out var c) ? c as JsonValue : null,
                JsonArray a when int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var idx) && idx < a.Count => a[idx] as JsonValue,
                _ => null,
            };
            var newValue = Convert(value, existing?.GetValueKind());
            switch (parent)
            {
                case JsonObject o: o[last] = newValue; break;
                case JsonArray a: a[int.Parse(last, CultureInfo.InvariantCulture)] = newValue; break;
                default: throw new ArgumentException($"Cannot set JSON path '{key}'.", nameof(key));
            }
        }

        private static JsonNode Convert(string value, JsonValueKind? kind)
        {
            bool IsBool() => value is "true" or "false";
            bool IsNumber() => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            var target = kind switch
            {
                JsonValueKind.String => JsonValueKind.String,
                JsonValueKind.True or JsonValueKind.False => IsBool() ? JsonValueKind.True : JsonValueKind.String,
                JsonValueKind.Number => IsNumber() ? JsonValueKind.Number : JsonValueKind.String,
                _ => IsBool() ? JsonValueKind.True : IsNumber() ? JsonValueKind.Number : JsonValueKind.String,
            };
            return target switch
            {
                JsonValueKind.True => JsonValue.Create(value == "true"),
                JsonValueKind.Number => JsonNode.Parse(value)!,
                _ => JsonValue.Create(value),
            };
        }

        public string Serialize()
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, IndentSize = 2, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                _root.WriteTo(w);
            var s = Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal);
            return _trailingNewline ? s + "\n" : s;
        }
    }
}
