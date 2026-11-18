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
            // Boost choices: Focus Mode, extras, per-game boost level, 4:3 games, favourites, last game, look.
            "focus=" + B(s.PureBoost), "eco=" + B(s.EcoBackground), "net=" + B(s.NetBoost), "auto=" + B(s.AutoBoost), "wait=" + B(s.WaitForGame),
            "theme=" + s.Theme, "horiz=" + B(s.OverlayHorizontal),
            "levels=" + string.Join(",", (s.GameLevels ?? []).Select(kv => kv.Key + ":" + kv.Value)),
            "stretch=" + string.Join(",", s.StretchGames ?? []), "fav=" + string.Join(",", s.Favorites ?? []),
            "last=" + (s.LastGameId ?? ""),
        };
        if (s.CrosshairDesign is { } x) pairs.Add("xhair=" + x.ToValorantCode());
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
        if (d.TryGetValue("corner", out var c) && c is "tl" or "tc" or "tr" or "ml" or "mr" or "bl" or "br") s = s with { OverlayCorner = c };
        if (d.TryGetValue("theme", out var th) && ThemePalette.All.Any(t => t.Key == th)) s = s with { Theme = th };
        if (d.TryGetValue("levels", out var lv))
            s = s with { GameLevels = List(lv).Select(x => x.Split(':', 2)).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]) };
        if (d.TryGetValue("stretch", out var st)) s = s with { StretchGames = List(st) };
        if (d.TryGetValue("fav", out var fv)) s = s with { Favorites = List(fv) };
        if (d.TryGetValue("last", out var last) && last.Length > 0) s = s with { LastGameId = last };
        if (d.TryGetValue("xhair", out var xh) && BoostRoot.Core.Crosshair.CrosshairDesign.FromValorantCode(xh) is { } design) s = s with { CrosshairDesign = design };
        if (d.TryGetValue("accent", out var a)) s = s with { Accent = a };
        if (d.TryGetValue("color", out var col) && col.Length is 7 or 9 && col[0] == '#') s = s with { OverlayColor = col };
        return s with
        {
            OverlayIcons = Bool("icons", s.OverlayIcons), OverlayOpacity = Int("opacity", s.OverlayOpacity, 0, 100), OverlayScale = Int("scale", s.OverlayScale, 60, 200),
            FpsCap = Int("cap", s.FpsCap, 0, 1000), SessionReport = Bool("report", s.SessionReport), StutterDetect = Bool("stutter", s.StutterDetect),
            PacketLossAlert = Bool("loss", s.PacketLossAlert), PauseDownloads = Bool("pause", s.PauseDownloads), Notifications = Bool("notify", s.Notifications),
            Hotkeys = Bool("hotkeys", s.Hotkeys), AutoUndo = Bool("autoundo", s.AutoUndo),
            PureBoost = Bool("focus", s.PureBoost), EcoBackground = Bool("eco", s.EcoBackground), NetBoost = Bool("net", s.NetBoost),
            AutoBoost = Bool("auto", s.AutoBoost), WaitForGame = Bool("wait", s.WaitForGame), OverlayHorizontal = Bool("horiz", s.OverlayHorizontal),
        };
    }

    private static string B(bool v) => v ? "1" : "0";
}
