using System.Globalization;
using System.Text;

namespace BoostRoot.Core.Crosshair;

/// <summary>One line group (inner or outer) of a crosshair.</summary>
public sealed record CrosshairLines(bool Show = true, int Thickness = 2, int Length = 6, int Offset = 3, double Opacity = 1);

/// <summary>Game-independent crosshair design that can be exported to several games.</summary>
public sealed record CrosshairDesign(
    string Name = "BoostRoot",
    string Color = "#00FF66",
    bool Outline = true,
    int OutlineThickness = 1,
    double OutlineOpacity = 0.5,
    bool Dot = false,
    int DotThickness = 2,
    double DotOpacity = 1,
    CrosshairLines? Inner = null,
    CrosshairLines? Outer = null)
{
    public CrosshairLines InnerLines => Inner ?? new();
    public CrosshairLines OuterLines => Outer ?? new(Show: false, Thickness: 2, Length: 2, Offset: 10, Opacity: 0.35);

    public static IReadOnlyList<CrosshairDesign> Presets { get; } =
    [
        new("Klasik", "#00FF66", Inner: new(true, 2, 6, 3, 1)),
        new("İnce artı", "#00FFFF", OutlineOpacity: 1, Inner: new(true, 1, 4, 2, 1)),
        new("Sadece nokta", "#FFFFFF", Dot: true, DotThickness: 3, Inner: new(false)),
        new("Kısa kalın", "#FFFF00", Inner: new(true, 3, 3, 2, 1)),
        new("Pembe nokta + artı", "#FF5CF0", Dot: true, DotThickness: 2, Inner: new(true, 2, 4, 3, 0.8)),
        new("Kırmızı T", "#FF3B3B", Inner: new(true, 2, 5, 3, 1)),
        new("Büyük boşluklu", "#00FF66", Inner: new(true, 2, 6, 8, 1), Outer: new(true, 2, 3, 16, 0.5)),
    ];

    private static (int R, int G, int B) Rgb(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return (0, 255, 102);
        return ((v >> 16) & 255, (v >> 8) & 255, v & 255);
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // ---------------- VALORANT ----------------
    // Profile code (Settings → Crosshair → Import profile code). Defaults are omitted like the game does.
    private static readonly (string Hex, int Index)[] ValorantColors =
        [("FFFFFF", 0), ("00FF00", 1), ("7FFF00", 2), ("DFFF00", 3), ("FFFF00", 4), ("00FFFF", 5), ("FF00FF", 6), ("FF0000", 7)];

    public string ToValorantCode()
    {
        var sb = new StringBuilder("0;P");
        void Add(string k, string v) => sb.Append(';').Append(k).Append(';').Append(v);
        var hex = Color.TrimStart('#').ToUpperInvariant();
        var idx = ValorantColors.FirstOrDefault(c => c.Hex == hex);
        if (idx.Hex is not null) { if (idx.Index != 0) Add("c", idx.Index.ToString(CultureInfo.InvariantCulture)); }
        else { Add("c", "8"); Add("u", hex + "FF"); }
        if (!Outline) Add("h", "0");
        else
        {
            if (OutlineThickness != 1) Add("t", Math.Clamp(OutlineThickness, 1, 6).ToString(CultureInfo.InvariantCulture));
            if (Math.Abs(OutlineOpacity - 0.5) > 0.001) Add("o", F(OutlineOpacity));
        }
        if (Dot)
        {
            Add("d", "1");
            if (DotThickness != 2) Add("z", Math.Clamp(DotThickness, 1, 6).ToString(CultureInfo.InvariantCulture));
            if (Math.Abs(DotOpacity - 1) > 0.001) Add("a", F(DotOpacity));
        }
        void Lines(string p, CrosshairLines l, CrosshairLines def)
        {
            if (!l.Show) { Add(p + "b", "0"); return; }
            if (l.Thickness != def.Thickness) Add(p + "t", Math.Clamp(l.Thickness, 0, 10).ToString(CultureInfo.InvariantCulture));
            if (l.Length != def.Length) Add(p + "l", Math.Clamp(l.Length, 0, 20).ToString(CultureInfo.InvariantCulture));
            if (l.Offset != def.Offset) Add(p + "o", Math.Clamp(l.Offset, 0, 20).ToString(CultureInfo.InvariantCulture));
            if (Math.Abs(l.Opacity - def.Opacity) > 0.001) Add(p + "a", F(l.Opacity));
        }
        Lines("0", InnerLines, new(true, 2, 6, 3, 0.8));
        var outer = OuterLines;
        if (outer.Show) Lines("1", outer, new(true, 2, 2, 10, 0.35)); else Add("1b", "0");
        return sb.ToString();
    }

    /// <summary>Reads the primary profile of a VALORANT crosshair code (unknown keys are ignored).</summary>
    public static CrosshairDesign? FromValorantCode(string code)
    {
        var parts = code.Trim().Split(';');
        if (parts.Length < 2 || parts[0] != "0") return null;
        var d = new Dictionary<string, string>();
        var section = "";
        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i] is "P" or "A" or "S") { section = parts[i]; continue; }
            if (i + 1 >= parts.Length) break;
            if (section == "P") d[parts[i]] = parts[i + 1];
            i++;
        }
        int I(string k, int def) => d.TryGetValue(k, out var v) && int.TryParse(v, CultureInfo.InvariantCulture, out var n) ? n : def;
        double D(string k, double def) => d.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : def;
        var c = I("c", 0);
        var color = c == 8 && d.TryGetValue("u", out var u) && u.Length >= 6 ? "#" + u[..6] : "#" + ValorantColors.FirstOrDefault(x => x.Index == c, ValorantColors[0]).Hex;
        return new CrosshairDesign("VALORANT", color, I("h", 1) == 1, I("t", 1), D("o", 0.5), I("d", 0) == 1, I("z", 2), D("a", 1),
            new CrosshairLines(I("0b", 1) == 1, I("0t", 2), I("0l", 6), I("0o", 3), D("0a", 0.8)),
            new CrosshairLines(I("1b", 1) == 1, I("1t", 2), I("1l", 2), I("1o", 10), D("1a", 0.35)));
    }

    // ---------------- Source engine (CS2, CS:GO, TF2, CS:Source) ----------------
    /// <summary>Console commands (paste into the console or autoexec.cfg).</summary>
    public string ToSourceCommands()
    {
        var (r, g, b) = Rgb(Color);
        var l = InnerLines;
        var sizePx = l.Show ? l.Length : 0;
        return string.Join("; ",
            "cl_crosshairstyle 4",
            $"cl_crosshairsize {F(sizePx / 2.0)}",
            $"cl_crosshairthickness {F(Math.Max(0, l.Thickness / 2.0 - 0.5))}",
            $"cl_crosshairgap {l.Offset - 4}",
            "cl_crosshaircolor 5",
            $"cl_crosshaircolor_r {r}", $"cl_crosshaircolor_g {g}", $"cl_crosshaircolor_b {b}",
            $"cl_crosshairalpha {(int)Math.Round(l.Opacity * 255)}",
            "cl_crosshairusealpha 1",
            $"cl_crosshairdot {(Dot ? 1 : 0)}",
            $"cl_crosshair_drawoutline {(Outline ? 1 : 0)}",
            $"cl_crosshair_outlinethickness {Math.Clamp(OutlineThickness, 0, 3)}",
            "cl_crosshair_t 0");
    }

    /// <summary>Settings the BoostRoot overlay crosshair (works on top of any game) can draw.</summary>
    public (string Color, int Size, int Thickness, int Gap, bool Dot, bool Outline) ToOverlay() =>
        (Color, InnerLines.Show ? InnerLines.Length : 0, InnerLines.Thickness, InnerLines.Offset, Dot, Outline);
}

/// <summary>Where a design can be sent and how.</summary>
public sealed record CrosshairTarget(string Id, string Game, string HowTr, string HowEn, Func<CrosshairDesign, string>? Export);

public static class CrosshairTargets
{
    public static IReadOnlyList<CrosshairTarget> All { get; } =
    [
        new("valorant", "VALORANT", "Ayarlar → Nişangâh → Profil kodunu içe aktar → kodu yapıştır.", "Settings → Crosshair → Import profile code → paste.", d => d.ToValorantCode()),
        new("cs2", "Counter-Strike 2", "Konsolu aç (~) ve komutları yapıştır. Kalıcı olsun istersen autoexec.cfg'ye ekle.", "Open the console (~) and paste. Add to autoexec.cfg to keep it.", d => d.ToSourceCommands()),
        new("tf2", "Team Fortress 2 / CS:Source", "Konsola yapıştır (desteklenen komutlar uygulanır).", "Paste into the console (supported commands apply).", d => d.ToSourceCommands()),
        new("overlay", "Diğer tüm oyunlar", "BoostRoot nişangâhı olarak kaydet; oyunda Alt+C ile aç/kapat (Fortnite, Apex, PUBG, Rust, R6, Minecraft ve hepsi).", "Save as the BoostRoot crosshair; toggle in game with Alt+C (works in every game).", null),
    ];
}
