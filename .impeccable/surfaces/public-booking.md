# Misafir rezervasyonu — kapsam ve görsel yön

20.10.2026 güncel durum: onaylanan UI uygulaması ve GitHub/ana 8080 teslimi tamamlandı; [güncel kabul/kanıt](../../docs/plans/P04.md#misafir-ekranı-kabul-ve-ana-8080-teslimi--20102026). Aşağıdaki planned/onay bekleme ifadeleri tarihli hazırlık kaydıdır. Gerçek SMS ve ana OTP/erişim kapalıdır.

Durum: yerel kapsam/görsel hazırlığı tamamlandı; **1 — Sabit özetli adımlar görsel yönü seçildi**, 16.10.2026. İnsan yanıtı Impeccable seçim sayfasından `optionId: summary`, `buildPath: comp` olarak alındı; [seçili görsel](../mocks/decision/public-booking-summary.png), [yanıt kaydı](../mocks/decision/public-booking-choice.json) ve görsel yan JSON'unda saklıdır. 16.10 seçimi yalnız görsel yön içindi; 18.10.2026 tarihli “Onaylıyorum” yanıtı güncellenen UI/API kapsamının uygulama, test, PR/CI ve kapıları kapalı ana 8080 teslimini onayladı. Mod: **Operate**. Hedef `/randevu` ve `/randevu/sonuc/{id}`; güncel uygulama/kabul kaydı P04 planındadır. Ürün otoritesi [PRODUCT](../../docs/PRODUCT.md), görsel otorite [DESIGN](../../DESIGN.md), ortak kontroller ve `tokens.css`'tir. [Uygulama planı](../../docs/plans/P04.md#p04-05--misafir-rezervasyonu-kapsam-ve-görsel-plan--16102026) teknik kapsamın tek kaydıdır.

## Ekran brief'i

- Amaç: işletmenin bağlantısından gelen müşteri, üyelik açmadan tek hizmet/çalışan için randevu talebi oluşturur; telefon doğrulaması ile işletme onayını karıştırmaz.
- Ana işlem ve sıra: hizmet → çalışan → zaman → asgari iletişim → telefon doğrulaması ve son kontrol → **Randevu talebi gönder**. Kod doğrulaması tek başına randevu oluşturmaz veya slot tutmaz. Varsayılan sonuç **İşletme onayı bekleniyor**; yalnız gerçek sunucu yanıtıyla gösterilir.
- Mobil: 320 px'te tek sütun, metinli ilerleme ve geri dönüş; seçim özeti düzenleyicinin yanında veya üstünde görünür. Sabit alt şerit/kaplayan modal yok. Geri dönüşte seçimler korunur; hizmet/çalışan/tarih değişince bağımlı eski saat temizlenir.
- API: P04-06 kamu okumaları, P05-02 telefon doğrulaması, P04-09 atomik yazım ve P04-10 tek randevu sonuç/iptal sözleşmeleri uygulandı. [Güncel ekran kapsamı ve API eşlemesi](../../docs/plans/P04.md#misafir-ekranı--güncellenmiş-kapsam-ve-durum-taslağı--18102026) teknik otoritedir. Ekran henüz bağlanmadı; ana ortam OTP/erişim kapalıdır. Görsel seçim, gerçek SMS veya ekran uygulama onayı değildir.
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

### 18.10.2026 kapsam yenilemesi

Yerel brief/kapsam hazırlığı `done`; aşağıdaki ekran uygulaması `planned`, onay bekliyor. 16.10 seçilmiş yerleşim ve renkler korunur; yeni alternatif veya yeniden görsel yön seçimi yoktur. [Gözden geçirme taslağı](../mocks/public-booking-review/README.md), seçili rasterı ve eklenen iletişim/doğrulama, sonuç, bağlantı ve iptal ekran durumlarını birlikte gösterir. Bu bir tasarım belgesidir; ürün kodu, çalışır prototip veya API kabulü değildir.

- Beş adım aynı kalır: **Hizmet / Çalışan / Zaman / İletişim / Kontrol**. Kod isteme İletişim adımındadır; Kontrol adımında maskeli telefon, altı haneli kod ve son özet bulunur. Kod doğrulandıktan sonra ayrı **Randevu talebi gönder** işlemi vardır. Kod isteme/doğrulama saati tutmaz. Çalışan açıkça seçilir; bu dilimde otomatik “uygun çalışan” ataması yoktur.
- Genişte geçerli adım solda, kısa seçim özeti sağda kalır. Mobilde özet formun üstündedir; 320/390 px'te kısa metinli “Adım 5 / 5 · Kontrol” göstergesi beş uzun etiketin taşmasını önler. 768 px'te içerik sığmıyorsa aynı tek sütun sırası, 1280 px'te seçili iki sütun kullanılır. Yapışan alt işlem şeridi ve kaplayan modal eklenmez.
- Sonuç görünümü ilk olarak sunucunun durumunu anlatır: **İşletme onayı bekleniyor / Randevunuz onaylandı / Randevu iptal edildi / Tamamlandı / Gelinmedi**. Sunucu zamanları İstanbul saatiyle, fiyat hizmet tutarı olarak gösterilir. Bekleme süresi dolduğunda kendiliğinden “iptal edildi” denmez; güncel sonuç okunur. Değişen fiyat/koşul sessizce kabul edilmez.
- Sonuçta **Durumu güncelle**, **Sonuç bağlantısını kopyala**, uygunsa **Randevuyu iptal et** bulunur. İptal, aynı sayfada tarih/hizmet özetiyle **Evet, randevuyu iptal et / Vazgeç** onayı açar. İptal sonucu sunucu cevabından sonra değişir. Bağlantı yetkisi alınamazsa rezervasyon başarısı silinmez; ayrı, düzeltilebilir uyarı gösterilir.
- “Sonucu belirsiz” görünümü alan hatası, geçersiz kod, güvenlik reddi ve kapalı hizmetten ayrılır. Gönderim/iptal yanıtı kaybolduğunda aynı işlem yeniden denenir; yeni randevu başlatılmaz. Süresi dolan/geçersiz bağlantı yalnız bu randevuya erişimi reddeder; telefonla geçmiş araması, Owner girişi veya üyelik önermez.

**Durma noktası:** kapsam ve ek durum taslağı sunulur; [AGENTS §11](../../AGENTS.md) gereği ekran kodundan önce ayrı uygulama onayı alınır. Gerçek SMS, hatırlatma, ana ortam kapılarını açma ve gerçek müşteriyle pilot bu onaya dahil değildir. Uygulama onaylanırsa test/PR/CI/main 8080 teslimi, kapılar kapalı tutularak ve ayrı sentetik ortamda yazma kabulüyle tamamlanır.

### 16.10.2026 tarihli seçim kanıtı

Üç görselden birinci seçildi; görsel yön yeniden onay beklemez. Shape adımı burada biter: Direction contract, uygulama kodu veya migration oluşturulmadı. “Başla” yalnız bu plan/taslak hazırlığına yetkiydi. Görsel seçimi API/OTP/maliyet veya üretim yayını onayı olarak yorumlanmaz; sonraki küçük uygulama kapsamı P04 planından açıkça sunulur.

Yerel kontrol: ilk üç görselli karşılaştırma sayfası Chrome/Playwright'ta 320/390/768/1280 px'te taşma/script hatası olmadan açıldı; metadata, örnek veri/onaysız işaretleri ve exact prompt dosyaları doğrulandı. İlk sandbox localhost denemesi engellendi, izinli kontrol geçti. Görsel düzeltmeleri sonrasında insan seçimi sunucuyu kapattı; son sayfa tekrarı bağlantı reddi verdi ve yeni başarılı kabul diye sunulmadı. Son iki raster doğrudan görüntüden incelendi, metadata/yanıt kayıtları ayrıca kontrol edilir. Kanıt `verify-public-booking.cjs`, `public-booking-verification.json`, `public-booking-options-*.png` ve görsel yan JSON'larındadır. Bunlar tasarım inceleme aracının kontrolüdür; ürün/API entegrasyonu veya dört genişlikte gerçek ürün kabulü değildir.


## Direction contract

THESIS: Tek göreve odaklanan adımlar, seçim bağlamını görünür tutar; işletme paneli veya müşteri hesabı eklenmez.

OWN-WORLD: Mevcut açık zemin, düz mavi/lacivert işlemler ve ince sınırlar. Unna yalnız markada, görevler sistem sans; ölçü ve renkler tokens.css kaynağındadır.

STORY: Müşteri hizmet/çalışan/zaman seçer; numarasını doğrular, koşulu görür, açıkça talep gönderir. Sonuç yalnız sunucu onayıyla gösterilir; tek randevu bağlantısı ve açık iptal onayı vardır.

FIRST VIEWPORT: Genişte görev başlığı ve beş adım solda; sağda aynı hizada seçim özeti. Tarih ve saatler sol formda, Geri/Devam et altında. Mobilde başlık/kısa ilerleme, kısa özet, form ve işlemler aynı DOM akışını korur; sonuçta durum ve özet işlemlerden önce gelir.

FORM: 1 — Sabit özetli adımlar; surface seed efcf54a0; seçili comp ../mocks/decision/public-booking-summary.png. Yeni görsel dünya veya raster yoktur. 18.10.2026 kullanıcı uygulama onayı güncel kapsamı bağlar; eski görsel onay kaydı tarihsel kalır.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
