using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace BoostRoot.ViewModels.Localization;

/// <summary>Indexed string table. Bind as {Binding [key], Source={x:Static loc:Loc.I}}; changing Language refreshes every binding.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    // Order matters: Languages must be initialised before the singleton constructor reads it.
    public static IReadOnlyList<string> Languages { get; } = ["tr", "en"];
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
    }

    public string this[string key] => _current.TryGetValue(key, out var v) ? v : key;
    public bool Has(string key) => _current.ContainsKey(key);

    public string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);
}
