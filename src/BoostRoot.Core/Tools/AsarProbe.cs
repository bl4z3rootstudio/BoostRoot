using System.Text;
using System.Text.Json;

namespace BoostRoot.Core.Tools;

/// <summary>Reads one file out of an Electron .asar archive (Riot Client) to spot the known corruption.</summary>
public static class AsarProbe
{
    /// <summary>True when dist/main/main.js is not JavaScript (a broken update puts protobuf text there). False when unsure.</summary>
    public static bool MainJsLooksBroken(Stream fs)
    {
        try
        {
            var head = new byte[16];
            fs.ReadExactly(head);
            var headerSize = BitConverter.ToInt32(head, 4);
            var jsonLen = BitConverter.ToInt32(head, 12);
            if (jsonLen <= 0 || jsonLen > 50_000_000) return true;
            var json = new byte[jsonLen];
            fs.ReadExactly(json);
            using var doc = JsonDocument.Parse(json);
            var node = doc.RootElement;
            foreach (var part in new[] { "dist", "main", "main.js" })
                if (!node.TryGetProperty("files", out var files) || !files.TryGetProperty(part, out node)) return false; // other layout: don't guess
            if (!node.TryGetProperty("offset", out var off) || !long.TryParse(off.GetString(), out var offset)) return false;
            fs.Position = 8 + headerSize + offset;
            var sample = new byte[512];
            var n = fs.Read(sample, 0, sample.Length);
            var text = Encoding.UTF8.GetString(sample, 0, n);
            return text.Contains("optimize_for", StringComparison.Ordinal) || text.Contains("syntax = \"proto", StringComparison.Ordinal)
                || text.TrimStart().StartsWith("option ", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or EndOfStreamException or ArgumentException) { return false; }
    }
}
