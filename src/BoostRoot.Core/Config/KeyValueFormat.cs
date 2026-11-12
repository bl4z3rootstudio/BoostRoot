namespace BoostRoot.Core.Config;

/// <summary>Flat "key{sep}value" lines (e.g. Minecraft options.txt with ':' separator).</summary>
public sealed class KeyValueFormat(string separator) : IConfigFormat
{
    public string Name => "keyvalue";
    public string Separator { get; } = separator;
    public IConfigDocument Parse(string text) => new Doc(text, Separator);

    private sealed class Doc : IConfigDocument
    {
        private readonly List<string> _lines;
        private readonly string _sep;
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
        private readonly List<string> _keys = [];

        public Doc(string text, string sep)
        {
            _sep = sep;
            _lines = [.. text.Split('\n')];
            for (var i = 0; i < _lines.Count; i++)
            {
                var t = _lines[i].TrimStart();
                if (t.Length == 0 || t[0] == '#') continue;
                var p = _lines[i].IndexOf(_sep, StringComparison.Ordinal);
                if (p <= 0) continue;
                var key = _lines[i][..p].Trim();
                if (_index.TryAdd(key, i)) _keys.Add(key);
            }
        }

        public IReadOnlyList<string> Keys => _keys;
        public bool Contains(string key) => _index.ContainsKey(key);

        public string? Get(string key) =>
            _index.TryGetValue(key, out var i) ? _lines[i][(_lines[i].IndexOf(_sep, StringComparison.Ordinal) + _sep.Length)..].TrimEnd('\r') : null;

        public void Set(string key, string value)
        {
            if (_index.TryGetValue(key, out var i))
            {
                var p = _lines[i].IndexOf(_sep, StringComparison.Ordinal) + _sep.Length;
                _lines[i] = _lines[i][..p] + value;
                return;
            }
            var at = _lines.Count > 0 && _lines[^1].Length == 0 ? _lines.Count - 1 : _lines.Count;
            _lines.Insert(at, key + _sep + value);
            _index[key] = at;
            _keys.Add(key);
        }

        public string Serialize() => string.Join('\n', _lines);
    }
}
