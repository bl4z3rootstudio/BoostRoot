namespace BoostRoot.Core.Config;

/// <summary>A parsed game settings file. Keys use a format-specific path syntax; values are plain strings.</summary>
public interface IConfigDocument
{
    string? Get(string key);
    void Set(string key, string value);
    bool Contains(string key);
    IReadOnlyList<string> Keys { get; }
    /// <summary>Serializes with '\n' newlines; newline style and BOM are handled by <see cref="IO.TextFile"/>.</summary>
    string Serialize();
}

public interface IConfigFormat
{
    string Name { get; }
    IConfigDocument Parse(string text);
}
