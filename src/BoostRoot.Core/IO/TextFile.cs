using System.Text;

namespace BoostRoot.Core.IO;

/// <summary>Text content with its original newline style and BOM, so files can be written back byte-faithfully.</summary>
public sealed record TextFile(string Text, string Newline, bool Utf8Bom)
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding Utf8NoBom = new(false, false);

    /// <summary>Decodes bytes; <see cref="Text"/> always uses '\n' line endings.</summary>
    public static TextFile Decode(byte[] bytes)
    {
        var hasBom = bytes.Length >= 3 && bytes[0] == Bom[0] && bytes[1] == Bom[1] && bytes[2] == Bom[2];
        var raw = Utf8NoBom.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        var newline = raw.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var text = raw.Replace("\r\n", "\n", StringComparison.Ordinal);
        return new TextFile(text, newline, hasBom);
    }

    public byte[] Encode()
    {
        var text = Newline == "\n" ? Text : Text.Replace("\n", Newline, StringComparison.Ordinal);
        var body = Utf8NoBom.GetBytes(text);
        if (!Utf8Bom) return body;
        var result = new byte[body.Length + 3];
        Bom.CopyTo(result, 0);
        body.CopyTo(result, 3);
        return result;
    }

    public TextFile WithText(string text) => this with { Text = text };
}
