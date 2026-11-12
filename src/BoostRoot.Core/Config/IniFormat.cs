namespace BoostRoot.Core.Config;

/// <summary>INI with key paths "Section/Key". Comments, array lines (+Key/-Key/.Key/!Key) and malformed lines are preserved verbatim.</summary>
public sealed class IniFormat : IConfigFormat
{
    public static IniFormat Instance { get; } = new();
    public string Name => "ini";
    public IConfigDocument Parse(string text) => new Doc(text);

    private sealed class Doc : IConfigDocument
    {
        private readonly List<string> _lines;
        private readonly Dictionary<string, int> _index = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _keys = [];

        public Doc(string text)
        {
            _lines = [.. text.Split('\n')];
            Reindex();
        }

        private void Reindex()
        {
            _index.Clear();
            _keys.Clear();
            string section = "";
            for (var i = 0; i < _lines.Count; i++)
            {
                if (TryHeader(_lines[i], out var name)) { section = name; continue; }
                if (!TryKey(_lines[i], out var key, out _)) continue;
                var path = section.Length == 0 ? key : section + "/" + key;
                if (_index.TryAdd(path, i)) _keys.Add(path);
            }
        }

        private static bool TryHeader(string line, out string name)
        {
            var t = line.Trim();
            if (t.Length >= 2 && t[0] == '[' && t[^1] == ']') { name = t[1..^1].Trim(); return true; }
            name = "";
            return false;
        }

        private static bool TryKey(string line, out string key, out int valueStart)
        {
            key = "";
            valueStart = -1;
            var t = line.TrimStart();
            if (t.Length == 0 || t[0] is ';' or '#' or '+' or '-' or '.' or '!' or '[') return false;
            var eq = line.IndexOf('=');
            if (eq <= 0) return false;
            key = line[..eq].Trim();
            if (key.Length == 0) return false;
            valueStart = eq + 1;
            while (valueStart < line.Length && line[valueStart] is ' ' or '\t') valueStart++;
            return true;
        }

        private static (string Section, string Key) Split(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash < 0 ? ("", path) : (path[..slash], path[(slash + 1)..]);
        }

        public IReadOnlyList<string> Keys => _keys;
        public bool Contains(string key) => _index.ContainsKey(key);

        public string? Get(string key)
        {
            if (!_index.TryGetValue(key, out var i)) return null;
            TryKey(_lines[i], out _, out var start);
            return _lines[i][start..].TrimEnd('\r', ' ', '\t');
        }

        public void Set(string key, string value)
        {
            if (_index.TryGetValue(key, out var i))
            {
                TryKey(_lines[i], out _, out var start);
                _lines[i] = _lines[i][..start] + value;
                return;
            }

            var (section, name) = Split(key);
            var newLine = name + "=" + value;
            var headerIdx = -1;
            var lastKeyIdx = -1;
            var inTarget = section.Length == 0;
            for (var j = 0; j < _lines.Count; j++)
            {
                if (TryHeader(_lines[j], out var n))
                {
                    if (inTarget && headerIdx >= 0) break; // end of first occurrence of the target section
                    inTarget = string.Equals(n, section, StringComparison.OrdinalIgnoreCase);
                    if (inTarget) headerIdx = j;
                    else if (section.Length == 0) break; // global keys live before the first header
                    continue;
                }
                if (inTarget && TryKey(_lines[j], out _, out _)) lastKeyIdx = j;
            }

            if (lastKeyIdx >= 0) _lines.Insert(lastKeyIdx + 1, newLine);
            else if (headerIdx >= 0) _lines.Insert(headerIdx + 1, newLine);
            else if (section.Length == 0) _lines.Insert(0, newLine);
            else
            {
                var at = _lines.Count > 0 && _lines[^1].Length == 0 ? _lines.Count - 1 : _lines.Count;
                var block = new List<string>();
                if (at > 0 && _lines[at - 1].Trim().Length > 0) block.Add("");
                block.Add("[" + section + "]");
                block.Add(newLine);
                _lines.InsertRange(at, block);
                if (_lines[^1].Length != 0) _lines.Add("");
            }
            Reindex();
        }

        public string Serialize() => string.Join('\n', _lines);
    }
}
