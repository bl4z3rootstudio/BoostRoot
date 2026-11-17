using System.Globalization;
using System.Text;

namespace BoostRoot.ViewModels;

/// <summary>Short shareable code for game-related settings ("BR1-…"). Paths, webhooks and language are never included.</summary>
public static class SettingsCode
{
    private const string Prefix = "BR1-";

    public static string Encode(AppSettings s)
    {
        var pairs = new List<string>
        {
            "close=" + string.Join(",", s.CloseList),
            "metrics=" + string.Join(",", s.Metrics),
            "corner=" + s.OverlayCorner,
            "accent=" + s.Accent,
            "icons=" + B(s.OverlayIcons), "color=" + s.OverlayColor, "opacity=" + s.OverlayOpacity.ToString(CultureInfo.InvariantCulture),
            "scale=" + s.OverlayScale.ToString(CultureInfo.InvariantCulture), "cap=" + s.FpsCap.ToString(CultureInfo.InvariantCulture),
            "report=" + B(s.SessionReport), "stutter=" + B(s.StutterDetect), "loss=" + B(s.PacketLossAlert), "pause=" + B(s.PauseDownloads),
            "notify=" + B(s.Notifications), "hotkeys=" + B(s.Hotkeys), "autoundo=" + B(s.AutoUndo),
        };
        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", pairs))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Applies the code on top of <paramref name="current"/>; null if the code is not valid.</summary>
    public static AppSettings? Decode(string code, AppSettings current)
    {
        code = code.Trim();
        if (!code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var b64 = code[Prefix.Length..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        string text;
        try { text = Encoding.UTF8.GetString(Convert.FromBase64String(b64)); } catch (FormatException) { return null; }
        var d = text.Split('\n').Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        if (d.Count == 0) return null;
        var s = current;
        string[] List(string v) => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Bool(string k, bool def) => d.TryGetValue(k, out var v) ? v == "1" : def;
        int Int(string k, int def, int min, int max) => d.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, min, max) : def;
        if (d.TryGetValue("close", out var close)) s = s with { CloseList = List(close) };
        if (d.TryGetValue("metrics", out var m)) s = s with { OverlayMetrics = [.. List(m).Where(AppSettings.AllMetrics.Contains)] };
        if (d.TryGetValue("corner", out var c) && c is "tl" or "tr" or "bl" or "br") s = s with { OverlayCorner = c };
        if (d.TryGetValue("accent", out var a)) s = s with { Accent = a };
        if (d.TryGetValue("color", out var col) && col.Length is 7 or 9 && col[0] == '#') s = s with { OverlayColor = col };
        return s with
        {
            OverlayIcons = Bool("icons", s.OverlayIcons), OverlayOpacity = Int("opacity", s.OverlayOpacity, 0, 100), OverlayScale = Int("scale", s.OverlayScale, 60, 200),
            FpsCap = Int("cap", s.FpsCap, 0, 1000), SessionReport = Bool("report", s.SessionReport), StutterDetect = Bool("stutter", s.StutterDetect),
            PacketLossAlert = Bool("loss", s.PacketLossAlert), PauseDownloads = Bool("pause", s.PauseDownloads), Notifications = Bool("notify", s.Notifications),
            Hotkeys = Bool("hotkeys", s.Hotkeys), AutoUndo = Bool("autoundo", s.AutoUndo),
        };
    }

    private static string B(bool v) => v ? "1" : "0";
}
