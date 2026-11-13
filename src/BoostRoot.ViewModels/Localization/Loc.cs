using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoostRoot.Core.Profiles;

namespace BoostRoot.ViewModels.Localization;

/// <summary>Indexed string table. Bind as {Binding [key], Source={x:Static loc:Loc.I}}; changing Language refreshes every binding.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    // Order matters: Languages must be initialised before the singleton constructor reads it.
    public static IReadOnlyList<string> Languages { get; } = ["tr", "en", "az", "de", "ru", "es", "pt", "fr", "ar"];
    public static Loc I { get; } = new();

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _tables = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string> _current;

    public event PropertyChangedEventHandler? PropertyChanged;

    private Loc()
    {
        foreach (var lang in Languages) _tables[lang] = Load(lang);
        _current = _tables["tr"];
    }

    internal static IReadOnlyDictionary<string, string> Load(string lang)
    {
        using var s = typeof(Loc).Assembly.GetManifestResourceStream($"BoostRoot.Loc.{lang}.json")
            ?? throw new InvalidOperationException($"Missing language table '{lang}'.");
        using var doc = JsonDocument.Parse(s);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
    }

    public string Language { get; private set; } = "tr";

    public void SetLanguage(string lang)
    {
        if (!_tables.TryGetValue(lang, out var t) || lang == Language) return;
        _current = t;
        Language = lang;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRtl)));
    }

    public string this[string key] => _current.TryGetValue(key, out var v) ? v
        : _tables["en"].TryGetValue(key, out var e) ? e : key;
    public bool Has(string key) => _current.ContainsKey(key);

    /// <summary>Right-to-left script (Arabic): the window mirrors its layout.</summary>
    public bool IsRtl => Language == "ar";

    public string Pick(Localized text) => Text(text.Tr, text.En);

    /// <summary>
    /// Built-in bilingual text (tips, checks, tweaks): Turkish/English as written, other languages from "@English text" entries
    /// in their table. Texts with values in them ("RAM runs at 2133 MHz…") match "@RAM runs at {0} MHz…" templates.
    /// </summary>
    public string Text(string tr, string en)
    {
        if (Language == "tr") return tr;
        if (Language == "en") return en;
        if (_current.TryGetValue("@" + en, out var v)) return v;
        foreach (var (re, tmpl) in Templates(Language))
            if (re.Match(en) is { Success: true } m)
                return string.Format(CultureInfo.CurrentCulture, tmpl, [.. m.Groups.Values.Skip(1).Select(g => (object)g.Value)]);
        return en;
    }

    private readonly Dictionary<string, List<(Regex, string)>> _templates = [];

    private List<(Regex, string)> Templates(string lang)
    {
        if (_templates.TryGetValue(lang, out var list)) return list;
        list = [];
        foreach (var (k, v) in _tables[lang])
            if (k.StartsWith('@') && k.Contains("{0}"))
                list.Add((new Regex("^" + Regex.Replace(Regex.Escape(k[1..]), @"\\\{(\d)}", "(.+?)") + "$", RegexOptions.CultureInvariant), v));
        return _templates[lang] = list;
    }

    public string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);
}
