---
version: 1
slug: services-tsx
primary_target: src/Web/src/features/services/Services.tsx
related_targets: [src/Web/src/features/services/ServiceRouteEditor.tsx,src/Web/src/features/services/ServiceEditor.tsx,src/Web/src/styles/tokens.css,src/Web/src/styles/global.css]
status: done
---

# T04 — Hizmetler ve minimalist buton önerisi

Mode: operate. Masaüstü/mobil taslak ve ortak minimalist buton önerisi kullanıcı tarafından 05.10.2026'da onaylandı; aynı taslağın ekran koduna uygulanması ayrıca onaylandı. Geliştirme ve yerel kabul tamamlandı; GitHub/ana yerel teslim ayrı izlenir. Kullanıcı ayrıca bazı butonların büyük göründüğünü belirterek daha minimalist tasarım istedi.

- Amaç: MFA Owner hizmetleri ad/süre/TL fiyatı/durumuyla kolayca tarar ve seçtiği hizmeti tek formda düzenler. Ana işlem listede Yeni hizmet, formda Kaydet.
- Bilgi sırası: listede tek Hizmetler başlığı/açıklama → ince satırlı liste → yenile/sayfalama; ayrı ekle/düzenle sayfasında Listeye dön → seçili ad/durum veya Yeni hizmet → alanlar → geri bildirim/kaydet/vazgeç → ayrı durum/silme bölümü. Formun altında liste bulunmaz.
- Mobil: Menü kapalı başlar; liste adı/süre/fiyat/durum ve tek Düzenle bağlantısı taşmadan okunur. Form tek sütun; kısa butonlar yan yana, uzun işlemler kendi satırında içeriğe göre genişler. 320/390 px uygulama kabulü daha sonra yapılır.
- API: mevcut GET/POST `/api/services/`, GET/POST `/api/services/{id}`, POST `/{id}/status` ve `/{id}/delete`; DTO, MFA Owner/CSRF/sürüm/idempotent oluşturma ve hata sözleşmesi korunur. Yeni endpoint, backend veya migration gerekmez.
- Durumlar: yükleme/boş/alan hatası/oturum-yetki reddi/bulunamayan kayıt/sürüm çatışması/istek sınırı/belirsiz sonuç/başarı/bekleyen kayıt/kaydedilmemiş değişiklik. Görseldeki değerler sentetik örnektir; API başarısı değildir.

## Görsel öneri

Mevcut beyaza yakın/mavi/lacivert dünya, metin markası ve grup menüsü korunur; renk kaynağı güncel DESIGN.md ve tokens.css'tir. Önceki krem/turuncu taslaklar renk yetkisi değildir. Liste kart içine ikinci kez başlık/açıklama koymaz; tek sayfa başlığı ve ince satır ayırıcıları yeterlidir. Masaüstünde hizalı ad/süre/fiyat/durum sütunları; mobilde aynı bilgiyi taşıyan satırlar vardır. Satır başına yalnız Düzenle. Aktifleştirme/pasifleştirme ve Sil seçili hizmette, formdan ayrı bölümde bulunur ve mevcut ayrı onayları gerektirir.

Yeni hizmet aynı üç alanı kullanır: Hizmet adı, Süre (dakika), Fiyat (TL). Henüz kaydedilmemiş kayıtta durum/silme işlemleri gösterilmez. Düzenlemede ad tam genişlik, süre/fiyat masaüstünde yan yana, mobilde alt alta; alan etiketleri görünürdür. Ad 1–100 karakter, süre 1–1440 tam dakika, fiyat mevcut 0–999999,99 TL sözleşmesine bağlıdır; sunucu son karardır. Örnek hizmet/fiyatlar gerçek firma verisi değildir.

## Ortak minimalist buton önerisi

Bu ortak stil T04 ile birlikte mevcut frontend'in gerçek buton/ekran bağlantılarında uygulandı; diğer ekranların görev düzeni değiştirilmedi.

- Hedef ölçü: çoğu işlemde 48 px yerine 44 px asgari yükseklik; okunur 14 px/600 metin, yaklaşık 12 px yatay iç boşluk, 6 px köşe. Ölçüler merkezi tokenlarla uygulanıp gerçek tarayıcıda doğrulandı; raster ölçümü kabul kanıtı olarak kullanılmadı.
- Genişlik içeriğe göre; gereksiz sabit minimum ve tam genişlik yardımcı işlem dolguları kaldırılır. 44×44 px tıklama alanı ve ayrı işlem satırları arasındaki mevcut 16 px dikey aralık korunur. Çok satırlı menü etiketi gerektiğinde daha yüksek kalır.
- Ana işlem dolu mavi; yardımcı işlem dolu lacivert; gezinme mevcut açık dolgu; tehlikeli işlem ayrı bölümde dolu kırmızı. Şeffaf yüzey, yeni renk ailesi, gölge, kabartma veya dekoratif ikon önerilmez.
- Aynı ekranda tek ana işlem belirgindir. Listeye dön/yenile/sayfalama ve Vazgeç aynı görsel ağırlıkta ana işlem gibi yarışmaz. Klavye odağı, hover/disabled/loading ve erişilebilir ad korunur.
- Mobilde bütün butonları küçücük veya tam genişlik yapma zorunluluğu yoktur: kısa işlemler sığdığı kadar yan yana; uzun metin taşmadan kırılır, grubun satır aralığı korunur. Gerçek hedef/kontrast/taşma kabulü dört genişlikte yapılacaktır.

## Durum ve etkileşim planı

- İlk yüklemede okunur yükleniyor durumu/skeleton; boş listede Yeni hizmet ana işlemi; hata durumunda tekrar deneme. Sonuç belirsizken başarı yazılmaz.
- 400 alan yanında hata ve ilk hatalı alana odak; 401 güvenli mevcut giriş/dönüş; 403 yetki açıklaması; 404 listede bulunmayan kayda geri dönüş; 409 taslağı koruyan sürüm uyarısı ve açık onayla Güncel kaydı yükle; 429 mevcut bekleme/tekrar dili.
- Başarı yalnız sunucu yanıtından sonra; beklerken çift gönderim ve gezinme engellenir. Kirli formda liste/menü/geçmiş/çıkış koruması sürer. Yenileme/sekme kapatma yalnız tarayıcının desteklediği mevcut uyarıyla korunur.
- Durum ve silme işlemleri alan kaydıyla birleşmez. Form kirli veya istek bekliyorsa durum/silme kapalı kalır; önce kaydetme/vazgeçme açıklanır. Durum onayı sonrası GET ile güncel kayıt/sürüm alınır; eski DTO ile düzenleme sürdürülmez. Silme ad/korunacak geçmişle ayrıca onaylanır; 204 sonrası listeye dönülür, Vazgeç odağı açan butona geri verir.
- Mevcut `/yonetim/hizmetler`, `/yeni`, `/{id}/duzenle` ve `?sayfa=N` yolları korunur. Düzenle/geri/vazgeç dönüşünde liste sayfası korunur; oluşturma/silme sonrasında liste sayfalama kararı mevcut veri sonucuyla doğrulanır.

## Taslak dosyaları ve inceleme

- Masaüstü liste/düzenleme: `.impeccable/mocks/decision/services-t04-desktop.png`.
- Mobil liste/düzenleme: `.impeccable/mocks/decision/services-t04-mobile.png`.
- Her PNG'nin `.png.json` yan kaydında exact prompt, `approved:true` ve üretim kaynağı vardır; aynı exact prompt PNG içine gömülüp geri okunarak doğrulandı. Üretim built-in image_gen ile yapıldı.

Öz eleştiri: listede işlem kalabalığı azalıyor; sütunlar ve mobil satır ayrımı okunur. Form ve durum/silme ayrılmış, ana işlem belirgin, yardımcı butonlar gereksiz genişliğe yayılmıyor. Rasterdaki hafif gölge/ton geçişleri ve dış sunum sınırı üretimde literal uygulanmayacak; kodda düz token renkleri ve semantik alanlar kullanılacak. Raster pikseli 44 px hedef, kontrast veya responsive çalışma kanıtı değildir. Görseller tek önerinin masaüstü/mobil gösterimidir; ek alternatif ve motor bitiş turu yapılmadı.

Onaylanan kapsam uygulandı: typecheck/lint/build ve 230 test geçti; ayrı sentetik gerçek API/Chrome kabulünde 33 durum × 320/390/768/1280 px doğrulandı. Görünür işlem hedefleri ≥44 px, etkin metin kontrastı ≥4.5, ortak buton metni 14 px/600 ve sıfır yatay taşma/sayfa hatası ölçüldü. Liste/form, onay, odak ve kırmızı Sil dolgusu görüntülerden ayrıca incelendi. Kabul paketi ve teslim sınırı docs/plans/P02.md içindedir. Ana 8080 ve DB/migration bu geliştirme kabulünün kapsamı dışında kalır; ana yerel teslim somut kabul paketi ve geri dönüş hazırlığından sonra ayrı onaylanır.
