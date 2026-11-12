namespace BoostRoot.Games;

/// <summary>Minimal Valve text VDF reader: returns nested case-insensitive dictionaries; values are strings.</summary>
public static class VdfReader
{
    public sealed class Node : Dictionary<string, object>
    {
        public Node() : base(StringComparer.OrdinalIgnoreCase) { }
        public string? Str(string key) => TryGetValue(key, out var v) ? v as string : null;
        public Node? Child(string key) => TryGetValue(key, out var v) ? v as Node : null;
    }

    public static Node Parse(string text)
    {
        var tokens = Tokenize(text).GetEnumerator();
        return ReadBlock(tokens, isRoot: true);
    }

    private static Node ReadBlock(IEnumerator<string> t, bool isRoot)
    {
        var node = new Node();
        while (t.MoveNext())
        {
            var key = t.Current;
            if (key == "}") { if (isRoot) continue; return node; }
            if (key == "{") continue;
            if (!t.MoveNext()) break;
            var val = t.Current;
            if (val == "{") node[key] = ReadBlock(t, false);
            else node[key] = val;
        }
        return node;
    }

    private static IEnumerable<string> Tokenize(string s)
    {
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c is '{' or '}') { i++; yield return c.ToString(); continue; }
            if (c == '"')
            {
                var sb = new System.Text.StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length) { sb.Append(s[i + 1] switch { 'n' => '\n', 't' => '\t', _ => s[i + 1] }); i += 2; continue; }
                    sb.Append(s[i++]);
                }
                i++;
                yield return sb.ToString();
                continue;
            }
            var start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not '{' and not '}') i++;
            yield return s[start..i];
        }
    }
}
