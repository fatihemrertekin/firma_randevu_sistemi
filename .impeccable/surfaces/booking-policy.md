# Rezervasyon ayarları — P03-05C görsel taslağı

Durum: geliştirme/GitHub/ana 8080 teslimi `done` (16.10.2026); [teslim kanıtı](../../docs/plans/P03.md#p03-05c-github-ve-ana-yerel-teslim--16102026). Kullanıcı 15.10.2026'da “onaylıyorum” yanıtıyla gösterilen görsel taslağı onayladı. Mode: **Operate**. Ürün yetkisi [PRODUCT](../../docs/PRODUCT.md), görsel yetki [DESIGN](../../DESIGN.md) ve güncel `ManagementLayout`/`tokens.css` dosyalarıdır. Mevcut açık palet ve doğrudan sol menü genişletilir; yeni görsel sistem kurulmaz.

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

Kanıt `.local/p03-05c/` altındadır. Son head/main CI ve şifreli yedekli ana teslim geçti; 46 tablonun bütün verileri, hesap/MFA/anahtar/ayar/SMTP korundu. Ana anonim dört genişlik, 16 API 401, yeni URL'nin giriş dönüşü, beş asset hash'i ve sağlık/restart başarılı. Gerçek ana Owner girişi/POST veya SMTP kullanılmadı. Müşteri iptal/onay ayarları ve native Impeccable finish veya tam WCAG sertifikası uygulanmış sayılmaz. Sonraki tek aday P03-06; planlı ve başlamadı.

## P04-07A — İptal süresi ve onay tercihi genişletme brief'i — 17.10.2026

Durum: **geliştirme/GitHub/ana 8080 teslimi done**. Kullanıcı 17.10.2026'da “Taslağı onaylıyorum. P04-07A’yı bu düzenle uygula; iptal süresi ve onay tercihini API’ye bağlayıp testleri ve ana 8080 teslimini tamamla.” dedi. Mode: **Operate**. Hedef mevcut `/yonetim/rezervasyon-ayarlari`, `src/Web/src/features/booking-policy/BookingPolicy.tsx`; yeni sayfa/menü yoktur. Ürün/sayısal öneri kaynağı [P04-07 planı](../../docs/plans/P04.md#p04-07--politika-kaydı-ve-telefon-doğrulama-uygulama-planı--17102026).

### Altı satırlık ekran özeti

1. Amaç: MFA Owner yeni çevrim içi rezervasyonlar için müşterinin iptal sınırını ve işletme onayı gerekip gerekmediğini birlikte belirler.
2. Ana işlem: mevcut **Ayarları kaydet**; beş alan tek sürümle, tek POST'ta kaydolur. Her seçimde otomatik kayıt veya ayrı yeni düğme yoktur.
3. Bilgi sırası: başlık/kapsam açıklaması → mevcut üç zaman kuralı → iptal süresi → onay tercihi ve sonucu → İstanbul saati/tek Kaydet/kirli durum.
4. Mobil: mevcut kapalı Menü, aynı okuma/DOM sırası; her satır etiket → yardım → alan/birim şeklinde alt alta; onay seçenekleri dikey ve metinler doğal sarılır.
5. API: mevcut `GET/POST /api/booking-policy/` genişletilecektir; `customerCancellationLeadMinutes`, `requiresBusinessApproval` henüz yoktur. Kamu katalog politika DTO'su aynı backend diliminde genişler; ekran OTP/SMS isteği yapmaz.
6. Durumlar: yükleme, alınamayan/eksik yanıt, alan hatası, kirli taslak, kayıt bekleme, başarı, yetkisizlik, 409, 429 ve belirsiz sonuç mevcut davranışı sürdürür; yeni alanlar da kilit/odak/toparlanma sözleşmesine katılır.

### Yerleşim, içerik ve örnek değerler

Mevcut açık palet, Unna marka, sistem sans, tam genişlikli beyaz form, ince satır ayırıcıları ve ortak token ölçüleri korunur. Geniş içerikte solda etiket/yardım, sağda alan/birim vardır; mevcut 40 rem içerik eşiğinde tek sütuna iner. İptal süresi dördüncü, onay tercihi beşinci satırdır. Renkli yeni kart, ek özet, istatistik, fotoğraf veya OTP ayarları eklenmez. Normal açılışta input otomatik odaklanmaz.

Önerilen sayfa açıklaması: **Randevu zamanlarını ve çevrim içi rezervasyon koşullarını belirleyin.** Formun kapsam notu: **Zaman ayarları yeni randevularda ve taşımada uygulanır. İptal ve onay tercihi yeni çevrim içi rezervasyonlar içindir. Mevcut randevuların koşulları korunur.** Böylece mevcut üç alanın taşıma etkisi ile yeni müşteri koşulları birbirine karışmaz.

| Alan | Metin ve etkileşim |
| --- | --- |
| İptal süresi | **Müşteri ne zamana kadar iptal edebilir?** Sayı alanı + **dakika** birimi. Yardım: **Müşteri, bağlantısından randevuya bu kadar süre kalana kadar iptal edebilir.** Örnekte 120; altında **120 dakika = 2 saat. Sınırdan sonra işletmeyle iletişime geçilir.** |
| Değer/sınır | Önerilen 0–43200 tam dakika, gerekli alan. 0 için yardım **Müşteri randevu başlamadan iptal edebilir.** olur; başladıktan sonra iptal yetkisi vermez. Saat/dakika açıklaması geçerli taslaktan hesaplanır; geçersiz girdiden sahte örnek üretilmez. Hata: **İptal süresi 0–43200 arasında tam dakika olmalı.** Aralık henüz ürün sahibi onaylı uygulama kuralı değildir. |
| Onay grubu | `fieldset/legend`: **Çevrim içi rezervasyon onayı**; yardım **Telefon doğrulaması ile işletme onayı farklı adımlardır.** İki açık radio seçeneği önerilir; küçük doğal radio, en az 44×44 hedef sağlayan tam etiket, klavyede ok tuşları/Tab. Switch veya seçimi gizleyen açılır kutu kullanılmaz. |
| İşletme onayı gerekir | Örnekte seçili (`requiresBusinessApproval: true`). Yardım: **Telefon doğrulamasından sonra talep onayınızı bekler. 30 dakika içinde onaylanmazsa veya randevu başlarsa iptal edilir; saat serbest kalır.** Bu seçeneğin değiştirilmesi mevcut Pending randevuyu onaylamaz. |
| Telefon doğrulamasından sonra otomatik onay | Örnekte seçili değil (`false`). Yardım: **Talep, uygunluk yeniden kontrol edilerek onaylanır.** OTP veya uygunluk atlanmaz. Seçimi değiştirmek yalnız taslağı kirletir; başarı göstermez. |
| Yakın randevu notu | Yeni alanların altında tek sakin açıklama: **Yakın bir randevu, müşteri iptal sınırının dışında kalabilir. Koşul müşteriye kayıt öncesinde gösterilir.** Ön süreyi iptal süresine zorlayan yeni bir doğrulama eklenmez. |

60 dakika / 30 gün / 15 dakika / 120 dakika / işletme onayı yalnız sentetik başlangıç örnekleridir. Başlangıç 120/true mevcut ürün kararına dayanır; gerçek form değerleri yalnız sunucudan gelir. İki seçenekli sunum için yapılandırılmış tercih sorusu iletildi; yanıt gelmediğinden okunabilir sonuç açıklamalı öneriyle taslak hazırlandı. Bu varsayım kullanıcının görsel onayı sayılmaz.

### API, durum ve kabul sözleşmesi

- GET'in genişletilmiş açık DTO'su `version, leadMinutes, horizonDays, slotMinutes, timeZone, customerCancellationLeadMinutes, requiresBusinessApproval`; POST beş ayar ve güncel `version` taşır. Mevcut endpoint bugün yeni iki alanı sunmaz; eski yanıtı 120/true ekleyerek geçerli saymak yasaktır. Eksik/yok/yanlış türde yeni alan yükleme hatasıdır; düzenleme/kaydet kapalı, Yeniden dene görünür. Boolean false geçerli seçimdir, eksik değerle karışmaz.
- MFA Owner/CSRF/aynı origin/no-store/15 sn timeout korunur. Backend fiyat/uygunluk/politikada son karardır. Staff veya anonim erişim 403/401; form başarı veya varsayılan göstermeden mevcut oturum toparlamasını izler. Backend migration/DTO/audit/katalog değişikliği ile form aynı uygulama diliminde teslim edilir.
- Bütün alanlar aynı dirty kontrolüne katılır; değer eski haline dönünce temiz sayılır. Tek Kaydet değişiklik yokken kapalıdır; POST/yanıt gövdesi bitene kadar iki yeni alan/radio dahil kilit ve gezinme/çift gönderim koruması sürer. Başarı yalnız doğrulanmış sunucu yanıtıyla, yeni temel değer/sürüm ve mevcut metin bildirimiyle gelir.
- 400 iptal alan hatası görünür etiket/yardım/hata bağıyla ilgili inputa; boolean hatası radio grubuna ve ilk seçeneğe odakla bağlanır. CSRF 400 genel hatadır. 409 taslağı korur, güncel GET öncesi silme onayı ister. 429 gerçek bekleme başlığını gözetir; POST otomatik tekrarlanmaz. Ağ/timeout/bozuk kayıt yanıtında sonuç belirsizdir; önce mevcut GET ile doğrulanır. Yeni alanlar URL/localStorage/log'a yazılmaz.
- İlk yüklemede düzenlenebilir sahte değer yoktur. Tek politika kaydı alınamaması boş form/oluşturma başarısı sayılmaz. Kirli gezinme/yenileme/çıkış koruması ve mevcut reload düğmesinin yalnız hata/toparlanma durumunda görünmesi korunur.
- Uygulama kabulü: iki alan sınırı/eksiklik/false, 120/true ilk migration, mevcut üç alan regresyonu, aynı değer/no-op, yetki/CSRF/409/429/audit/katalog; gerçek PostgreSQL ve sentetik HTTP. Web typecheck/lint/test/build, 320/390/768/1280 gerçek tarayıcı görüntüleri/klavye/alan odakları/çift gönderim/belirsiz sonuç; etiket ve hedef boyutları. Şifreli yedek/önceki veri karşılaştırması, head/main CI ve ana 8080 anonim kabulü ayrı uygulama tesliminde zorunludur. Müşteri yazımı/snapshot/OTP/iptal bağlantısı bu iki ayar alanıyla uygulanmış sayılmaz.

### Taslak dosyaları ve görsel değerlendirme

- [Masaüstü önerisi](../mocks/booking-policy-extension/desktop.png): mevcut formun geniş satır düzeni ve tek Kaydet korunur; dördüncü satırdaki süre, beşinci satırdaki açık seçeneklerle tamamlanır.
- [Mobil önerisi](../mocks/booking-policy-extension/mobile.png): **aynı uzun sayfanın başlangıcı ve aşağı kaydırılmış devamı yan yana**; iki alternatif, iki sütunlu mobil ürün veya iki ayrı form değildir. Başlangıç/Devamı ve TASLAK işaretleri inceleme açıklamasıdır, ürün ekranına taşınmaz.
- Rasterlar üretim aracında mevcut sentetik P03-05C gerçek Chrome görüntüleri referans alınarak hazırlandı; kaynak `BookingPolicy.tsx`/CSS/tokens ve güncel `ManagementLayout` ile karşılaştırıldı. Eski görüntüde bulunmayan güncel Randevular gezinmesi masaüstü taslağa dahil edildi. Eski Owner örneği gerçek müşteri bilgisi değildir; taslakta sentetik işletme etiketi kullanıldı.
- Kendi değerlendirmesi: yeni alanların görsel ağırlığı mevcut alanlarla aynı, sayı/birim hizaları ve koyu metin/açık zemin korunuyor; varsayılan onay ve otomatik onayın sonuçları metinle ayrılıyor. İlk tek uzun mobil üretim okunabilir dar viewport oranını iyi temsil etmedi; tek düzeltme turunda iki ardışık görünümle değiştirildi. Nihai mobilde metinler sarılıyor ve Kaydet sonunda; masaüstünde uzun radio yardım metni sağ sütunda kırılıyor. Raster masaüstü alt panel marjını tam göstermiyor; uygulamada panel doğal yüksekliğiyle bütünüyle görünmelidir. Kesin metin, ölçü ve semantik otorite bu brief ve mevcut tokenlardır.
- Bunlar statik tasarım önerileridir: gerçek 390/1280 ekran görüntüsü, responsive/kontrast ölçümü, 44 px hedef/klavye/CSRF/API kabulü değildir. Görüntüdeki değer/kapalı düğme gerçek kayıt sonucu değildir. Exact promptlar yan `.prompt.txt` dosyalarında ve PNG içine gömülü; üretim/değerlendirme kaydı [draft.json](../mocks/booking-policy-extension/draft.json). Önceki taslak, görsel sistem ve çalışma dalı korunur.

Bu brief/taslak hazırlığı kod yazmadan tamamlandı; ardından kullanıcı iki alanın uygulama/test/ana teslimine açıkça yetki verdi. Onay, uygulama kabulünün yerine geçmez. Gerçek ana Owner oturumu/POST veya SMS kullanılmaz. DESIGN.md ve Impeccable bağlam yapılandırması bu dar işin yan etkisi olarak onarılmaz.

## P04-07A uygulama kabulü — 17.10.2026

İki alan mevcut endpoint/CSRF/sürüm/no-op/audit ve kamu katalog projeksiyonuna bağlandı. Formda sayı yardımı, false/0 değeri, adlandırılmış fieldset/radio grubu, tek Kaydet, kirli taslak/boş yanıt/hata/odak/bekleme ve belirsiz sonuç korunur. Alanlar aynı satır sistemini kullanır; yeni ortak görsel token veya bağımlılık yoktur. Rasterdaki TASLAK/örnek değer etiketleri ürüne taşınmaz. Fieldset'in erişilebilir adı dışarıdaki görünür başlıkla `aria-labelledby` üzerinden sağlanır; klavye radio davranışı doğaldır.

Yerel web typecheck/lint, 358 test ve build geçti. Ayrı 8100 sentetik Chrome/gerçek API kabulünde dört genişlikte 36 görüntü; false/125 kalıcılığı ve 0 açıklaması, radio ok tuşu/44 px etiket hedefi, iptal alan hatası/odak, tek gönderim, gerçek 409/CSRF/yanıt kaybı/Staff 403/anonim 401/429 ve beş asset eşitliği geçti. Dört temsilci görüntü onaylı taslak/DESIGN ile incelendi: genişte iki sütun, mobilde sarma/tek sıra, görünür odak ve doğal panel sonu korunuyor; yatay taşma/script hatası yok. Tam WCAG sertifikası veya müşteri rezervasyon kabulü değildir. Kanıt `.local/p04-07a/`; son kaynak/CI/restore/ana teslim tamamlanmadan uygulama done değildir.

Son kaynak/restore/head-main CI ve yedekli ana 8080 teslimi tamamlandı; [kanıt ve geri dönüş](../../docs/plans/P04.md#p04-07a-github-ve-ana-yerel-teslim--17102026). İki alan gerçek API ile çalışır; eski randevular/46 tablonun eski verileri ve hesap/MFA/anahtar/ayar/SMTP korundu. Yeni müşteri yazımı/OTP/SMS başlamadı. Son belge kaydının kendi CI kapıları korunur.
