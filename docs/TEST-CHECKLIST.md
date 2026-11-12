# BoostRoot — PC test listesi (kullanıcı yapacak)

Sağ tık → Yönetici olarak çalıştır gerekmez; uygulama kendisi yönetici izni ister.

## 1. Açılış
- [ ] Hoş geldin ekranı çıktı, "Başlayalım" sonrası bir daha çıkmıyor
- [ ] Sol menü: Boost, PC Karnesi, FPS Testi, Stres Testi, İnternet, Klipler, Araçlar, Ayarlar
- [ ] Oyunlar listesinde yüklü oyunlar görünüyor (VALORANT, Minecraft, Steam/Epic oyunları); kapaklar birkaç saniye sonra geliyor

## 2. Boost (önce VALORANT kapalıyken)
- [ ] VALORANT seç → "Ne değişecek?" açılıyor, liste mantıklı
- [ ] Rekabetçi → BOOST → "Oyunu başlatayım mı?" çıktı
- [ ] İlk Boost'ta geri yükleme noktası oluştu (Başlat → "Geri yükleme noktası oluştur" → Sistem Geri Yükleme listesinde "BoostRoot")
- [ ] Oyunda: çözünürlük 1440x1080, tuşlar, sens, nişangâh AYNI
- [ ] "Geri al" → ayarlar eski haline döndü

## 3. Oyun içi (oyun açıkken)
- [ ] Alt+O gösterge (FPS, CPU, GPU, sıcaklık) — çerçevesiz tam ekranda
- [ ] Alt+C nişangâh ortada
- [ ] Klipler → ffmpeg indir → oyunda Alt+F10 (son 30 sn), Alt+F9 kayıt başlat/durdur, Alt+F1 foto; ekranda bildirim
- [ ] Klipler sayfasında görünüyor, oynatma/kesme/dikey çalışıyor (Discord webhook istersen)
- [ ] Oyun kapanınca gösterge/nişangâh kendiliğinden kapandı

## 4. Diğer ekranlar
- [ ] PC Karnesi: işlemci i5-9400F, RX 580, RAM, monitör Hz, uyarılar doğru
- [ ] FPS Testi: PresentMon indir (taşınabilir sürümde) → önce/sonra ölç → paylaşım görseli Resimler\BoostRoot'a kaydoldu
- [ ] Stres Testi: İşlemci 5 dk, Ekran kartı 5 dk (ayrı pencere açılır, Esc kapatır), RAM 5 dk — sıcaklık grafiği doluyor
- [ ] İnternet: ping tablosu + hız testi
- [ ] Araçlar: çöp tarama/temizleme, başlangıç uygulamaları aç/kapa, AMD rehberi, gelişmiş ayarlar aç/kapa
- [ ] Ayarlar: dil TR/EN değişiyor, gösterge metrikleri, nişangâh rengi/boyutu

## 5. 4:3 Stretched (isteğe bağlı)
- [ ] VALORANT seç → "4:3 Stretched" işaretle → Boost → oyunu aç → ekran dolu 4:3; oyun kapanınca masaüstü eski çözünürlük

Bir şey çalışmazsa: ekran görüntüsü + `%LOCALAPPDATA%\BoostRoot` ve `%PROGRAMDATA%\BoostRoot\journal` klasörlerini gönder.
