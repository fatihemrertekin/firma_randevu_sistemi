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


## Hafif pastel ve fotoğraf uyumu — 05.10.2026

Kullanıcı mevcut renk ailelerinin tamamını çok hafif pastel hale getirmeyi ve sağdaki krem etkiyi gidermeyi istedi; krem olmayan benzer fotoğraf üretimini ayrıca yetkilendirdi. Kapsam mevcut giriş/MFA/kimlik ve yönetim paletidir; yerleşim/API/oturum/CSRF değişmez. Önceki kabul yukarıda tarihsel kayıt olarak korunur; bu devamın yerel geliştirme kabulü `done`, GitHub/ana yerel teslimi `in_progress`.

Mavi, lacivert, koyu metin, nötr zemin ve hata kırmızısı birlikte yumuşatılır; beyaz alanlar/dolu butonlar ve en az 4.5:1 aktif metin kontrastı korunur. Tek imagegen üretimi `assets/plates/salon-background-neutral.png` kullanılır; doğal yeşil detaylar hafif kalır, sarı/amber/kremsi ışık yoktur. Masaüstü ve mobilde örtü fotoğrafı zeminle birleştirir; profil fotoğrafı mevcut küçük alan/oranda kalır. Yeni ekran, layout, backend, migration, bağımlılık veya T04 yok.

Kabul: typecheck/lint/203 test/build; paketli imajda 16 durum × dört genişlik, gerçek giriş/MFA/profil ve anonim 401; dolu buton, hedef, taşma ve renk kontrastı kontrolleri geçti. Fotoğraftaki koyu bölgeler üzerinde yazı için açık örtü güçlendirildi; masaüstü fotoğraf altı örneklerinde 3:1 büyük/4.5:1 gövde kontrastı geçti. Tek düzeltme ve son onay incelemesi tamamlandı; yeni taslak/native turu yapılmadı. [Ayrıntılı kabul ve teslim](../../docs/plans/P02.md#hafif-pastel-ve-fotoğraf-uyumu--05102026).
