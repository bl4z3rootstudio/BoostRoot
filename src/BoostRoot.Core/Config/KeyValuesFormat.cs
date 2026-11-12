using System.Text.RegularExpressions;

namespace BoostRoot.Core.Config;

/// <summary>Valve KeyValues ("block" { "key" "value" }) with key paths "block/sub/key". Only the value text of a line is ever rewritten.</summary>
public sealed partial class KeyValuesFormat : IConfigFormat
{
    public static KeyValuesFormat Instance { get; } = new();
    public string Name => "keyvalues";
    public IConfigDocument Parse(string text) => new Doc(text);

    [GeneratedRegex("\"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex Quoted();

    private sealed class Doc : IConfigDocument
    {
        private readonly List<string> _lines;
        private readonly Dictionary<string, int> _index = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _blockClose = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _keys = [];

        public Doc(string text)
        {
            _lines = [.. text.Split('\n')];
            Reindex();
        }

        private void Reindex()
        {
            _index.Clear();
            _blockClose.Clear();
            _keys.Clear();
            var stack = new List<string>();
            string? pending = null;
            for (var i = 0; i < _lines.Count; i++)
            {
                var line = StripComment(_lines[i]).Trim();
                if (line.Length == 0) continue;
                var matches = Quoted().Matches(line);
                if (matches.Count >= 2)
                {
                    var path = string.Join('/', [.. stack, matches[0].Groups[1].Value]);
                    if (_index.TryAdd(path, i)) _keys.Add(path);
                    continue;
                }
                if (matches.Count == 1) pending = matches[0].Groups[1].Value;
                if (line.EndsWith('{'))
                {
                    stack.Add(pending ?? "");
                    pending = null;
                }
                else if (line == "}" && stack.Count > 0)
                {
                    _blockClose.TryAdd(string.Join('/', stack), i);
                    stack.RemoveAt(stack.Count - 1);
                }
            }
        }

        private static string StripComment(string line)
        {
            var inQuote = false;
            for (var i = 0; i < line.Length - 1; i++)
            {
                if (line[i] == '"') inQuote = !inQuote;
                if (!inQuote && line[i] == '/' && line[i + 1] == '/') return line[..i];
            }
            return line;
        }

        public IReadOnlyList<string> Keys => _keys;
        public bool Contains(string key) => _index.ContainsKey(key);

        public string? Get(string key) =>
            _index.TryGetValue(key, out var i) ? Quoted().Matches(_lines[i])[1].Groups[1].Value : null;

        public void Set(string key, string value)
        {
            var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
            if (_index.TryGetValue(key, out var i))
            {
                var g = Quoted().Matches(_lines[i])[1].Groups[1];
                _lines[i] = _lines[i][..g.Index] + escaped + _lines[i][(g.Index + g.Length)..];
                return;
            }
            var slash = key.LastIndexOf('/');
            var block = slash < 0 ? "" : key[..slash];
            var name = slash < 0 ? key : key[(slash + 1)..];
            if (!_blockClose.TryGetValue(block, out var close))
                throw new ArgumentException($"KeyValues block '{block}' does not exist; cannot add '{name}'.", nameof(key));
            var depth = block.Length == 0 ? 0 : block.Split('/').Length;
            _lines.Insert(close, new string('\t', depth) + "\"" + name + "\"\t\t\"" + escaped + "\"");
            Reindex();
        }

        public string Serialize() => string.Join('\n', _lines);
    }
}
