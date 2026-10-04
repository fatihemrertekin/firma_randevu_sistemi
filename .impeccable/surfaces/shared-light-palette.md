# Ortak açık palet — 05.10.2026

Durum: kullanıcı palet/kapsam onayı, yerel geliştirme ve GitHub/ana yerel teslim `done`. [Teslim kanıtı](../../docs/plans/P02.md#ortak-açık-palet-bakımı--05102026).

Amaç: krem zemini beyaza yaklaştırmak ve butonları belirgin dolguyla ayırmak.
Kapsam: giriş, MFA, parola yenileme/değiştirme, davet/e-posta işlemleri ve mevcut yönetim ekranlarının tamamı.
Ana işlem: dolu mavi/beyaz; yardımcı işlem: dolu lacivert/beyaz; gezinme/metin eylemi: hafif dolgu/lacivert; tehlikeli işlem: kırmızı/beyaz.
Mobil: mevcut düzen ve 44 px hedefler korunur; 320/390/768/1280 px kontrol edilir.
API: mevcut DTO/yetki/CSRF/oturum/başarı/hata/yükleme durumları korunur; yalnız CSS renk rolleri değişir.

Yeni ekran, backend, migration, bağımlılık, görsel, T04 veya dark mode yok. Yerleşim için mevcut onaylı taslaklar; renk için güncel [DESIGN](../../DESIGN.md) ve [tokens.css](../../src/Web/src/styles/tokens.css) geçerlidir. Giriş fotoğrafı yeniden üretilmez.

Kabul: typecheck/lint/203 test/build; paketli imajda 16 durum × dört genişlik, dolu buton ve en az 4.5:1 aktif yazı kontrastı/44 px hedef/taşmasız düzen. Gerçek giriş, MFA kurulumu/ikinci adım, profil kaydı ve anonim 401 doğrulandı.

Görsel değerlendirme: beyaza yakın zemin ve beyaz alan ayrımı belirgin; mavi ana işlem, lacivert yardımcı işlem ve kırmızı tehlike yüzeyleri okunabilir. Giriş/mobil MFA, masaüstü profil, 768 px saatler ve mobil hizmet onayı mevcut yerleşimle uyumlu. Fotoğrafın sıcak tonu korunur; yeni raster/native turu yapılmaz. Bu bakım tüm kimlik işlemlerinin uçtan uca tekrarı ya da tam WCAG/native bitiş kabulü değildir.
