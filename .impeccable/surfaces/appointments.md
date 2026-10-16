# Günlük randevu yönetimi

Mod: `Operate`. Hedef: `src/Web/src/features/appointments/Appointments.tsx`, `/yonetim/randevular`.

## Onay ve görsel sözleşme

16.10.2026 işlevsel plan ve çalışan gruplaması onaylandı; ilk raster fazla basit bulundu. [Revize görsel](../mocks/decision/p04-01-staff-day-refined.png) “Bu görsel yönde uygula” yanıtıyla onaylandı; exact prompt ve onay yan JSON'dadır. Yeni randevu, ayrıntı ve taşımanın [HTML taslakları](../mocks/decision/p04-forms.html) ayrıca gösterildi, “Üç formu da bu düzenle uygula” yanıtıyla onaylandı. Plan ve bu dört yüzey tekrar onay beklemez.

Otorite `docs/PRODUCT.md`, `DESIGN.md`, mevcut `ManagementLayout`, ortak kontroller ve `tokens.css`'tir. Unna marka ve sistem sans korunur; raster ölçüm aracının önerdiği başka başlık fontu kurulmaz. Metin, kontrol, zaman hattı ve ikonlar semantiktir; UI bitmap olarak gömülmez. Fotoğraf/avatar, yeni palet veya ortak tasarım yenilemesi yoktur.

İlk görüntü: mevcut menü/başlık; Randevular ve tek ana Yeni randevu işlemi; gün/çalışan araçları; çalışan adları altında kronolojik kayıtlar. Açık mavi çalışan başlıkları, tabular başlangıç/bitiş saatleri ve ince zaman hattı gruplamayı okunur kılar. Bölüm yüksekliği içeriğe göre biter. Durum yazıyla verilir; yalnız iptal onayı tehlikeli işlem rengidir. Rasterın yaklaşık tonu/ölçüsü ve örnek notu ürün verisi değildir; kod tokenları ve gerçek API verisini kullanır.

Genişte iki çalışan sütunu; dar içerikte aynı DOM sırasıyla tek akış ve çalışan filtresi. Çalışanlar alfabetik/kimlikle kararlı sıralanır. Sayfalı günün bölümleri “Bu sayfadaki randevular” olarak etiketlenir; tam gün yüklenmiş iddiası yoktur. Katalog ilk sayfadan ibaret sayılmaz; Diğer çalışanları/hizmetleri yükle gerçek sayfalı GET'e bağlıdır. Pasif/silinmiş tanım geçmişi gizlemez.

## Veri ve işlemler

| İşlem | Sözleşme |
| --- | --- |
| Gün/çalışan/sayfa | `GET /api/appointments/?date=yyyy-MM-dd&staffMemberId=...&page=...&pageSize=...`, MFA Owner, İstanbul günü ve telefon içermeyen DTO. Filtre/sayfa URL'dedir. |
| Ayrıntı | `GET /api/appointments/{id}`; telefon yalnız yetkili ayrıntıda. Hizmet/fiyat/süre/çalışan snapshot'ı. |
| Yeni kayıt | Sayfalı hizmet/çalışan GET, `/availability` ile gerçek saat/hizmet sürümü; `POST /api/appointments/` ile Confirmed kayıt. İstemciden fiyat/süre gitmez. |
| Taşıma | `/move-availability` ile sürüm/hedef; `/{id}/move` ile sürüm/işlem anahtarı. Kaydedilene kadar mevcut randevu özeti kalır. |
| Durum | Pending son anından önce Onayla/İptal; başlamamış Confirmed Taşı/İptal; başlamış Confirmed Tamamlandı/Gelmedi. Her POST ayrı satır içi onay, sürüm ve işlem anahtarı taşır; sunucu son karardır. |

Yeni kayıt/taşıma formunda solda etiketli alanlar, sağda seçili verinin özeti; dar alanda tek sütun. Uygun saatler klavyeyle seçilebilen radio grubudur. Hizmet/çalışan/tarih değişiminde eski slot bırakılır. İletişim alanları yalnız yeni kayıtta görünür; müşteri arama/birleştirme yoktur.

## Durum ve toparlanma

- Yükleme/boş/filtre sonucu/hata/yetkisizlik/başarı ayrı; GET hatası boş gün değildir. Önceki istek iptal edilir, geç cevap ve geri/ileri gezinme eski veriyi yeni filtre altında göstermez.
- POST ve gövde okunana kadar ikinci gönderim/ayrılma kilitlidir. Kirli form mevcut gezinme/yenileme/çıkış korumasındadır; kişi/işlem verisi kalıcı depolama veya URL'ye yazılmaz.
- 400 alan hatası alan yanında/ilk alanda odak; CSRF 400 genel hata. 401 mevcut giriş/MFA dönüşü, 403 yetki, 404 güncel yükleme/liste dönüşü. Gerçek Retry-After varsa gözetilir; otomatik yazma döngüsü yoktur.
- 409 taslağı korur, eski slotu bırakır; açık güncel bilgi/saat yüklemesi gerekir. Başarısız taşıma eski randevuyu değiştirmez.
- Ağ/5xx/bozuk başarıda sonuç belirsizdir: alanlar kilitli, taslak ve aynı gövde/anahtar bellekte; açık tekrar aynı istektir. Başarı yalnız geçerli sunucu yanıtından sonra; idempotent tekrarın güncel durumu esas alınır.

## Kabul sınırı

Hafta/ay, sürükle-bırak, anonim rezervasyon/OTP/SMS/token, tahsilat, müşteri geçmişi ve Staff yönetimi yoktur. Gerçek API/tarayıcı, GitHub ve ana 8080 kabulü [P04 planında](../../docs/plans/P04.md) ayrı kaydedilir. Taslak/değerlendirme üretim veya WCAG sertifikası değildir. Yerel kanıt `.local/p04-02/` altındadır. Kapatılmamış native Impeccable araç kapısı tamamlandı diye raporlanmaz.

## Uygunluk nedeni — mevcut yüzey iyileştirmesi

16.10.2026 kullanıcı isteği: saat bulunamadığında gerçek neden saat bölümünde gösterilir. Yeni bir ekran veya tasarım yönü değildir. `availability` ve `move-availability` GET'lerinin `unavailableReasons` alanı sunucu tarafından hesaplanır; tek veya çoklu nedenler mevcut tokenlardan açık mavi, ince çerçeveli bilgi alanında, “Bu seçimle uygun saat bulunamıyor” başlığı altında görünür. Hata/400 tarih bilgisi “Uygun saatler alınamadı” durumuyla ayrıdır. Seçim değişince eski neden kaldırılır; saat bulununca panel kalkar. POST/ayar veya randevu kendiliğinden değiştirilmez.


U01 (16.10.2026): mevcut onay/iptal/tamamlandı/gelmedi alanı işlem sonucunu ve zaman korumasını açıklar; düğmeler gerçek işlemi adlandırır. Belirsiz kayıt, taşıma ve durum metinleri mevcut güvenli tekrar düğmesini anlatır; idempotency ve izin verilen geçişler değişmez. Yeni ekran/yerleşim/API yoktur. Kabul ve teslim P04 U01 kaydındadır.
