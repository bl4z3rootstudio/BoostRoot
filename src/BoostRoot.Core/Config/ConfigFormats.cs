namespace BoostRoot.Core.Config;

public static class ConfigFormats
{
    public static IReadOnlyCollection<string> Names { get; } = ["ini", "keyvalue", "keyvalues", "json", "xml"];

    public static IConfigFormat Get(string name, string? separator = null) => name switch
    {
        "ini" => IniFormat.Instance,
        "keyvalue" => new KeyValueFormat(string.IsNullOrEmpty(separator) ? "=" : separator),
        "keyvalues" => KeyValuesFormat.Instance,
        "json" => JsonFormat.Instance,
        "xml" => XmlFormat.Instance,
        _ => throw new ArgumentException($"Unknown config format '{name}'.", nameof(name)),
    };
}
