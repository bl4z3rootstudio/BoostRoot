using System.Text;

namespace BoostRoot.Core.Profiles;

/// <summary>Hundreds of games without a hand-written profile: driver + Windows optimization and a genre guide.
/// Also builds a generic profile for any installed game BoostRoot doesn't know yet.</summary>
public static class GameLibrary
{
    private static readonly HashSet<string> CompetitiveGenres = ["fps", "br", "tactical", "moba", "fighting"];

    private static readonly Dictionary<string, Localized> GenreTips = new()
    {
        ["fps"] = new("Rekabetçi FPS: V-Sync kapalı, FPS sınırı monitör Hz'inin biraz üstü, gölge/efekt düşük-orta, hareket bulanıklığı ve alan derinliği kapalı, varsa NVIDIA Reflex / AMD Anti-Lag açık. Görüş mesafesini düşürme.", "Competitive FPS: V-Sync off, FPS cap a bit above your monitor Hz, shadows/effects low-medium, motion blur and depth of field off, Reflex / Anti-Lag on. Keep view distance."),
        ["br"] = new("Battle royale: Görüş mesafesi yüksek (uzaktaki rakipler), bitki örtüsü düşük, gölge düşük-orta, efektler düşük, V-Sync kapalı, hareket bulanıklığı kapalı.", "Battle royale: view distance high, foliage low, shadows low-medium, effects low, V-Sync off, motion blur off."),
        ["tactical"] = new("Taktik shooter: Doku orta, gölge orta (gölgeler rakibi gösterir), hacimsel efektler düşük, FSR/DLSS Kalite, V-Sync kapalı.", "Tactical shooter: textures medium, shadows medium (they reveal enemies), volumetrics low, FSR/DLSS Quality, V-Sync off."),
        ["moba"] = new("MOBA: Gölge ve efekt düşük, karakter kalitesi orta, V-Sync kapalı, FPS sınırını monitör Hz'ine yakın tut.", "MOBA: shadows and effects low, character quality medium, V-Sync off, cap FPS near your monitor Hz."),
        ["fighting"] = new("Dövüş oyunu: Oyun 60 FPS kilitlidir; amaç sabit 60. V-Sync açık kalabilir, giriş gecikmesi için oyun içi 'input delay' ayarını en düşüğe al.", "Fighting game: locked to 60 FPS; aim for a stable 60. Lower the in-game input delay setting."),
        ["racing"] = new("Yarış: Gölge orta, yansıma düşük-orta, kalabalık/seyirci düşük, hareket bulanıklığı kapalı, FSR/DLSS Kalite. Direksiyon kullanıyorsan FPS'i sabit tut.", "Racing: shadows medium, reflections low-medium, crowds low, motion blur off, FSR/DLSS Quality."),
        ["sports"] = new("Spor: Kalabalık ve saç/kumaş detaylarını düşür, V-Sync kapalı, FPS'i 60 ya da monitör Hz'inde sabitle.", "Sports: lower crowd and hair/cloth detail, V-Sync off, cap FPS at 60 or your monitor Hz."),
        ["openworld"] = new("Açık dünya: Kalabalık yoğunluğu orta, görüş mesafesi orta, hacimsel bulut/sis düşük, ışın izleme kapalı, FSR/DLSS/XeSS Kalite.", "Open world: crowd density medium, view distance medium, volumetric clouds/fog low, ray tracing off, FSR/DLSS/XeSS Quality."),
        ["action"] = new("Aksiyon: Işın izleme kapalı, FSR/DLSS Kalite, gölge orta, hareket bulanıklığı isteğe bağlı. VRAM çubuğunu aşma.", "Action: ray tracing off, FSR/DLSS Quality, shadows medium. Stay under the VRAM bar."),
        ["rpg"] = new("RPG: Gölge orta, bitki örtüsü orta, kalabalık orta, FSR/DLSS Kalite. Mod kullanıyorsan doku modları VRAM'i doldurabilir.", "RPG: shadows medium, foliage medium, crowds medium, FSR/DLSS Quality. Texture mods can fill VRAM."),
        ["mmo"] = new("MMO: Oyuncu/karakter sayısı ve görüş mesafesi en çok FPS'i yer. Kalabalık alanlarda 'gösterilecek oyuncu sayısı'nı düşür, gölgeleri orta yap.", "MMO: player count and view distance cost the most. Lower 'max visible players' in crowded areas, shadows medium."),
        ["survival"] = new("Hayatta kalma: Bitki örtüsü/çimen mesafesi düşük-orta, gölge orta, hacimsel bulut düşük. Büyük üslerde FPS düşmesi normaldir.", "Survival: foliage/grass distance low-medium, shadows medium, volumetric clouds low. Big bases lower FPS."),
        ["horror"] = new("Korku: Atmosfer için gölge ve ışığı orta tut, hareket bulanıklığı ve film grenini kapat, FSR/DLSS Kalite.", "Horror: keep shadows/lighting medium for atmosphere, turn off motion blur and film grain, FSR/DLSS Quality."),
        ["strategy"] = new("Strateji: FPS'ten çok işlemci önemli. Birim/asker sayısı ve simülasyon hızı yüklenir; arka plan uygulamalarını kapat, gölge ve su kalitesini düşür.", "Strategy: CPU matters more than GPU. Close background apps, lower shadow and water quality."),
        ["building"] = new("Şehir/fabrika kurma: Geç oyunda işlemci darboğazı olur. Gölge düşük, alan derinliği kapalı, nüfus/araç sayısı görsel ayarlarını düşür.", "City/factory builder: late game is CPU-bound. Shadows low, depth of field off, lower visible population/vehicles."),
        ["sim"] = new("Simülasyon: Gölge ve yansıma orta, trafik/yoğunluk düşük-orta, FPS sınırını 60'ta sabitle. Mod sayısı arttıkça yükleme ve FPS düşer.", "Simulator: shadows/reflections medium, traffic density low-medium, cap FPS at 60."),
        ["indie"] = new("Hafif oyun: V-Sync'i kapat ya da FPS'i monitör Hz'ine sabitle; laptopta pil ömrü için FPS sınırı koy.", "Light game: turn V-Sync off or cap FPS at your monitor Hz; cap FPS on laptops to save battery."),
        ["platformer"] = new("Platform: Akıcılık için FPS'i monitör Hz'ine sabitle ve V-Sync'i kapat; giriş gecikmesi azalır.", "Platformer: cap FPS at your monitor Hz and turn V-Sync off for lower input lag."),
        ["coop"] = new("Co-op: Efekt ve parçacıkları düşür (kalabalık sahnelerde FPS düşer), gölge orta, V-Sync kapalı.", "Co-op: lower effects/particles (busy scenes drop FPS), shadows medium, V-Sync off."),
        ["puzzle"] = new("Bulmaca/hikâye: Görsellik önemli; Kalite seviyesini kullan, sadece V-Sync'i kapat ve FPS'i monitör Hz'ine sabitle.", "Puzzle/story: visuals matter; use Quality, just turn V-Sync off and cap FPS at your monitor Hz."),
        ["generic"] = new("BoostRoot bu oyunu henüz tanımıyor; Windows ve ekran kartı ayarları uygulanır. Oyun içinde: V-Sync kapalı, hareket bulanıklığı kapalı, gölge orta, FSR/DLSS Kalite.", "BoostRoot doesn't know this game yet; Windows and GPU settings are applied. In game: V-Sync off, motion blur off, shadows medium, FSR/DLSS Quality."),
    };

    public static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in Normalize(name)) sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        var s = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return s.Length > 34 ? s[..34].TrimEnd('-') : s;
    }

    /// <summary>"Tom Clancy's Rainbow Six® Siege" → "tom clancys rainbow six siege".</summary>
    public static string Normalize(string name)
    {
        var s = name.ToLowerInvariant().Replace("&", " and ", StringComparison.Ordinal)
            .Replace('ı', 'i').Replace('ö', 'o').Replace('ü', 'u').Replace('ş', 's').Replace('ç', 'c').Replace('ğ', 'g').Replace('é', 'e');
        var sb = new StringBuilder();
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c is ' ' or '-' or ':' or '_' or '.' or '/') sb.Append(' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static Dictionary<BoostLevel, LevelSpec> Levels(bool competitive) => new()
    {
        [BoostLevel.Quality] = new(new Dictionary<string, string>(), new GpuPrefs(LowLatency: true, VSync: false, RadeonBoost: false)),
        [BoostLevel.Balanced] = new(new Dictionary<string, string>(), new GpuPrefs(LowLatency: true, VSync: false, RadeonBoost: !competitive)),
        [BoostLevel.MaxFps] = new(new Dictionary<string, string>(), new GpuPrefs(LowLatency: true, VSync: false, RadeonBoost: !competitive, TextureFiltering: "performance", Upscaler: competitive ? "off" : "quality")),
        [BoostLevel.Competitive] = new(new Dictionary<string, string>(), new GpuPrefs(LowLatency: true, VSync: false, RadeonBoost: false, TextureFiltering: "performance", Upscaler: "off")),
    };

    private static GameProfile Make(string id, string name, string genre, Dictionary<string, string> detect, string antiCheat)
    {
        var comp = CompetitiveGenres.Contains(genre);
        IReadOnlyList<RecommendRule> rec = comp ? [new(null, BoostLevel.Competitive)]
            : [new(40, BoostLevel.MaxFps), new(65, BoostLevel.Balanced), new(null, BoostLevel.Quality)];
        return new GameProfile(id, name, 1, antiCheat, comp, "B", detect, new Dictionary<string, string>(), [], [], Levels(comp), rec,
            new Dictionary<string, SettingNote>(), GenreTips.GetValueOrDefault(genre, GenreTips["generic"]), "library");
    }

    /// <summary>Parses profiles/library.txt ("Name;genre;process;anticheat"). Ids already used are skipped.</summary>
    public static IReadOnlyList<GameProfile> Parse(string text, IEnumerable<GameProfile> existing)
    {
        var ids = existing.Select(p => p.Id).ToHashSet();
        var names = existing.Select(p => Normalize(p.Name)).ToHashSet();
        var list = new List<GameProfile>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split(';');
            var name = f[0].Trim();
            if (name.Length == 0 || !names.Add(Normalize(name))) continue;
            var id = "lib-" + Slug(name);
            if (!ids.Add(id)) continue;
            var genre = f.Length > 1 && GenreTips.ContainsKey(f[1].Trim()) ? f[1].Trim() : "generic";
            var detect = new Dictionary<string, string> { ["name"] = name };
            if (f.Length > 2 && f[2].Trim().Length > 0) detect["process"] = f[2].Trim();
            list.Add(Make(id, name, genre, detect, f.Length > 3 && f[3].Trim().Length > 0 ? f[3].Trim() : "none"));
        }
        return list;
    }

    /// <summary>Profile for an installed game nobody wrote a profile for. "dir:" = any process running from the install folder.</summary>
    public static GameProfile Generic(string name, string source, string sourceId, string installPath) =>
        Make("auto-" + Slug(source + "-" + sourceId), name, "generic",
            new Dictionary<string, string> { [source] = sourceId, ["process"] = "dir:" + installPath }, "none");

    private static readonly string[] NotGames = ["redistributable", "steamworks", "proton", "steam linux runtime", "sdk", "dedicated server", "soundtrack", "benchmark", "wallpaper engine", "steamvr", "directx", "vc++", "visual c", "tool", "editor", "launcher", "demo", "playtest", "test server", "pts"];

    public static bool LooksLikeGame(string name)
    {
        var n = name.ToLowerInvariant();
        return !NotGames.Any(x => n.Contains(x, StringComparison.Ordinal));
    }
}
