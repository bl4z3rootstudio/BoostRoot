using BoostRoot.Core.Profiles;

namespace BoostRoot.Core.Tools;

public enum DoctorSeverity { Ok, Info, Warning, Problem }

/// <param name="CanAutoFix">BoostRoot can fix it with one click; otherwise <see cref="Manual"/> explains how.</param>
public sealed record DoctorFinding(string Id, DoctorSeverity Severity, Localized Title, Localized Detail, bool CanAutoFix, Localized? Manual = null);

/// <summary>Facts about one game and the PC, gathered by the platform layer (kept plain so the rules are testable).</summary>
public sealed record DoctorFacts(
    string GameName,
    string Source,
    bool InstallFolderExists,
    bool ExeFound,
    bool LauncherInstalled,
    bool GameRunningButNoWindow,
    double SystemDiskFreeGb,
    double? GameDiskFreeGb,
    bool VcRedistInstalled,
    bool? VanguardRunning,
    bool RiotClientBroken,
    bool HasCompatFlags,
    bool BoostActive,
    bool PendingReboot,
    int DriverAgeDays,
    bool ShaderCacheLarge);

/// <summary>Turns facts into findings. Every finding either has a one-click fix or plain manual steps.</summary>
public static class GameDoctor
{
    public static IReadOnlyList<DoctorFinding> Evaluate(DoctorFacts f)
    {
        var l = new List<DoctorFinding>();
        if (!f.InstallFolderExists)
            l.Add(new("folder-missing", DoctorSeverity.Problem, new("Oyun klasörü bulunamadı", "Game folder not found"),
                new("Oyun taşınmış ya da silinmiş olabilir.", "The game may have been moved or deleted."), false,
                new("Oyunu kendi başlatıcısından (Steam, Epic, Riot...) açıp 'Konum bul' ya da 'Yükle' de; elle eklediysen Boost sayfasından kaldırıp yeniden ekle.",
                    "Open the game's launcher (Steam, Epic, Riot...) and use 'Locate' or 'Install'; if you added it by hand, remove and add it again on the Boost page.")));
        else if (!f.ExeFound)
            l.Add(new("exe-missing", DoctorSeverity.Problem, new("Oyunun .exe dosyası eksik", "The game's .exe is missing"),
                new("Antivirüs silmiş ya da güncelleme yarıda kalmış olabilir.", "An antivirus may have removed it or an update did not finish."),
                f.Source == "steam", f.Source == "steam" ? null
                    : new("Başlatıcıda oyunun ayarlarından 'Doğrula' / 'Onar' seçeneğini çalıştır.", "Run 'Verify' / 'Repair' from the game's settings in its launcher.")));
        if (f.Source == "steam" && f.InstallFolderExists)
            l.Add(new("steam-verify", DoctorSeverity.Info, new("Oyun dosyalarını doğrula", "Verify game files"),
                new("Steam bozuk ya da eksik dosyaları bulup yeniden indirir.", "Steam finds broken or missing files and downloads them again."), true));
        if (!f.LauncherInstalled && f.Source is "steam" or "epic" or "riot")
            l.Add(new("launcher-missing", DoctorSeverity.Problem, new("Oyunun başlatıcısı bulunamadı", "The game's launcher was not found"),
                new("Oyun başlatıcısı olmadan açılmaz.", "The game cannot start without its launcher."), false,
                new("Steam, Epic Games ya da Riot Client'ı resmi sitesinden yeniden kur.", "Reinstall Steam, Epic Games or Riot Client from the official site.")));
        if (f.GameRunningButNoWindow)
            l.Add(new("stuck-process", DoctorSeverity.Problem, new("Oyun arkada takılı kalmış", "The game is stuck in the background"),
                new("Önceki açılış kapanmamış; bu yüzden oyun yeniden açılmıyor.", "A previous start never closed, so the game will not open again."), true));
        if (f.RiotClientBroken)
            l.Add(new("riot-client", DoctorSeverity.Problem, new("Riot Client dosyası bozuk", "Riot Client file is broken"),
                new("\"A JavaScript error occurred in the main process\" hatasının sebebi budur.", "This causes the \"A JavaScript error occurred in the main process\" error."), true));
        if (f.VanguardRunning == false)
            l.Add(new("vanguard", DoctorSeverity.Problem, new("Vanguard (Riot hile koruması) çalışmıyor", "Vanguard (Riot anti-cheat) is not running"),
                new("VALORANT ve LoL, Vanguard çalışmadan açılmaz.", "VALORANT and LoL do not start without Vanguard."), true,
                new("Düzeltmeden sonra bilgisayarı yeniden başlat. Olmazsa Ayarlar > Uygulamalar'dan 'Riot Vanguard'ı kaldırıp oyunu açınca yeniden kurdur.",
                    "Restart the PC after the fix. If it still fails, uninstall 'Riot Vanguard' in Settings > Apps and let the game reinstall it.")));
        if (!f.VcRedistInstalled)
            l.Add(new("vcredist", DoctorSeverity.Problem, new("Visual C++ kütüphaneleri eksik", "Visual C++ runtime is missing"),
                new("Çoğu oyun bu Microsoft kütüphanesi olmadan açılmaz (VCRUNTIME140.dll hatası).", "Most games will not start without this Microsoft runtime (VCRUNTIME140.dll error)."), true,
                new("Microsoft'un sitesinden 'Visual C++ Redistributable x64 (2015-2022)' indirip kur.", "Download and install 'Visual C++ Redistributable x64 (2015-2022)' from Microsoft.")));
        if (f.SystemDiskFreeGb < 10 || f.GameDiskFreeGb < 5)
            l.Add(new("disk-space", DoctorSeverity.Warning, new("Disk neredeyse dolu", "Disk is almost full"),
                new("Güncellemeler ve oyun kayıtları için yer kalmayabilir.", "Updates and saves may fail without free space."), true,
                new("Kullanmadığın oyunları ve büyük dosyaları sil.", "Delete games and big files you no longer use.")));
        if (f.HasCompatFlags)
            l.Add(new("compat-flags", DoctorSeverity.Warning, new("Oyunda uyumluluk modu açık", "Compatibility mode is set on the game"),
                new("Eski Windows uyumluluk ayarları yeni oyunlarda çökme ve siyah ekran yapabilir.", "Old Windows compatibility settings can cause crashes and black screens in new games."), true));
        if (f.BoostActive)
            l.Add(new("boost-active", DoctorSeverity.Info, new("Bu oyunda BoostRoot ayarları açık", "BoostRoot settings are active for this game"),
                new("Sorun Boost'tan sonra başladıysa, geri alıp dene.", "If the problem started after Boost, undo it and try again."), true));
        if (f.ShaderCacheLarge)
            l.Add(new("shader-cache", DoctorSeverity.Info, new("Gölgelendirici önbelleği büyük", "Shader cache is large"),
                new("Sürücü güncellemesinden sonra takılma ya da çökme varsa temizlemek düzeltir; ilk açılış biraz uzun sürer.", "Clearing it fixes stutter or crashes after a driver update; the first start takes a bit longer."), true));
        if (f.DriverAgeDays > 180)
            l.Add(new("driver-old", DoctorSeverity.Warning, new($"Ekran kartı sürücüsü {f.DriverAgeDays} günlük", $"Graphics driver is {f.DriverAgeDays} days old"),
                new("Yeni oyunlar eski sürücüde çökebilir.", "New games can crash on old drivers."), false,
                new("Güncellemeler sayfasından ya da NVIDIA/AMD'nin sitesinden en son sürücüyü kur.", "Install the latest driver from the Updates page or NVIDIA/AMD's site.")));
        if (f.PendingReboot)
            l.Add(new("reboot", DoctorSeverity.Warning, new("Windows yeniden başlatma bekliyor", "Windows is waiting for a restart"),
                new("Yarım kalmış güncellemeler oyunların açılmasını engelleyebilir.", "Unfinished updates can stop games from starting."), false,
                new("Bilgisayarı yeniden başlat.", "Restart the PC.")));
        if (l.All(x => x.Severity is DoctorSeverity.Info or DoctorSeverity.Ok))
            l.Insert(0, new("all-good", DoctorSeverity.Ok, new("Belirgin bir sorun bulunamadı", "No obvious problem found"),
                new("Oyun hâlâ açılmıyorsa dosyalarını doğrulamak ve bilgisayarı yeniden başlatmak çoğu sorunu çözer.", "If it still will not start, verifying its files and restarting the PC fixes most problems."), false));
        return l;
    }
}
