# BoostRoot — İlerleme Notu (devam etmek için önce bunu oku)

Son güncelleme: 12.11.2026 gece

## Bitti
- **Tasarım** onaylı: `docs/superpowers/specs/2026-11-12-boostroot-design.md` (sürüm 26.11.1, 4:3 Stretched dahil)
- **Yol haritası:** `docs/superpowers/plans/2026-11-12-00-roadmap.md`
- **Plan 01 Temel** ✅ — sürüm (YY.AY.N), IFileSystem/TextFile (BOM+CRLF korur), ayar biçimleri (ini, keyvalue, keyvalues, json, xml), NeverTouchGuard (+4:3 istisnası), Journal + SafeFileEditor, profil şeması/yükleyici/doğrulayıcı, PathResolver + ProfileApplier, PcScorer + Recommender, CI, NativeAOT sondası (uyarısız)
- **Plan 02 Platform/Oyunlar/Boost** ✅ — IRegistry/IProcessManager/IPowerPlans, RegistryChanger + PowerPlanChanger + UndoService, WindowsTweaks kataloğu (güvenli + gelişmiş; ağ/DNS/Defender/Update'e asla dokunmaz), BoostOrchestrator (oyun açıkken reddeder, kritik hata = tam geri alma, korunan süreçler asla kapatılmaz), Steam/Epic/Riot/Xbox/Minecraft/Roblox bulucuları, GameCatalog, LaunchCommands, Platform.Windows (kayıt defteri, işlemler + askıya alma, güç planı, geri yükleme noktası)
- **Plan 03 Arayüz — kabuk** ✅ — Avalonia 12.1.6 uygulaması (`src/BoostRoot.App`, exe adı BoostRoot), A1 roket ikonu, tema, kendi başlık çubuğu (WindowDecorations=BorderOnly), sol menü, Ana ekran C (seçili oyun + BOOST + 4 seviye + kapak ızgarası), Ayarlar, TR/EN (`Loc`), WindowsAppServices bileşim kökü, ViewModel testleri
- Testler: 102 (Linux'ta), + Windows entegrasyon testleri (CI windows-latest)

## Sıradaki (sırayla)
1. Plan 03 kalan: HardwareScanner (Platform.Windows) + HardwareTables + HealthChecks (Core) → PC Karnesi sayfası + ilk açılış sihirbazı; oyun detay ekranı (seviye kartları, adım listesi, (?) ipuçları); kapak indirme (Steam CDN header.jpg, çevrimdışı modda kapalı); geri yükleme noktası ilk boost'ta.
2. Plan 10 öne alınabilir: gerçek oyun profilleri (şu an yalnızca `_example.json` var, uygulamada oyun listesi BOŞ görünür). Valorant için ayar dosyası anahtarları kullanıcının kendi dosyalarından doğrulanmalı (`%LOCALAPPDATA%\VALORANT\Saved\Config\<id>\Windows\RiotUserSettings.ini` ve `WindowsClient\GameUserSettings.ini`).
3. Plan 04 GPU (NVAPI/ADLX), 05 FPS testi, 06 stres, 07 internet+araçlar, 08 Agent/overlay/4:3, 09 klip, 11 güncelleyici+kurulum, 12 yayın.

## Notlar
- Bu ortamda .NET 10 SDK: `export PATH=$HOME/.dotnet:$PATH`; Linux'ta Windows hedefli projeler derlenir (EnableWindowsTargeting) ama çalıştırılamaz.
- Avalonia 12: `ExtendClientAreaChromeHints` yok → `WindowDecorations`.
- Kullanıcının PC'sinden dosya okuma (device_stage_files) bu oturumda HTTP 401 verdi; klasör izni isteği otomatik reddedildi. Gerçek testler kullanıcı başındayken yapılacak.
- Kullanıcı tercihleri: Türkçe adım adım anlatım, ücretli şey önermeden önce sor, bütçe 0 TL, kullanım limitini az harca.
