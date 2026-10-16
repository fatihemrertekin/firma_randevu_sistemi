# Misafir rezervasyonu — kapsam ve görsel yön

Durum: yerel kapsam/görsel hazırlığı tamamlandı; **1 — Sabit özetli adımlar görsel yönü seçildi**, 16.10.2026. İnsan yanıtı Impeccable seçim sayfasından `optionId: summary`, `buildPath: comp` olarak alındı; [seçili görsel](../mocks/decision/public-booking-summary.png), [yanıt kaydı](../mocks/decision/public-booking-choice.json) ve görsel yan JSON'unda saklıdır. Bu seçim ekran/API uygulama yetkisi değildir. Mod: **Operate**. Hedef önerisi `/randevu`; mevcut uygulamada bu rota yoktur. Ürün otoritesi [PRODUCT](../../docs/PRODUCT.md), görsel otorite [DESIGN](../../DESIGN.md), ortak kontroller ve `tokens.css`'tir. [Uygulama planı](../../docs/plans/P04.md#p04-05--misafir-rezervasyonu-kapsam-ve-görsel-plan--16102026) teknik kapsamın tek kaydıdır.

## Ekran brief'i

- Amaç: işletmenin bağlantısından gelen müşteri, üyelik açmadan tek hizmet/çalışan için randevu talebi oluşturur; telefon doğrulaması ile işletme onayını karıştırmaz.
- Ana işlem ve sıra: hizmet → çalışan → zaman → asgari iletişim → telefon doğrulaması ve son kontrol → **Randevu talebi gönder**. Kod doğrulaması tek başına randevu oluşturmaz veya slot tutmaz. Varsayılan sonuç **İşletme onayı bekleniyor**; yalnız gerçek sunucu yanıtıyla gösterilir.
- Mobil: 320 px'te tek sütun, metinli ilerleme ve geri dönüş; seçim özeti düzenleyicinin yanında veya üstünde görünür. Sabit alt şerit/kaplayan modal yok. Geri dönüşte seçimler korunur; hizmet/çalışan/tarih değişince bağımlı eski saat temizlenir.
- API: P04-06 ile ayrı kamu katalog/çalışan/uygunluk GET'leri uygulandı; [sözleşme ve teslim durumu](../../docs/plans/P04.md#p04-06--müşteriye-açık-okuma-apileri--16102026) planındadır. Mevcut Owner uçları korunur. OTP, talep ve tek randevu durum/iptal sözleşmeleri halen eksiktir. Ekran bu okumalara henüz bağlanmadı; taslak sentetik gösterimdir, API/gönderim/kayıt kanıtı değildir.
- Durumlar: katalog/saat yükleniyor; hizmet/çalışan/saat yok; yükleme hatası ve tekrar dene; alan/kod hatası; 409 dolan saat veya değişen koşul; 429 bekleme; gönderim; sonucu belirsiz talep; Pending/Confirmed/Cancelled ve süre dolması. Başarı ve boş sonuç uydurulmaz.

## Korunan görsel sistem

Mevcut beyaza yakın zemin, beyaz form yüzeyi, düz mavi ana işlem, lacivert yardımcı işlem, ince sınır ve küçük köşeler korunur. Unna yalnız “Randevu” markasında; görev başlığı ve alanlar sistem sans. Müşteri yüzeyinde Owner menüsü, hesap/Çıkış yap, logo, müşteri fotoğrafı, istatistik, tahsilat veya üyelik zorunluluğu yoktur. İşletme adı gerçek uygulamada dar kamu DTO'sundan gelecektir; örnek görsellerde **Örnek işletme** kullanılır.

İptal ve onay koşulları son gönderimden önce okunur: başlangıç iptal süresi 120 dakika; tam sınır dahildir. Başlangıcı bundan yakın seçilen randevuda bağlantıdan iptal edilemeyeceği ayrıca açıklanır. İşletme onayı için son an `min(oluşturma + 30 dakika, başlangıç)`tır; takvimde saat seçmek bu süreyi başlatmaz. Müşteri koşulu kayıtla korunur; sonraki ayar değişimi geçmiş koşulu değiştirmez. SMS kodu iletişim bilgisini doğrular; randevu kesinliği anlamına gelmez.

## Karşılaştırılan yapılar

Görsel dünya sabittir; yalnız yerleşim seçilir. Yedi yapı: (1) tek odaklı beş adım; (2) tek sayfada açılır bölümler; (3) sabit özetli adımlar; (4) dikey ilerleme çizgisi; (5) hizmet listesi ve ayrı seçim sayfası; (6) seçim ve doğrulama için iki ana bölüm; (7) üstten seçim, altta tam genişlik zaman alanı. Aynı hizmet → çalışan → zaman sırası her yapıda korunur. Impeccable surface seed `efcf54a0`, doğrulanmış API sonucu 3/6/2; eski görsel dünya korunur, katalogdaki başka dünyalar bu yüzey turunun seçeneği değildir.

Sunulan üç seçenek (eşit görsel kapsam):

| Seçenek | Yerleşim ve ayırt edici davranış | Bedel |
| --- | --- | --- |
| 1 — Sabit özetli adımlar | Genişte solda geçerli adım, sağda hizmet/çalışan/saat özeti; mobilde özet üstte, form altta. Seçim değiştir bağlantıları ilgili adıma döner. | Küçük ekranlarda özet kısa tutulmalı. |
| 2 — İki ana bölüm | Önce hizmet/çalışan/zaman tek düzen içinde, ardından iletişim/doğrulama/son kontrol. Üstte iki metinli ilerleme bölümü vardır. | İlk bölüm daha fazla bilgiyi aynı anda gösterir. |
| 3 — Açılır bölümler | Tek sütunda önceki seçimler kısa özet satırları olur; geçerli bölüm açılır. Genişte de tek okuma çizgisi korunur. | Uzun akışta dikey kaydırma gerekir. |

Görseller `.impeccable/mocks/decision/public-booking-{summary,two-part,accordion}.png` altında, exact prompt/onay bilgileri yan JSON'lardadır. Taslaklardaki hizmet/fiyat/süre/çalışan/saatler açıkça örnektir; gerçek işletme verisi veya boş slot iddiası değildir. Her görsel aynı zaman seçme anını gösterir; durum kapsamı yukarıdaki brief'te tutulur. Bunlar gerçek responsive/erişilebilirlik/API kabulü değildir.

Görsel inceleme: üç yapının hiyerarşisi ve masaüstü/mobil farkı okunur; örnek veri notu, İstanbul saati, seçili saat ve işletme onayı/iptal koşulu görünür. Bir toplu düzeltme turunda ilk seçeneğin eksik mobil tarih alanı eklendi, birinci/üçüncü seçeneklerde Geri dolu lacivert yapıldı. Rasterın hafif buton ton geçişi ve yaklaşık ölçüleri uygulamaya taşınmaz; mevcut düz token stilleri bağlayıcıdır. Üçüncü seçeneğin mobil zaman dizilişi düzeltme isteğine rağmen masaüstüyle aynı sıraya gelmedi; bu açık görsel sınır korunur, uygulamada aynı kronolojik veri/DOM kullanılır. Bu yüzden raster tam ekran şartnamesi veya gerçek responsive/erişilebilirlik kabulü değildir. Seçim/düzeltme isteği beklenirken ek üretim turu açılmaz.

## Onay ve durma noktası

Üç görselden birinci seçildi; görsel yön yeniden onay beklemez. Shape adımı burada biter: Direction contract, uygulama kodu veya migration oluşturulmadı. “Başla” yalnız bu plan/taslak hazırlığına yetkiydi. Görsel seçimi API/OTP/maliyet veya üretim yayını onayı olarak yorumlanmaz; sonraki küçük uygulama kapsamı P04 planından açıkça sunulur.

Yerel kontrol: ilk üç görselli karşılaştırma sayfası Chrome/Playwright'ta 320/390/768/1280 px'te taşma/script hatası olmadan açıldı; metadata, örnek veri/onaysız işaretleri ve exact prompt dosyaları doğrulandı. İlk sandbox localhost denemesi engellendi, izinli kontrol geçti. Görsel düzeltmeleri sonrasında insan seçimi sunucuyu kapattı; son sayfa tekrarı bağlantı reddi verdi ve yeni başarılı kabul diye sunulmadı. Son iki raster doğrudan görüntüden incelendi, metadata/yanıt kayıtları ayrıca kontrol edilir. Kanıt `verify-public-booking.cjs`, `public-booking-verification.json`, `public-booking-options-*.png` ve görsel yan JSON'larındadır. Bunlar tasarım inceleme aracının kontrolüdür; ürün/API entegrasyonu veya dört genişlikte gerçek ürün kabulü değildir.
