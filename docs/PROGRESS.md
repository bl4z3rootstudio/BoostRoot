# BoostRoot — İlerleme Notu (devam etmek için önce bunu oku)

Son güncelleme: 12.11.2026 gece · sürüm 26.11.1 · testler: 169 (Linux) + Windows entegrasyon testleri (CI)

## Bitti (kod + otomatik testler)
- Plan 01 Temel, 02 Platform/Oyunlar/Boost, 03 Arayüz (ana ekran, "Ne değişecek?" önizleme + ipuçları, PC Karnesi, ilk açılış ekranı, Steam kapak indirme, ilk Boost'ta geri yükleme noktası, gelişmiş Windows ayarları)
- Plan 04 GPU: NVIDIA oyun başına sürücü profili (NVAPI DRS, geri alınabilir); AMD için Adrenalin + otomatik hız aşırtma rehberi (AMD güvenli API sunmadığı için otomatik değil)
- Plan 05 FPS testi: PresentMon (kurulumla gelir / taşınabilirde indirilir), ortalama/%1/%0.1/takılma, geçmiş (jsonl), hikâye + kare paylaşım görseli
- Plan 06 Stres: CPU (deterministik hesap hatası), RAM (desen doğrulama), GPU (OpenGL/ANGLE ağır shader), hepsi birden; canlı grafik; 90°C / hotspot 105°C kesme; ısıl yavaşlama tespiti. Sensörler: NVML (NVIDIA), ADL OD5/6/N/8 (AMD), CPU yük+MHz+throttle
- Plan 07 İnternet + Araçlar
- Plan 08 Oyun oturumu: Alt+O gösterge, Alt+C nişangâh, global kısayollar, 4:3 Stretched (geçici masaüstü modu + oyun anahtarları)
- Plan 09 Klipler: ffmpeg (talep üzerine, SHA-256 doğrulamalı), Desktop Duplication + GPU kodlayıcı, replay (Alt+F10), kayıt (Alt+F9), foto (Alt+F1), WASAPI ses (sistem/mikrofon), galeri, kes, dikey 9:16, Discord webhook (10 MB'a sıkıştırır)
- Plan 10 Profiller: 30 oyun (10'u ayar dosyası düzenler, 20'si sürücü/Windows + rehber); üretici `tools/gen-profiles.py`
- Plan 11 Güncelleyici (GitHub Releases + SHA256SUMS), Inno Setup kurulum, kaldırırken "her şeyi geri al" (`BoostRoot.exe --revert-all`), release workflow (tag `v26.11.1`)
- Plan 12 README TR/EN, `docs/index.html` tanıtım sitesi (GitHub Pages /docs), FUNDING.yml, sürüm notları

## Bilinçli olarak yapılmayanlar / sınırlar
- İşlemci sıcaklığı: kernel sürücüsü (PawnIO/WinRing0) gerektirdiği için okunmuyor (anti-cheat riski). GPU sıcaklığı var.
- AMD sürücü ayarları ve otomatik hız aşırtma otomatik değil, adım adım rehber.
- Profil paketleri ayrı imzalı (Ed25519) güncellenmiyor; uygulama güncellemesiyle geliyor.
- Gösterge/nişangâh exclusive fullscreen'de görünmeyebilir (enjeksiyon yapmadığımız için).
- Kripto adresleri README'de "yakında" — kullanıcıdan alınacak.

## 13.11.2026 gece eklenenler
- Nişangâh Stüdyosu (VALORANT kodu dışa/içe aktarma, CS2/TF2 komutları, BoostRoot overlay), 500+ oyun kütüphanesi (`profiles/library.txt`), tanınmayan her yüklü oyuna genel profil ("dir:" süreç tespiti), ana ekranda arama, RAM boşaltma, oyun süresi, oyun açıkken yüksek öncelik + 0.5 ms zamanlayıcı
- GitHub: https://github.com/bl4z3rootstudio/BoostRoot (public), Pages: https://bl4z3rootstudio.github.io/BoostRoot/
- **v26.11.1 yayınlandı** (Release workflow, manuel tetikleme: Actions → Release → Run workflow → sürüm). Kurulum 15 MB, taşınabilir 19 MB.
- Etiket (tag) push'u oturum proxy'sinde 403 veriyor → sürüm çıkarmak için Actions'tan "Run workflow" kullan.

## Sıradaki
1. Kullanıcı PC testi: `docs/TEST-CHECKLIST.md` (UAC "Evet" kullanıcı tarafından basılmalı; uzaktan tıklanamıyor)
2. Bulunan hatalar → 26.11.2

## Notlar
- .NET 10 SDK: `export PATH=$HOME/.dotnet:$PATH`; Linux'ta Windows projeleri derlenir ama çalışmaz; NativeAOT yalnızca Windows CI'da.
- Avalonia 12: `WindowDecorations`; XAML'de generic tip yok (KindChoice/MinutesChoice).
