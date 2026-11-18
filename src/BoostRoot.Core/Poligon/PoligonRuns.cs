using System.Text.Json;

namespace BoostRoot.Core.Poligon;

/// <summary>One finished drill, as written by the Poligon (Godot) app to results.json.</summary>
public sealed record PoligonRun(string Drill, string Game, double Sens, double Score, int Kills, int Shots, double Accuracy, double Headshot,
    double AvgReactionMs, bool Died, DateTimeOffset At);

public static class PoligonRuns
{
    /// <summary>Drill ids → Turkish/English names (ids are fixed in poligon/scripts/drills.gd).</summary>
    public static IReadOnlyDictionary<string, (string Tr, string En)> Names { get; } = new Dictionary<string, (string, string)>
    {
        ["duel"] = ("Tepki Düellosu", "Reaction Duel"),
        ["flick"] = ("Flick & Headshot", "Flick & Headshot"),
        ["micro"] = ("Mikro Ayar", "Micro Adjust"),
        ["turn"] = ("Köre Dönme", "Blind Turn"),
        ["strafe"] = ("A-D Düellosu", "A-D Duel"),
        ["counter"] = ("Karşı-Strafe", "Counter-Strafe"),
        ["placement"] = ("Nişan Yüksekliği", "Crosshair Placement"),
        ["dm"] = ("Ölüm Maçı", "Deathmatch"),
        ["warmup"] = ("Maç Öncesi Isınma", "Pre-match Warm-up"),
        ["free"] = ("Serbest Poligon", "Free Range"),
    };

    /// <summary>Tolerant parser: bad or partial entries are skipped, never thrown.</summary>
    public static IReadOnlyList<PoligonRun> Parse(string json)
    {
        var list = new List<PoligonRun>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("drill", out var d) || d.ValueKind != JsonValueKind.String) continue;
                double N(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
                string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                var died = e.TryGetProperty("died", out var dv) && dv.ValueKind == JsonValueKind.True;
                list.Add(new PoligonRun(d.GetString()!, S("game"), N("sens"), N("score"), (int)N("kills"), (int)N("shots"), N("acc"), N("hs"),
                    N("avg_react"), died, DateTimeOffset.FromUnixTimeSeconds((long)N("ts"))));
            }
        }
        catch (JsonException) { }
        return list;
    }

    /// <summary>Per drill: best score, last score, runs and average reaction of the last 10 runs.</summary>
    public static IReadOnlyList<(string Drill, double Best, double Last, int Runs, double AvgReact, double AvgAcc)> Summary(IEnumerable<PoligonRun> runs, string game) =>
        [.. runs.Where(r => r.Game == game && r.Drill != "free").GroupBy(r => r.Drill).Select(g =>
        {
            var ordered = g.OrderBy(r => r.At).ToList();
            var recent = ordered.TakeLast(10).ToList();
            var withReact = recent.Where(r => r.AvgReactionMs > 0).ToList();
            return (g.Key, g.Max(r => r.Score), ordered[^1].Score, ordered.Count, withReact.Count > 0 ? withReact.Average(r => r.AvgReactionMs) : 0, recent.Average(r => r.Accuracy));
        }).OrderBy(x => Array.IndexOf(Order, x.Key))];

    private static readonly string[] Order = ["warmup", "duel", "dm", "flick", "micro", "turn", "strafe", "counter", "placement"];
}

/// <summary>Reads the player's real VALORANT settings so the range matches without typing anything.</summary>
public static class ValorantConfig
{
    /// <summary>From RiotUserSettings.ini: "EAresFloatSettingName::MouseSensitivity=0.400000".</summary>
    public static double? Sensitivity(string ini)
    {
        foreach (var line in ini.Split('\n'))
        {
            var l = line.Trim();
            const string key = "EAresFloatSettingName::MouseSensitivity=";
            if (l.StartsWith(key, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(l[key.Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 && v < 100)
                return v;
        }
        return null;
    }

    /// <summary>From RiotUserSettings.ini: the "limit FPS always" value, if that limit is on.</summary>
    public static int? FpsCap(string ini)
    {
        bool on = false;
        double? cap = null;
        foreach (var line in ini.Split('\n'))
        {
            var l = line.Trim();
            if (l.Equals("EAresBoolSettingName::LimitFramerateAlways=True", StringComparison.OrdinalIgnoreCase)) on = true;
            const string key = "EAresFloatSettingName::MaxFramerateAlways=";
            if (l.StartsWith(key, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(l[key.Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) cap = v;
        }
        return on && cap is > 20 and < 1000 ? (int)Math.Round(cap.Value) : null;
    }

    /// <summary>From GameUserSettings.ini: a 4:3-ish resolution without letterbox = stretched.</summary>
    public static bool? Stretched(string ini)
    {
        int? w = null, h = null;
        bool? letterbox = null;
        foreach (var line in ini.Split('\n'))
        {
            var kv = line.Trim().Split('=', 2);
            if (kv.Length != 2) continue;
            switch (kv[0])
            {
                case "ResolutionSizeX" when int.TryParse(kv[1], out var x): w = x; break;
                case "ResolutionSizeY" when int.TryParse(kv[1], out var y): h = y; break;
                case "bShouldLetterbox": letterbox = kv[1].Trim().Equals("True", StringComparison.OrdinalIgnoreCase); break;
            }
        }
        if (w is not { } ww || h is not { } hh || hh <= 0) return null;
        if (ww / (double)hh >= 1.5) return false;
        // 4:3 with letterbox on: the picture is not stretched by the game; the GPU scaling decides — unknown here.
        return letterbox == true ? null : true;
    }
}

public static class ValorantCrosshairConfig
{
    /// <summary>The active crosshair profile from RiotUserSettings.ini (SavedCrosshairProfileData), as a design.</summary>
    public static BoostRoot.Core.Crosshair.CrosshairDesign? Read(string ini)
    {
        foreach (var raw in ini.Split('\n'))
        {
            var l = raw.Trim();
            var i = l.IndexOf("SavedCrosshairProfileData=", StringComparison.Ordinal);
            if (i < 0) continue;
            try
            {
                var value = l[(i + "SavedCrosshairProfileData=".Length)..];
                // The value is a JSON string literal containing JSON.
                var inner = value.StartsWith('"') ? JsonSerializer.Deserialize<string>(value, PoligonJson.Default.String) : value;
                if (inner is null) return null;
                using var doc = JsonDocument.Parse(inner);
                var root = doc.RootElement;
                var profiles = root.GetProperty("profiles");
                var idx = root.TryGetProperty("currentProfile", out var cp) && cp.ValueKind == JsonValueKind.Number ? cp.GetInt32() : 0;
                if (idx < 0 || idx >= profiles.GetArrayLength()) idx = 0;
                var p = profiles[idx].GetProperty("primary");
                bool B(JsonElement e, string k, bool d) => e.TryGetProperty(k, out var v) ? v.ValueKind == JsonValueKind.True : d;
                double D(JsonElement e, string k, double d) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : d;
                int I(JsonElement e, string k, int d) => (int)Math.Round(D(e, k, d));
                string Hex(JsonElement c) => $"#{I(c, "r", 255):X2}{I(c, "g", 255):X2}{I(c, "b", 255):X2}";
                var color = B(p, "bUseCustomColor", false) && p.TryGetProperty("colorCustom", out var cc) ? Hex(cc) : p.TryGetProperty("color", out var c) ? Hex(c) : "#FFFFFF";
                BoostRoot.Core.Crosshair.CrosshairLines Lines(string name, bool defShow)
                {
                    if (!p.TryGetProperty(name, out var e)) return new(defShow);
                    var len = I(e, "lineLength", 6);
                    return new(B(e, "bShowLines", defShow), I(e, "lineThickness", 2), len, I(e, "lineOffset", 3), D(e, "opacity", 1),
                        B(e, "bAllowVertScaling", false) ? I(e, "lineLengthVertical", len) : null, B(e, "bShowMovementError", false), B(e, "bShowShootingError", false));
                }
                return new("VALORANT", color, B(p, "bHasOutline", true), I(p, "outlineThickness", 1), D(p, "outlineOpacity", 0.5),
                    B(p, "bDisplayCenterDot", false), I(p, "centerDotSize", 2), D(p, "centerDotOpacity", 1), Lines("innerLines", true), Lines("outerLines", false));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException) { return null; }
        }
        return null;
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class PoligonJson : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>Reads CS2's cs2_user_convars_*.vcfg: sensitivity and crosshair (converted to a design the range can draw).</summary>
public static class Cs2Config
{
    public static Dictionary<string, string> Convars(string vcfg)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(vcfg, "\"([A-Za-z0-9_]+)\"\\s+\"([^\"]*)\""))
            d[m.Groups[1].Value] = m.Groups[2].Value;
        return d;
    }

    private static double? Num(Dictionary<string, string> d, string k) =>
        d.TryGetValue(k, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    public static double? Sensitivity(string vcfg) => Num(Convars(vcfg), "sensitivity") is { } s && s > 0 && s < 100 ? s : null;

    /// <summary>Inverse of CrosshairDesign.ToSourceCommands (sizes are approximate: CS units differ slightly).</summary>
    public static BoostRoot.Core.Crosshair.CrosshairDesign? Crosshair(string vcfg)
    {
        var d = Convars(vcfg);
        if (Num(d, "cl_crosshairsize") is not { } size) return null;
        var thick = Num(d, "cl_crosshairthickness") ?? 0.5;
        var gap = Num(d, "cl_crosshairgap") ?? -1;
        int Byte(string k, int def) => (int)Math.Clamp(Num(d, k) ?? def, 0, 255);
        var colorIdx = (int)(Num(d, "cl_crosshaircolor") ?? 1);
        var color = colorIdx switch
        {
            0 => "#FF0000", 1 => "#00FF00", 2 => "#FFFF00", 3 => "#0000FF", 4 => "#00FFFF",
            _ => $"#{Byte("cl_crosshaircolor_r", 50):X2}{Byte("cl_crosshaircolor_g", 250):X2}{Byte("cl_crosshaircolor_b", 50):X2}",
        };
        var alpha = (Num(d, "cl_crosshairusealpha") ?? 1) > 0 ? Byte("cl_crosshairalpha", 200) / 255.0 : 1;
        var lines = new BoostRoot.Core.Crosshair.CrosshairLines(size > 0, (int)Math.Max(1, Math.Round((thick + 0.5) * 2)), (int)Math.Round(size * 2), (int)Math.Max(0, Math.Round(gap + 4)), Math.Round(alpha, 2));
        return new("CS2", color, (Num(d, "cl_crosshair_drawoutline") ?? 0) > 0, (int)Math.Clamp(Num(d, "cl_crosshair_outlinethickness") ?? 1, 1, 3), 1,
            (Num(d, "cl_crosshairdot") ?? 0) > 0, 2, 1, lines, new(false), (Num(d, "cl_crosshair_t") ?? 0) > 0);
    }
}
