# Rezervasyon ayarları — P03-05C görsel taslağı

Durum: kullanıcı 15.10.2026'da “onaylıyorum” yanıtıyla gösterilen görsel taslağı onayladı; ekranın yerel uygulama kabulü aşağıdadır, GitHub/ana teslimi henüz tamamlanmadı. P03-05C `in_progress`. Mode: **Operate**. Ürün yetkisi [PRODUCT](../../docs/PRODUCT.md), görsel yetki [DESIGN](../../DESIGN.md) ve güncel `ManagementLayout`/`tokens.css` dosyalarıdır. Mevcut açık palet ve doğrudan sol menü genişletilir; yeni görsel sistem kurulmaz.

## Amaç ve düzen

- İşletme sahibi mevcut üç rezervasyon kuralını görür, değiştirir ve tek işlemle kaydeder. Gösterilen 60 dakika / 30 gün / 15 dakika yalnız başlangıç değerlerini kullanan örnektir; taslak gerçek firma verisini okumaz.
- Bilgi sırası: sayfa başlığı/açıklama → mevcut randevuların korunacağı notu → üç etiketli kural → İstanbul saati → Kaydet ve kaydedilmemiş değişiklik durumu. İlave özet paneli, fotoğraf, istatistik veya iptal/onay ayarı yoktur.
- İçerik tam genişliktedir. Geniş içerikte her satırda solda etiket/açıklama, sağda sayı ve birim vardır; içerik daralınca aynı DOM sırasında alt alta gelir. Mobil menü kapalı başlar. Kontroller ortak 48 px alan/44 px işlem ölçüsünü, 32 px görev başlığını ve tokenları kullanır.
- Onaylı giriş noktası İşletme grubunda Hizmetler'den sonra **Rezervasyon ayarları**; URL `/yonetim/rezervasyon-ayarlari`. Ekran/menü/rota yerelde uygulanmıştır; ana teslim kanıtı P03 planında ayrıca tutulur.
- Başlangıç aralığı yanında yalnız açıklayıcı saat dizisi gösterilir; gerçek uygunluk veya boş slot değildir. Günlük hizalama 00.00'a göredir; 7 gibi bir değerde örnek de bu hizalamayı kullanır. Hizmet süresi yuvarlanmaz.

## API bağlantısı ve davranış sözleşmesi

| Görünür işlem / alan | Mevcut sözleşme ve planlanan ekran davranışı |
| --- | --- |
| İlk yükleme | `GET /api/booking-policy/`; MFA Owner. Açık DTO `version, leadMinutes, horizonDays, slotMinutes, timeZone`; başarılı ve geçerli yanıt gelmeden düzenlenebilir varsayılan değer gösterilmez. Tek kurulum kaydı beklenir; boş/geçersiz yanıt hata durumudur, oluşturma düğmesi yoktur. |
| Ön süre | `leadMinutes`, gerekli tam sayı 0–43200 dakika. Ufuk dakikasından kısa olmalıdır. Etiket ve alan yanı hata açıkça bağlıdır. |
| İleri rezervasyon | `horizonDays`, gerekli tam sayı 1–365 gün. |
| Başlangıç aralığı | `slotMinutes`, gerekli tam sayı 1–60 dakika. Yalnız 15/30/60 seçimleriyle API daraltılmaz. |
| Ayarları kaydet | Aynı origin/cookie/CSRF ile `POST /api/booking-policy/`; üç alanın tamamı ve son doğrulanan `version`. Değişiklik yoksa düğme kapalıdır. Bekleyen istekte alanlar ve gezinme kilitlenir; ikinci gönderim olmaz. Başarı yalnız geçerli sunucu yanıtından sonra görünür; yanıt yeni temel değer/sürüm olur. |
| Güncel ayarları yükle | Aynı GET. Kirli taslak silinmeden önce mevcut kaydedilmemiş değişiklik koruması kullanılır. Belirsiz POST sonucunda otomatik yeniden POST yapılmaz; önce sunucu okunur. GET de başarısızsa taslak korunur ve kaydetme kapalı kalır. |

## Durumlar

- **Yükleniyor:** metinli durum, düzenleme alanları henüz yok; boş değerler gerçek ayar gibi gösterilmez.
- **Yükleme hatası / ayar alınamadı:** açık hata ve Yeniden dene; kurulum kaydı otomatik yaratılmaz. Eski bir yükleme mevcutsa bunun önceki veri olduğu belirtilir.
- **Düzenleme / kirli:** tek Kaydet düğmesi, satır içi değişiklik bilgisi. Bölüm/URL/yenileme/çıkışta mevcut taslak korumasına katılır. Özel veri veya sürüm kalıcı tarayıcı depolamasına yazılmaz.
- **Kaydediliyor:** düğme metni ve `aria-busy`, alan ve gezinme kilidi; başarı varsayılmaz. Yanıt gövdesi okunana kadar bekleyen işlem bitmiş sayılmaz.
- **400 alan hatası:** sunucu ProblemDetails alanları ilgili etiketli alana bağlanır, ilk hataya odak taşınır; taslak korunur. CSRF 400 ayrı genel hata ve güvenli oturum/yeniden yükleme toparlamasıdır; sayı hatası gibi gösterilmez.
- **409:** Ayarlar başka bir işlemde değişti; taslak korunur. Güncel ayarları yükle, silme onayı ve ardından güncel sürümle yeniden düzenleme; kör üzerine yazma yoktur.
- **Ağ / timeout / bozuk POST yanıtı:** kayıt sonucu belirsiz olabilir. Kaydet kapalı, güncel GET ile doğrulama; doğrulanmamış taslak başarı olarak sunulmaz.
- **429:** taslak korunur; varsa sunucu bekleme bilgisi gözetilir, uydurma geri sayım yoktur. Önce güncel durum okunur; otomatik gönderim döngüsü yoktur.
- **401:** mevcut oturum sona erme/MFA akışı; **403:** işletme sahibi yetkisi yok. Başarılı işlem/varsayılan form gösterilmez. Taslak prototip bu mesajları gösterir, gerçek giriş yönlendirmesi yapmaz.
- **Başarı:** yeni sunucu değerleriyle temiz form ve metinli bildirim; mevcut randevuların korunduğu açıklama aynı kalır.

## Kanıt ve onay sınırı

Etkileşimli örnek [index.html](../mocks/booking-policy/index.html) yalnız tarayıcı içi taslaktır. Üstteki durum seçicisi inceleme aracıdır; ürün ekranına dahil değildir. Örnek hesap, değerler, hata ve başarı mesajları simülasyondur; API isteği, gerçek kayıt veya kimlik doğrulaması yoktur.

Görsel kontrol (15.10.2026): 320/390/768/1280 px ve her genişlikte 13 durum geçti; taşma, etiketler, alanların sağ kenar hizası, değişiklik yokken kapalı Kaydet, kayıt simülasyonu, ufuk/ön süre alan hatası, bekleyen istekte kilit ve mobil Menü/Escape/odak dönüşü doğrulandı. Dokuz görüntü ilk turda tasarım sistemiyle incelendi; tek toplu düzeltmede alan/birim hizası ve yeni düzenlemede eski başarı mesajının temizlenmesi düzeltildi. İkinci tur aynı matrisi geçti, dört normal genişlik ve mobil alan hatası görüntüsü yeniden incelendi. Başlık/etiket/hata metni sarılıyor; 320 px'te sayfa uzuyor, yatay taşma veya kesilme yok. Hata yalnız renkle değil metinle açıklanıyor; masaüstünde tam genişlik ve mevcut menü hiyerarşisi korunuyor. Script hatası ve dış/API isteği sıfır; [rapor](../mocks/booking-policy/verification.json), [yerel kontrol](../mocks/booking-policy/verify.cjs). Bu kontrol gerçek API, WCAG sertifikası veya native Impeccable finish kabulü değildir. Ek agent çalıştırılmadı.

Impeccable context motoru kökte PRODUCT.md aradığı için mevcut `docs/PRODUCT.md` dosyasını bulamadığını bildirdi. Kök AGENTS.md'nin belge haritası uyarınca mevcut ürün belgesi doğrudan okundu; ikinci PRODUCT veya bağlam onarım işi oluşturulmadı.

## Onay sonrası yerel uygulama kabulü

Mevcut GET/POST ve ortak CSRF, ErrorMessage, taslak/URL koruması kullanıldı; backend, şema, bağımlılık veya ortak görsel token değişmedi. Typecheck/lint, son kaynakta 312 web testi ve üretim derlemesi geçti. İlk sandbox test çalıştırıcısının geçici dosya erişim hatası kabul sayılmadı; izinli ortamda ilgili 83 test, ardından ilk kaynakta 311 test başarıyla çalıştı. Son incelemede `007` girdisi için kayıt doğrulamasıyla saat örneğinin farklı sözdizimi kabul ettiği bulundu; önce başarısız regresyonla üretildi, örnek aynı beş basamak sınırına alındı. Son 312 test ve taze sentetik imajda 32 görüntülü gerçek API akışı `007` dahil geçti; önceki kaynak sonuçları son kaynak kabulü yerine kullanılmadı.

Son non-root imaj ayrı sentetik PostgreSQL/MFA Owner kurulumunda gerçek Chrome/API ile doğrulandı: kayıt/yenileme, 7 dakikalık örnek, tek POST, alan hatası/odak, kirli gezinme reddi, gerçek 409 ve yükleme onayı, gerçek CSRF 400, sunucuda commit sonrası kesilen yanıtın belirsiz sonucu ve GET ile toparlanması, Staff GET/POST 403 ve menü yokluğu, anonim 401/giriş dönüşü, gerçek 429. Sekiz durum × dört genişlikte 32 görüntü, yatay taşma ve script hatası sıfır; beş asset HTTP hash'i derlemeyle eşit. Dört normal genişlik ve temsilci alan/çatışma/belirsiz/başarı görüntüleri onaylı taslak ve DESIGN ile incelendi: ana hiyerarşi, tam genişlik, birim hizası, mobil sarma ve görünür alan odağı korunuyor. Genel hata ve başarı mevcut ortak bileşenlerin görünümünü kullanır; yeni tasarım varyasyonu eklenmedi.

Kanıt `.local/p03-05c/` altındadır. Gerçek ana Owner girişi/POST veya SMTP kullanılmadı; ana teslim henüz bu kabulün parçası değildir. Müşteri iptal/onay ayarları ve native Impeccable finish veya tam WCAG sertifikası uygulanmış sayılmaz. GitHub/ana teslimden sonra P03-06 yalnız sonraki adaydır; başlamaz.
