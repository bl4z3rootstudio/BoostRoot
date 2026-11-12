# BoostRoot 1.26.11 — Tasarım Belgesi

Tarih: 12 Kasım 2026 · Yapımcı: bl4z3root · Durum: Onay bekliyor

---

## 1. Amaç

BoostRoot, Windows oyuncuları için **ücretsiz, reklamsız, hesapsız** bir oyun performans uygulamasıdır. Kullanıcı oyununu seçer, tek tuşa basar; BoostRoot o oyunun ve bilgisayarın en iyi FPS ayarlarını uygular, isterse oyunu başlatır. Bunun yanında PC analizi, FPS testi, stres testleri, internet analizi, klip alma ve oyun içi gösterge sunar.

**Başarı ölçütleri**
- Desteklenen bir oyunda "Boostla" sonrası FPS testi, gerçek ve ölçülmüş bir artış gösterir. Uygulama hiçbir yerde uydurma rakam göstermez.
- Kullanıcının çözünürlüğü, tuşları, fare hassasiyeti ve nişangahı **hiçbir koşulda** değişmez.
- Hiçbir özellik hile koruması olan oyunlarda ban riski oluşturmaz.
- Her değişiklik tek tıkla geri alınabilir. Kaldırma sırasında BoostRoot'un yaptığı tüm değişiklikler geri alınabilir.
- BoostRoot kapalıyken bilgisayarda hiçbir işlemi çalışmaz.
- Kullanıcı arayüzü basittir. Karmaşık seçenekler "Uzman modu" ve "Gelişmiş" sekmelerinin arkasındadır.

**Rakiplerden farkı** (rakip araştırması 12.11.2026):
| Rakipte sorun | BoostRoot'ta |
|---|---|
| Hone: optimizasyonların çoğu aylık ücretli | Her şey ücretsiz |
| Razer Cortex: şişkinlik, yüksek RAM, reklam, kaldırınca ayar bozulması | Kapalıyken 0 iş; özellikler varsayılan kapalı; kaldırıcı her şeyi geri alır |
| Klasik booster'lar 1–10 FPS kazandırır, bazen düşürür | Asıl kazanç oyun içi ayarlardan; FPS testiyle kanıtlanır |
| Razer "Booster Prime" yalnızca birkaç oyunda oyun ayarı yapar | ~33 oyunda oyun ayarı |
| LagoFast hız aşırtmasının kararlılık riski | Stres testiyle doğrulanan, sorunlu çıkarsa kendini geri alan güvenli hız aşırtma |
| Medal/Outplayed: kayıp/kesik klipler, FPS düşüşü | Donanım kodlayıcı, atomik dosya yazımı, reklam yok |
| Afterburner/RTSS: Vanguard vb. altında engellenir | Oyuna girmeyen güvenli pencere overlay |
| İnternette dolaşan hurafe ayarlar (HPET vb.) | Yalnızca etkisi ölçülmüş, güvenli ayarlar |

---

## 2. Kapsam

### 2.1 Sürüm 1.26.11 içinde
1. İlk açılış sihirbazı ve PC Karnesi
2. PC analizi ve donanım sorun tespiti (XMP, monitör Hz, sürücü yaşı, ağ hızı vb.)
3. Oyun bulucu (Steam, Epic, Riot, Xbox/Game Pass, Minecraft başlatıcıları, Roblox) + elle ekleme
4. ~33 oyun profili, 4 seviye (Kalite / Dengeli / Maks FPS / Rekabetçi) + PC'ye göre öneri + Uzman modu
5. Boost akışı: oyun ayarları, arka plan kapatma, Windows ayarları, GPU sürücü profili, başlatma sorusu, isteğe bağlı "oyun bitene kadar bekle"
6. Windows optimizasyonu (Güvenli + Gelişmiş sekmesi)
7. NVIDIA ve AMD sürücü profili otomasyonu
8. Güvenli GPU hız aşırtma
9. FPS testi (öncesi/sonrası) ve paylaşım kartı
10. Stres testleri: CPU, GPU, RAM, kombine
11. İnternet analizi (yalnızca ölçüm)
12. Araçlar: sürücü güncellik kontrolü, çöp temizleyici, başlangıç yöneticisi
13. Klip sistemi: anlık tekrar, tam kayıt, basit düzenleyici, ekran görüntüsü
14. Oyun içi gösterge (overlay) ve nişangah overlay'i
15. Oynama süresi ve FPS geçmişi
16. Discord'a tek tıkla gönderme
17. Yedekleme, geri alma, geri yükleme noktası
18. Otomatik güncelleme (uygulama) + ayrı imzalı profil paketi güncellemesi
19. Türkçe (ana) + İngilizce
20. Kurulum dosyası, GitHub deposu, tanıtım sitesi, bağış (GitHub Sponsors + kripto)

### 2.2 Kapsam dışı (bilerek)
- **İnternet yönlendirme / ping düşürme sunucuları** (LagoFast'ın ücretli kısmı): sunucu altyapısı gerektirir.
- **DNS, TCP, ağ kartı gelişmiş ayarlarını değiştirme:** SplitWire gibi araçları bozar, faydası kanıtlanmamıştır. Ağ tarafında yalnızca ölçüm ve tavsiye yapılır.
- **Oyuna kod enjeksiyonu, bellek okuma/yazma, grafik API kancası:** Ban sebebidir.
- **Çözünürlük, tuş, hassasiyet, nişangah değiştirme.**
- 32-bit Windows, Windows 7/8, macOS, Linux.
- Hesap sistemi, bulut senkronizasyonu, reklam, otomatik veri toplama.
- Yapay zekâ ile otomatik "öldürme anı" klibi: oyunların resmi olay API'si gerekir. İlerideki bir sürüme bırakılmıştır.

---

## 3. Temel ilkeler (her özellikte geçerli)

1. **Dokunulmazlar:** Çözünürlük, pencere modu, tuş atamaları, fare hassasiyeti, nişangah, oyun içi ses/dil ayarları. Her profil bu anahtarları `neverTouch` listesinde tutar. Yazma motoru bu listedeki bir anahtara yazmayı **kod seviyesinde reddeder**; bu kural her oyun için ayrı otomatik testle doğrulanır.
2. **Hile koruması güvenliği:** Korumalı bir oyunun (Vanguard, EAC, BattlEye, VAC, Ricochet, Javelin) işlemine, belleğine, DLL'lerine veya kurulum dosyalarına dokunulmaz. Yalnızca oyunun kullanıcı ayar dosyaları (oyun kapalıyken) ve işletim sistemi/sürücü seviyesindeki ayarlar değişir. Korumalı oyunlarda süreç önceliği ve CPU çekirdek ataması da yapılmaz.
3. **Önce yedek, sonra değişiklik, sonra doğrulama.** Hata olursa otomatik geri alma.
4. **Dürüstlük:** Rakamlar ya ölçülür ya "tahmini" diye etiketlenir. Bir ayarın etkisi bilinmiyorsa uygulanmaz.
5. **Hafiflik:** Kapalıyken sıfır işlem. Açıkken boşta hedef < 60 MB RAM ve < %1 CPU. Pencere küçültülünce animasyonlar durur.
6. **Gizlilik:** Hiçbir veri otomatik gönderilmez. İnternete yalnızca şunlar için çıkılır: güncelleme kontrolü (GitHub), profil paketi (GitHub), oyun kapak resimleri (Steam/Epic CDN, bir kez indirilip önbelleğe alınır), hız testi (Cloudflare), ping testleri, kullanıcının istediği Sodium indirmesi (Modrinth), Discord'a gönderme (kullanıcının kendi webhook'u). Ayarlardan "çevrimdışı mod" seçilebilir.

---

## 4. Platform ve teknoloji

| Konu | Karar |
|---|---|
| İşletim sistemi | Windows 10 (1809+) ve Windows 11, Home/Pro, x64 |
| Dil / çalışma ortamı | C#, .NET 10 (LTS) |
| Arayüz | Avalonia UI, önceden derlenmiş (NativeAOT) tek dosya |
| Pencere | Kendi koyu başlık çubuğu, boyutlandırılabilir, en az 1100×700 |
| Yetki | Her açılışta yönetici (uygulama bildiriminde `requireAdministrator`) |
| NVIDIA | NVAPI (sürücü profilleri — DRS, sıcaklık/saat, hız aşırtma) |
| AMD | ADLX SDK (oyun profili ayarları, sıcaklık/saat, ayarlama) |
| Intel GPU | Yalnızca okuma (WMI/DXGI); profil otomasyonu yok |
| FPS ölçümü | PresentMon (Intel, MIT lisanslı) uygulamayla birlikte gelir |
| CPU sıcaklığı | LibreHardwareMonitor kütüphanesi + PawnIO sürücüsü; ilk ihtiyaçta kullanıcıya sorularak kurulur |
| Ekran yakalama | Windows.Graphics.Capture (yedek: DXGI Desktop Duplication) |
| Video kodlama | Media Foundation donanım kodlayıcıları (NVENC / AMF / Quick Sync); yoksa yazılım kodlayıcı + uyarı |
| Kurulum | Inno Setup, BoostRoot temalı 3 adım (Hoş geldin → Kur → Bitti) |
| Derleme | GitHub Actions (windows-latest) |
| Lisans | GPL-3.0 (kod açık; türev çalışmalar da açık kalmak zorunda). "BoostRoot" adı ve logosu lisansa dahil değildir. |

---

## 5. Mimari

Kod, birbirinden bağımsız test edilebilir projelere bölünür. Windows'a bağımlı olmayan iş mantığı ayrı tutulur ki her derlemede otomatik test edilebilsin.

```
BoostRoot.sln
├─ src/
│  ├─ BoostRoot.App            Avalonia arayüzü (ekranlar, tema, çeviriler)
│  ├─ BoostRoot.Core           İş mantığı; Windows'a bağımlı değil
│  │    ├─ Profiles            Profil şeması, yükleme, seviye seçimi, öneri motoru
│  │    ├─ ConfigWriters       INI, JSON, XML, CFG, key=value, Unity Registry (soyut), Minecraft options.txt
│  │    ├─ Safety              neverTouch denetimi, yedek/geri al günlüğü (journal)
│  │    ├─ Scoring             PC Karnesi puanlama
│  │    ├─ Versioning          1.YY.M(.n) karşılaştırma
│  │    └─ Abstractions        IRegistry, IFileSystem, IProcessManager, IGpuVendor...
│  ├─ BoostRoot.Platform.Windows   Kayıt defteri, güç planları, servisler, süreçler,
│  │                                WMI donanım bilgisi, geri yükleme noktası, kısayollar
│  ├─ BoostRoot.Gpu.Nvidia     NVAPI sarmalayıcı
│  ├─ BoostRoot.Gpu.Amd        ADLX sarmalayıcı
│  ├─ BoostRoot.Games          Oyun bulucu (Steam VDF, Epic manifest, Riot, Xbox, Minecraft, Roblox), başlatıcılar
│  ├─ BoostRoot.Bench          PresentMon sürücüsü, FPS testi, paylaşım kartı üretimi
│  ├─ BoostRoot.Stress         CPU/GPU/RAM/kombine yükleri + sensör izleme
│  ├─ BoostRoot.Net            Hız testi, ping/jitter/kayıp, bağlantı tavsiyeleri
│  ├─ BoostRoot.Capture        Anlık tekrar tamponu, kayıt, ekran görüntüsü, düzenleyici
│  ├─ BoostRoot.Overlay        Şeffaf üst pencere (gösterge + nişangah)
│  ├─ BoostRoot.Agent          İsteğe bağlı oyun-sırası arka plan işlemi (bekle/dondur, klip, overlay)
│  └─ BoostRoot.Updater        Uygulama ve profil paketi güncellemeleri, imza doğrulama
├─ profiles/                   Oyun profilleri (JSON) + imzalı paket üretimi
├─ installer/                  Inno Setup betiği ve tema görselleri
├─ site/                       Tanıtım sitesi (GitHub Pages, TR/EN)
├─ tests/                      Birim + entegrasyon testleri
└─ .github/workflows/          Derleme, test, paketleme, sürüm
```

**İki işlem modeli**
- **BoostRoot.exe (arayüz):** Kullanıcı açınca çalışır, kapatınca tamamen kapanır.
- **BoostRoot.Agent.exe:** Yalnızca kullanıcı "Oyun bitene kadar bekle", "Klip" veya "Overlay" özelliklerinden birini açtıysa ve bir oyun çalışıyorsa vardır. Oyun kapanınca kendini kapatır. Hedef RAM: overlay için < 30 MB; klip tamponu açıkken tampon süresi kadar ek bellek. Arayüz ile adlandırılmış kanal (named pipe) üzerinden konuşur. Windows ile birlikte başlamaz.
  - Klip veya overlay sürekli açık olsun isteyenler için Ayarlar'da "Oyun açılınca otomatik başla" seçeneği bulunur (varsayılan **kapalı**). Bu seçenek açılırsa yalnızca küçük bir oyun-algılayıcı görev (Görev Zamanlayıcı, oturum açılışında, < 10 MB) çalışır; kullanıcıya bu açıkça söylenir.

---

## 6. Modüller

### 6.1 İlk açılış sihirbazı
Adımlar: Hoş geldin (dil seçimi) → Tarama animasyonu (donanım, oyunlar, Windows durumu, isteğe bağlı kısa ping testi) → **PC Karnesi** → Bulunan oyunlar → "Önerilenleri uygula" veya "Kendim seçeceğim". İlk değişiklikten önce Windows geri yükleme noktası oluşturulur (sistem koruması kapalıysa kullanıcıya sorulur; reddederse yalnızca BoostRoot yedekleri kullanılır).

### 6.2 PC Analizi ve Karnesi
- **Okunanlar:** CPU (model, çekirdek, saat), GPU (üretici, model, VRAM, sürücü sürümü ve tarihi), RAM (miktar, çalışan hız, modüllerin desteklediği hız), diskler (tür, boş yer), monitörler (çözünürlük, mevcut Hz, desteklenen en yüksek Hz), ağ bağdaştırıcısı (tür, bağlantı hızı), Windows sürümü, güç planı, Game Mode, HAGS, Bellek bütünlüğü, laptop/masaüstü.
- **Puan (0–100):** GPU %45, CPU %25, RAM %15, disk %5, monitör/ayar sağlığı %10. Puan tablosu Core içinde sabit veriyle tutulur ve test edilir.
- **Sorun tespiti** (her biri için "Nasıl düzeltilir?" rehberi veya "Düzelt" düğmesi):
  - RAM desteklenen hızın altında → BIOS'ta XMP/EXPO rehberi (anakart üreticisine göre tuş bilgisi)
  - Monitör en yüksek Hz'de değil → Windows'tan düzeltilir
  - GPU sürücüsü 6 aydan eski → resmi indirme sayfası
  - Ağ kartı 1 Gbps yerine 100 Mbps → kablo/port tavsiyesi
  - Ağ kartı güç tasarrufu açık → düzelt
  - Wi-Fi kullanılıyor → kablo tavsiyesi
  - Ağ kartında Windows'un genel sürücüsü var → resmi sürücü bağlantısı
  - Sistem diski %90+ dolu → çöp temizleyici
  - Resizable BAR destekli ama kapalı (okunabildiğinde) → BIOS rehberi

### 6.3 Oyun Bulucu
Steam (`libraryfolders.vdf` + `appmanifest_*.acf`), Epic (`ProgramData\Epic\EpicGamesLauncher\Data\Manifests`), Riot (`RiotClientInstalls.json` + `C:\Riot Games`), Xbox/Game Pass (`XboxGames` klasörleri + paket listesi), Minecraft (resmi başlatıcı, Modrinth, Prism, CurseForge örnekleri), Roblox (`%LOCALAPPDATA%\Roblox`). Bulunamayan oyun için "Oyun ekle" → exe seçimi → profil eşleştirme (yoksa yalnızca Windows/sürücü boost'u). Kapak görselleri ilk görüldüğünde indirilir ve `%LOCALAPPDATA%\BoostRoot\covers` içinde saklanır; çevrimdışıysa oyun ikonu kullanılır.

### 6.4 Oyun Profilleri
**Şema (özet):**
```json
{
  "id": "valorant",
  "name": "VALORANT",
  "schemaVersion": 1,
  "antiCheat": "vanguard",
  "competitive": true,
  "detect": { "riot": "valorant" },
  "launch": { "riot": "valorant" },
  "configs": [
    { "id": "gus", "format": "ini",
      "path": "%LOCALAPPDATA%/VALORANT/Saved/Config/*/WindowsClient/GameUserSettings.ini",
      "pick": "newest" }
  ],
  "neverTouch": ["gus:*ResolutionSize*", "gus:*FullscreenMode*", "gus:*Sensitivity*"],
  "levels": {
    "quality":     { "set": { } },
    "balanced":    { "set": { } },
    "maxfps":      { "set": { } },
    "competitive": { "set": { }, "gpu": { "antiLag": true, "boost": false } }
  },
  "recommend": [ { "ifScoreBelow": 45, "level": "maxfps" }, { "else": "competitive" } ],
  "tips": { "tr": "...", "en": "..." },
  "verifiedOn": "2026-11"
}
```
- Her ayar için TR/EN açıklama ve tahmini etki ("FPS etkisi: yüksek, görüntü etkisi: düşük") bulunur; Uzman modunda ayar ayar gösterilir.
- Kullanıcı Uzman modunda kendi profilini kaydedip `.brprofile` dosyası olarak paylaşabilir. İçe aktarılan profil de `neverTouch` denetiminden geçer.
- **Profil paketi:** Tüm profiller tek pakette, Ed25519 ile imzalı. Uygulama içindeki genel anahtarla doğrulanmayan paket reddedilir. Paket uygulamadan bağımsız güncellenir.

**Oyun listesi ve otomasyon seviyesi**
Her oyun uygulama sırasında tek tek araştırılır ve aşağıdaki iki gruptan birine yerleştirilir:
- **A — Tam:** Oyunun ayar dosyası/kaydı düzenlenir + sürücü + Windows.
- **B — Kısmi:** Oyun ayarları düzenlenemiyor (şifreli/bulut). Sürücü + Windows uygulanır; oyun içi ayarlar için adım adım kart gösterilir.

| Grup | Oyunlar |
|---|---|
| Rekabetçi | VALORANT, Counter-Strike 2, PUBG, Fortnite, Apex Legends, League of Legends, Rainbow Six Siege, Rust, Rocket League |
| Hayatta kalma / ko-op | 7 Days to Die, Valheim, Sons of the Forest, Palworld, Stranded Deep, PEAK, Lethal Company, Phasmophobia |
| Hikâyeli / açık dünya | The Last of Us Part I, The Last of Us Part II, GTA V, Red Dead Redemption 2, Cyberpunk 2077, Elden Ring, Hogwarts Legacy |
| Diğer | Minecraft (Java), Roblox, Euro Truck Simulator 2, Forza Horizon 5, Ranch Simulator, Banker Simulator |

Ön tahmin: Forza Horizon 5 ve The Last of Us kısmi (B) gruba düşebilir. Kesin sınıflandırma uygulama sırasında yapılır ve README'de yayımlanır.

**Seviye anlamları**
- **Kalite:** Görüntü neredeyse aynı. Yalnızca görsel katkısı düşük ama maliyeti yüksek ayarlar (hareket bulanıklığı, film greni, renk sapması, alan derinliği, aşırı hacimsel sis/bulut, ekran-alanı yansımalarının en üst kademesi) düşer. Hikâyeli oyunlarda FSR/DLSS/XeSS **Kalite** modu açılır (desteklenen kartlarda).
- **Dengeli:** Gölge, ortam perdeleme ve efektler bir-iki kademe düşer; dokular ve model kalitesi korunur.
- **Maks FPS:** Her ayar FPS için. Doku kalitesi yalnızca VRAM yetersizse düşer.
- **Rekabetçi:** Maks FPS + düşük gecikme (NVIDIA Reflex/Anti-Lag açık, dikey eşitleme kapalı, sınırsız ya da monitör Hz'inin üstünde FPS) + görüş netliği (bulanıklık yok, sis düşük). Rekabetçi oyunlarda FSR/DLSS kapalı kalır.

**Minecraft özel:** Java sürümünde "Performans modlarını kuralım mı?" sorusu. Evet → Modrinth API'den, mevcut Minecraft sürümüne uyan Fabric Loader + Sodium (+ uygun ise Lithium, FerriteCore) **ayrı bir profil/örnek** olarak kurulur. Kullanıcının mevcut modlu örneklerine dokunulmaz.

### 6.5 Boost akışı
1. Oyun seçilir, seviye seçilir (önerilen işaretli).
2. Oyun çalışıyorsa: "Ayarlar oyun kapalıyken uygulanmalı, oyunu kapat" uyarısı.
3. Yedek → oyun ayar dosyaları → doğrulama.
4. Arka plan kapatma: kullanıcının seçtiği liste (varsayılan öneriler: tarayıcılar, Spotify, diğer başlatıcılar). Önce nazik kapatma, 4 sn sonra zorla. Asla kapatılmayanlar: ses sürücüleri, hile korumaları, Discord (varsayılan olarak listede değil), Windows çekirdek işlemleri, SplitWire ve benzeri ağ araçları.
5. Windows oyun ayarları (6.6 Güvenli liste).
6. GPU sürücü profili (6.7).
7. "Oyunu başlatayım mı?" → Evet: platform URI'siyle (steam://rungameid, Riot Client argümanları, com.epicgames.launcher://, Xbox shell:AppsFolder) başlatır. Hayır: "Ben açarım".
8. "Oyun bitene kadar bekle" açıksa: Agent başlar. Oyun açıkken listedeki programları **askıya alır** (dondurur, kapatmaz); oyun kapanınca geri açar. Askıya alma güvenli olmayan programlar (ses, iletişim, sürücü yardımcıları) listeye eklenemez.
9. Sonuç ekranı: yapılanların listesi + "FPS testi yap" önerisi.

### 6.6 Windows Optimizasyonu
**Güvenli (Boost ile otomatik uygulanabilir):**
- Güç planı: Yüksek Performans (masaüstü). Laptop'larda da aynısı uygulanır (kullanıcı kararı); ancak pildeyken bir kerelik bilgi notu gösterilir.
- İşlemci güç kısıtlamasını (Power Throttling) oyun exe'si için kapatma
- Windows Oyun Modu açık
- Xbox Game DVR arka plan kaydı kapalı
- Pencereli oyun iyileştirmeleri açık (Win11)
- Oyun exe'si için "Yüksek performans" GPU tercihi
- Korumasız oyunlarda süreç önceliği "Yüksek"
- Görsel efektler: saydamlık kapalı (isteğe bağlı)

**Gelişmiş sekmesi (her biri uyarılı, tek tek, geri alınabilir):**
- Bellek bütünlüğü (HVCI) kapatma: güvenlik uyarısıyla, yeniden başlatma gerektirir
- Gereksiz servisler (Xbox servisleri, Fax, Uzak Kayıt Defteri vb.), sabit ve gözden geçirilmiş bir liste
- Zamanlayıcı çözünürlüğü (Win11 `GlobalTimerResolutionRequests`)
- HAGS (Donanım hızlandırmalı GPU zamanlaması) aç/kapa, kartın desteğine göre
- Tarayıcı ve Discord donanım hızlandırma önerisi (yalnızca bilgi)
- Ağ kartı güç tasarrufu kapatma

**Asla yapılmayanlar:** HPET/`useplatformclock` değişiklikleri, MSI modu toplu değişikliği, TCP/Nagle/DNS/ağ ayarları, Windows Defender'ı kapatma, Windows Update'i kapatma, sayfa dosyasını kapatma.

### 6.7 GPU sürücü profili
- **NVIDIA (NVAPI DRS):** Oyun exe'sine bağlı profil: Düşük gecikme modu, dikey eşitleme kapalı (oyun yönetiyorsa uygulama tercihi), güç yönetimi "Maksimum performans tercih et", doku filtreleme kalitesi "Yüksek performans" (Maks FPS/Rekabetçi), gölgelendirici önbelleği açık. Mevcut değerler yedeklenir.
- **AMD (ADLX):** Oyun profili: Radeon Anti-Lag açık, Chill kapalı, dikey yenileme beklemesi kapalı, Radeon Boost **Rekabetçi oyunlarda kapalı, diğerlerinde açık**, doku filtreleme "Performans". Kullanıcının renk/doygunluk ayarlarına dokunulmaz.
- **Intel/diğer:** Yalnızca bilgi kartı.

### 6.8 Güvenli GPU hız aşırtma
Araçlar sekmesinde, isteğe bağlı. Akış: Mevcut değerleri yedekle → küçük adımlarla çekirdek/bellek saatini artır (NVAPI/ADLX ayarlama) → her adımda 60 sn GPU stres testi + hata/çökme/sıcaklık denetimi → kararsızlık görülürse bir önceki kararlı adıma dön → son değere %3 güvenlik payı → sonuç: "+X MHz, kararlı". Sınırlar: üretici güç limitini ve fan eğrisini aşmaz. Sürücü çökmesi algılanırsa (TDR) bir sonraki açılışta otomatik varsayılana döner. "Varsayılana dön" düğmesi her zaman görünür.

### 6.9 FPS testi ve paylaşım kartı
- "Testi başlat" → oyuna geç → BoostRoot PresentMon ile o oyunun karelerini 60/120 sn (seçilebilir) kaydeder; kısayol veya süre bitimiyle durur.
- Sonuç: ortalama FPS, %1 en düşük, %0.1 en düşük, kare süresi grafiği.
- "Önce" ve "sonra" testleri eşleştirilir; aynı sahne/harita tavsiyesi verilir.
- Paylaşım kartı: hikâye (1080×1920) ve kare (1080×1080) PNG. İçerik: oyun, önce → sonra, yüzde, %1 en düşük, seviye, donanım özeti, "Ücretsiz · BoostRoot". Yalnızca gerçek ölçüm sonuçlarıyla oluşturulabilir.
- Geçmiş: Her oyunun test sonuçları zaman çizelgesinde saklanır.

### 6.10 Stres testleri
- **CPU:** Tüm çekirdeklerde AVX2 (yoksa SSE) kayan nokta + tamsayı yükü.
- **GPU:** Direct3D 11 ile ağır gölgelendirici sahne (pencerede, seçilebilir çözünürlük).
- **RAM:** Kullanılabilir belleğin seçilen yüzdesinde (varsayılan %70) desen yazma/okuma ve hata doğrulama.
- **Kombine:** Hepsi birlikte (güç kaynağı ve soğutma testi).
- **Süre:** 5 / 15 / 60 dk + "Özel" (1–120 dk).
- **Canlı grafikler:** sıcaklık, saat hızı, yük, güç, fan.
- **Güvenlik kesintisi:** GPU ≥ 90 °C, CPU ≥ 95 °C (veya üreticinin TjMax − 5 °C değeri), sistem uyarısı ya da hata → test durur.
- **Karne:** Geçti / Dikkat (ısınma kaynaklı yavaşlama, yüksek sıcaklık) / Kaldı (hata, çökme). Her sonuca açıklama ve öneri eklenir.
- CPU sıcaklığı için PawnIO yalnızca ilk ihtiyaçta, açıklamalı onayla kurulur; kaldırıcı onu da kaldırır.

### 6.11 İnternet analizi
- Hız testi: Cloudflare hız uç noktalarıyla indirme/yükleme + boşta ve yük altında gecikme.
- Oyun sunucuları: Seçilen oyunların bilinen bölge sunucularına (İstanbul, Frankfurt, Paris, Stockholm vb.) ping, dalgalanma (jitter) ve paket kaybı. Sunucu listesi profil paketiyle güncellenir.
- Tavsiyeler: Wi-Fi → kablo; bağlantı 100 Mbps → kablo/port; yüksek dalgalanma → arka plan indirmeleri; kayıp → modem/İSS. **Hiçbir ayar değiştirilmez.**

### 6.12 Araçlar
- **Sürücü kontrolü:** GPU sürücü sürümü ve tarihi; 6 aydan eskiyse resmi sayfa bağlantısı (BoostRoot sürücü indirmez/kurmaz).
- **Çöp temizleyici:** Windows Temp, kullanıcı Temp, küçük resim önbelleği, hata raporları, Teslim İyileştirme önbelleği, eski Windows Update dosyaları (yönetici). **Dokunulmaz:** DirectX/NVIDIA/AMD gölgelendirici önbellekleri, oyun önbellekleri, İndirilenler, Geri Dönüşüm Kutusu (yalnızca ayrı onayla).
- **Başlangıç yöneticisi:** Windows ile başlayan uygulamalar, etki tahminiyle; aç/kapa (geri alınabilir).

### 6.13 Klip sistemi
- **Anlık tekrar:** Agent içinde; ekran kartının donanım kodlayıcısıyla, RAM'de halka tampon. Varsayılan son 30 sn, 1080p, 60 FPS, H.264 (destekliyse HEVC seçeneği). Ayarlar: süre 10–300 sn, çözünürlük (720p / 1080p / 1440p / ekran), FPS (30/60/120/144), kalite (bit hızı), kodlayıcı, kayıt klasörü.
- **Tam kayıt:** Başlat/durdur, aynı ayarlar.
- **Ses:** Sistem sesi / mikrofon / ikisi / ayrı kanallar (kullanıcı seçer).
- **Kısayollar:** Alt+F10 klip, Alt+F9 kayıt, Alt+F1 ekran görüntüsü (değiştirilebilir; çakışma uyarısı verir).
- **Güvenilirlik:** Klip önce `.part` dosyasına yazılır, tamamlanınca adı değiştirilir (yarım dosya olmaz); disk dolarsa uyarı; kaydetme anında kısa bir ekran bildirimi (overlay açıksa) ve ses.
- **Klasörler:** `Videolar\BoostRoot\<Oyun>\<tarih_saat>.mp4`, ekran görüntüleri `Resimler\BoostRoot\<Oyun>\`.
- **Galeri ve düzenleyici:** Oyun kapaklarıyla galeri; kesme/kırpma, birden çok parçayı birleştirme, dikey (9:16) kırpma, yeniden kodlamadan hızlı kayıt (mümkünse).
- **Discord'a gönder:** Kullanıcının kendi webhook URL'si (Ayarlar'da, açıklamalı). Discord boyut sınırını aşan klipler için otomatik sıkıştırma önerisi.
- **Yakalama yöntemi:** Windows.Graphics.Capture / DXGI ile, oyuna girmeden.

### 6.14 Oyun içi gösterge ve nişangah
- Şeffaf, tıklanamaz, her zaman üstte duran ayrı bir pencere. Oyuna hiçbir şekilde enjekte edilmez.
- **Gösterge öğeleri** (her biri aç/kapa, sıralanabilir): FPS, kare süresi, %1 en düşük, GPU kullanımı/sıcaklık/saat/VRAM, CPU kullanımı/sıcaklık, RAM kullanımı, ping (seçili sunucu), saat.
- Konum (4 köşe + özel), boyut, saydamlık, renk. Alt+O ile aç/kapa.
- FPS verisi PresentMon'dan; sensörler 1 sn aralıkla.
- **Sınır:** Özel tam ekran (exclusive fullscreen) modunda görünmeyebilir. BoostRoot bunu algılar ve "Göstergeyi görmek için kenarlıksız pencere moduna geç" bilgisini verir; oyunun pencere modunu **kendisi değiştirmez** (dokunulmaz kuralı).
- **Nişangah overlay'i:** Hazır şekiller + renk/boyut/kalınlık/açıklık; ilk açılışta "Oyun içine girmez, ama bazı turnuva kuralları yasaklayabilir" bilgisi.

### 6.15 Oynama süresi ve geçmiş
Agent çalıştığında veya BoostRoot başlatıcısıyla açılan oyunlarda oturum süresi kaydedilir; FPS testleri ve boost geçmişiyle birlikte oyun sayfasında grafik olarak gösterilir. Veriler yerelde SQLite'ta tutulur.

### 6.16 Yedekleme ve geri alma
- **Journal:** Her işlem (dosya, kayıt defteri, servis, güç planı, sürücü profili, başlangıç öğesi) önceki değeriyle birlikte `%PROGRAMDATA%\BoostRoot\journal` altına yazılır.
- **Geri al:** Oyun bazında, işlem bazında veya "Her şeyi geri al".
- **Kaldırıcı:** "BoostRoot'un yaptığı tüm değişiklikleri geri al" kutusu varsayılan işaretli.
- **Geri yükleme noktası:** İlk boost'tan önce bir kez.

### 6.17 Ayarlar
Dil, tema vurgu rengi (mor varsayılan), sesler (aç/kapa), animasyon seviyesi, kısayollar, klip/overlay ayarları, arka planda kapatılacaklar listesi, çevrimdışı mod, güncelleme kanalı (kararlı), "Destek ol" (GitHub Sponsors + kripto adresleri), "Hata bildir", Hakkında ("by bl4z3root", lisanslar, üçüncü taraf bileşenler).

### 6.18 Hata yönetimi
- Yerel günlük: `%LOCALAPPDATA%\BoostRoot\logs`, 7 gün döner.
- "Hata bildir": günlükten kişisel yollar temizlenerek hazırlanmış bir GitHub Issue sayfası açar; kullanıcı görmeden hiçbir şey gönderilmez.
- Çökme: bir sonraki açılışta "BoostRoot beklenmedik şekilde kapandı, rapor göndermek ister misin?" sorusu.
- Her dış bileşen (NVAPI, ADLX, PresentMon, PawnIO, ağ) yoksa veya hata verirse ilgili özellik kibarca devre dışı kalır; uygulama çökmez.

---

## 7. Ekranlar ve görsel kimlik

Onaylanan taslaklar: "BoostRoot – Logo ve Ekran Taslakları" tuvali.
- **Logo:** A1 Roket. Koyu kare zemin üzerinde mor çizgi roket; kelime işareti "Boost**Root**" (Root mor).
- **Renkler:** Zemin #0B0A12, yüzey #14121F, kenarlık #2A2540, vurgu mor #A855F7, açık mor #C084FC, metin #EDEBF5, ikincil metin #A19DB5; durum renkleri yeşil #86EFAC, sarı #FCD34D, kırmızı #F87171.
- **Yazı tipleri:** Space Grotesk (başlıklar), Inter (metin); uygulamaya gömülü.
- **Ana ekran (C):** Sol menü (Boost, PC Karnesi, FPS Testi, Stres Testi, İnternet, Klipler, Araçlar, Ayarlar) + üstte seçili oyun ve dev BOOST düğmesi + seviye seçici + altta oyun kapakları (yüklüler önde, desteklenen ama yüklü olmayanlar soluk).
- **Oyun boost ekranı, PC Karnesi, paylaşım kartı:** Tuvaldeki taslaklar.
- **Stres testi ekranı:** Canlı çizgi grafikler + sonuç karnesi.
- **Her ayarın yanında (?) ipucu.** Animasyonlar "akıcı ama hafif"; pencere küçültülünce veya oyun öndeyken durur.
- Hafif efekt sesleri (boost bitti, klip kaydedildi), kapatılabilir.

---

## 8. Sürümleme ve güncelleme

- **Biçim:** `ANA.YY.AY` + aynı ayda ek sürüm için `.N`. İlk sürüm **1.26.11**. Örnek sonraki: 1.26.11.1, 1.26.12, 1.27.1. Ana sayı yalnızca büyük değişikliklerde artar.
- **Uygulama güncellemesi:** Açılışta (günde en fazla bir kez) GitHub Releases API kontrol edilir; yeni sürümde bildirim → indir → SHA-256 doğrula → kurulumu sessiz çalıştır → yeniden başlat. Elle güncelleme: yeni kurulum dosyasına çift tıklamak yeterlidir (üzerine kurar, ayarları korur).
- **Profil paketi:** Ayrı, imzalı; uygulama güncellemesi gerektirmez.

---

## 9. Performans bütçeleri

| Ölçü | Hedef |
|---|---|
| Soğuk açılış | < 1,0 sn (SSD) |
| Arayüz boşta RAM | < 60 MB |
| Arayüz boşta CPU | < %1 |
| BoostRoot kapalıyken | 0 işlem (otomatik başlatma seçeneği açılmadıkça) |
| Agent (yalnız overlay) | < 30 MB RAM, < %1 CPU |
| Anlık tekrar FPS etkisi | < %3 (donanım kodlayıcıyla) |
| Kurulum boyutu | < 60 MB |

Bu bütçeler, GitHub Actions'ta ölçülebilenler için otomatik kontrol edilir; diğerleri sürüm öncesi elle ölçülür.

---

## 10. Yerelleştirme

Türkçe ana dil, İngilizce tam çeviri. Tüm metinler kaynak dosyalarındadır, profil açıklamaları da TR/EN içerir. Sayı/tarih biçimleri seçilen dile göredir.

---

## 11. Test stratejisi

- **Birim testleri (her derlemede):** Ayar yazıcıları (gidiş-dönüş, biçim koruma, yorum satırlarını bozmama), `neverTouch` reddi (her oyun profili için ayrı test), journal/geri al, profil şema doğrulama, imza doğrulama, sürüm karşılaştırma, puanlama, öneri motoru, oyun bulucu ayrıştırıcıları (örnek VDF/manifest dosyalarıyla).
- **Entegrasyon testleri (Windows sunucusunda):** Kayıt defteri/güç planı uygulama-geri alma, sahte oyun klasörüyle uçtan uca boost, kurulum/kaldırma.
- **Arayüz testleri:** Avalonia headless ile ana akışlar.
- **Gerçek donanım testi:** bl4z3root'un PC'si (AMD RX 580, i5-9400F, Win11 Pro) + en az bir NVIDIA'lı ve bir Windows 10'lu arkadaş bilgisayarı. Her oyun için: boost → oyun içi ayarları kontrol (çözünürlük/tuş değişmedi) → FPS testi → geri al.
- **Hile koruması kontrolü:** Valorant, CS2, PUBG, Fortnite ve Apex'te boost + overlay + klip birlikte kullanılır ve uyarı/atılma olmadığı doğrulanır.

---

## 12. Yayın paketi (her şey hazır, son adım yalnızca "Yayınla")

- GitHub deposu (açık kaynak, GPL-3.0), TR/EN README (ekran görüntüleri, gerçek ölçümler, SSS, "Ban riski var mı?" bölümü)
- GitHub Actions: derleme, test, imzalı profil paketi, kurulum dosyası, SHA-256
- Tanıtım sitesi (GitHub Pages, TR/EN): büyük "İndir" düğmesi, Windows 10/11 bilgisi, özellikler, gerçek FPS sonuçları, gizlilik notu
- GitHub Sponsors sayfası metni + kripto (BTC/USDT) adres alanları (adresleri bl4z3root verecek)
- Sürüm notları şablonu
- Not: Kod imzalama sertifikası olmadığından Windows SmartScreen "Bilinmeyen yayımcı" uyarısı gösterebilir. README ve site, nasıl geçileceğini açıklar. Ücretli sertifika ayrı bir karar konusudur.

---

## 13. Riskler ve bilinmeyenler

| Risk | Önlem |
|---|---|
| Oyun güncellemeleri ayar dosyası yapısını değiştirir | Profil paketi ayrı ve hızlı güncellenir; okunamayan dosyada değişiklik yapılmaz |
| ADLX/NVAPI'nin NativeAOT ile uyumu | Erken teknik deneme; gerekirse bu iki modül ayrı küçük yardımcı exe olur |
| Overlay özel tam ekranda görünmez | Algılanır ve kullanıcıya açıkça söylenir |
| Antivirüs yanlış alarmı (yeni, imzasız exe) | Açık kaynak, NativeAOT, VirusTotal'a gönderim, Microsoft'a yanlış pozitif bildirimi |
| PawnIO/sürücü kurulumunun güvenlik algısı | İsteğe bağlı, açıklamalı, kaldırıcıyla kaldırılır |
| NVIDIA tarafı geliştirici PC'sinde test edilemez | Arkadaş testi şart; NVIDIA modülü "beta" etiketiyle çıkabilir |
| Kapsamın büyüklüğü | Modüler yapı; her modül bağımsız tamamlanıp test edilir |

---

## 14. Onay bekleyen bilgiler (bl4z3root'tan)
- Kripto bağış adresleri (BTC, USDT ağı) ve GitHub Sponsors hesabı
- GitHub kullanıcı adı / depo adı (öneri: `boostroot`)
- NVIDIA ve Windows 10 testine yardım edecek arkadaşlar
