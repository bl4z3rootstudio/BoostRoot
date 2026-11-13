using System.Xml.Linq;

namespace BoostRoot.Core.Config;

/// <summary>XML with key paths "Root/Child/Leaf" (element text) or "Root/Child@attr" (attribute).</summary>
public sealed class XmlFormat : IConfigFormat
{
    public static XmlFormat Instance { get; } = new();
    public string Name => "xml";
    public IConfigDocument Parse(string text) => new Doc(text);

    private sealed class Doc(string text) : IConfigDocument
    {
        private readonly XDocument _doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        private readonly bool _trailingNewline = text.EndsWith('\n');

        private static (string[] Elements, string? Attr) Split(string key)
        {
            var at = key.LastIndexOf('@');
            var path = at < 0 ? key : key[..at];
            return (path.Split('/'), at < 0 ? null : key[(at + 1)..]);
        }

        private XElement? Find(string[] elements, bool create)
        {
            var root = _doc.Root ?? throw new FormatException("XML has no root element.");
            if (!string.Equals(root.Name.LocalName, elements[0], StringComparison.Ordinal)) return null;
            var current = root;
            foreach (var name in elements.Skip(1))
            {
                var child = current.Elements().FirstOrDefault(e => e.Name.LocalName == name);
                if (child is null)
                {
                    if (!create) return null;
                    child = new XElement(current.Name.Namespace + name);
                    current.Add(child);
                }
                current = child;
            }
            return current;
        }

        public IReadOnlyList<string> Keys
        {
            get
            {
                var list = new List<string>();
                if (_doc.Root is { } r) Walk(r, r.Name.LocalName, list);
                return list;
            }
        }

        private static void Walk(XElement e, string path, List<string> list)
        {
            foreach (var a in e.Attributes().Where(a => !a.IsNamespaceDeclaration)) list.Add(path + "@" + a.Name.LocalName);
            var children = e.Elements().ToList();
            if (children.Count == 0) list.Add(path);
            foreach (var c in children) Walk(c, path + "/" + c.Name.LocalName, list);
        }

        public bool Contains(string key) => Get(key) is not null;

        public string? Get(string key)
        {
            var (els, attr) = Split(key);
            var e = Find(els, false);
            if (e is null) return null;
            return attr is null ? (e.HasElements ? null : e.Value) : e.Attribute(attr)?.Value;
        }

        public void Set(string key, string value)
        {
            var (els, attr) = Split(key);
            var e = Find(els, true) ?? throw new ArgumentException($"XML root does not match path '{key}'.", nameof(key));
            if (attr is null) e.Value = value;
            else e.SetAttributeValue(attr, value);
        }

        public string Serialize()
        {
            // XmlWriter with NewLineHandling.None keeps "\n" as-is on every OS (default would write \r\n on Windows).
            var sb = new System.Text.StringBuilder();
            using (var w = System.Xml.XmlWriter.Create(sb, new System.Xml.XmlWriterSettings
            {
                OmitXmlDeclaration = true, NewLineHandling = System.Xml.NewLineHandling.None, Indent = false,
                ConformanceLevel = System.Xml.ConformanceLevel.Fragment,
            }))
            {
                foreach (var n in _doc.Nodes()) n.WriteTo(w);
            }
            var body = sb.ToString();
            var s = body;
            if (_doc.Declaration is { } d)
            {
                var sep = !body.StartsWith('\n') && text.Contains("?>\n", StringComparison.Ordinal) ? "\n" : "";
                s = d + sep + body;
            }
            return _trailingNewline && !s.EndsWith('\n') ? s + "\n" : s;
        }
    }
}
