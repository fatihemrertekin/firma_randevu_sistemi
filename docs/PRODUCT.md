# PRODUCT — İş modeli, satış, ödeme ve maliyet

<!-- impeccable:product-schema 1 -->

Hazırlanma: 2026-09-29. Bu dosya iş kararlarını ve varsayımlarını tutar; teknik kurallar AGENTS.md'dedir, aşamalar ROADMAP.md'dedir. Şirket henüz kurulmadığı için buradaki hukuki/vergisel notlar uzman doğrulaması gerektirir.

## Platform

web

## Users

İlk hedef Türkiye'deki tek şubeli berber/kuaför işletmeleridir. Owner işletme tanımlarını ve yetkili yönetim işlemlerini yürütür; Staff yalnız izinli işlemleri kullanır. Sonraki hedef güzellik salonları ve benzer randevulu hizmet işletmeleridir. Misafir rezervasyon akışının yerel/sentetik uygulaması vardır; gerçek gönderim ve pilot kabulü ayrı kapılardır.

## Product Purpose

### 1. Ürün özeti

Türkiye'deki küçük hizmet işletmelerine markalı randevu/yönetim yazılımı, aylık peşin abonelikle. İlk hedef tek şubeli berber/kuaför; sonraki hedef güzellik salonları. Amaç telefon yükünü ve çakışmaları azaltmak, günlük işi kolaylaştırmaktır.

Ticari politikalar: Modelimiz aylık ücretli ve reklamsızdır; kota/maliyet şartları sözleşmede açıktır. Rakibin ücretsiz veya sınırsız mesaj vaadini maliyet hesabımıza aktarma. Deneme sentetik demo ile başlar; kaynak tüketen ücretsiz canlı kurulum iş kararı ister. Ücretli özel domain ve destek seviyesi ayrıca tanımlanır.

## Positioning

Her firmaya ayrı kurulumla sunulan, firmanın markasına uyarlanabilen ortak bir randevu ürünüdür; tenant filtresine dayalı ortak veri izolasyonu kullanılmaz. Değer önerisi aşağıdaki ürün hipotezidir; rakip üstünlüğü, ölçülmüş tasarruf veya doğrulanmış pazar talebi iddiası değildir.

## Operating Context

Yönetim, işletme sahibi ve yetkili personelin günlük işi için Türkçe responsive web üzerinden kullanılır. Mevcut giriş, MFA ve yönetim akışları aynı origin'deki API ile çalışır; masaüstü ve mobil kullanım birlikte ele alınır. İşletme zamanı varsayılan `Europe/Istanbul`dur. Misafirin hizmet → çalışan → zaman → iletişim → onay akışı backend ve sentetik doğrulama ile kabul edilmiştir; gerçek SMS/gönderim kapıları ana kurulumda kapalıdır.

## Capabilities and Constraints

Mevcut teslim kimlik/MFA/kurtarma, çalışan erişimleri, işletme/personel/hizmet/audit yönetimi, Owner günlük/haftalık randevu yönetimi, müşteri rezervasyon/sonuç/iptal akışının gönderim kapalı sentetik kabulü ve kapalı OTP/hatırlatma/tek-randevu ret/normal SMS adaptörünü kapsar. Randevu API'si Owner içindir; Staff kimliği ayrı takvim yetkisi teslimi değildir. Güncel sınırlar [STATUS](STATUS.md), dar kabuller [P04](plans/P04.md) ve [P05](plans/P05.md) içindedir.

Motor ve yerel/sentetik rezervasyon teslimleri, gerçek gönderim veya ücretli pilotun hazır olduğu anlamına gelmez. 29.10.2026 kullanıcı kararıyla temel randevu ihracı gerçek Excel (.xlsx) olarak P04-45 kapsamında yerelde teslim edildi; abonelik dönem/ödeme kaydı henüz uygulanmadı; kurulum/izolasyon/host dışı yedek-restore ve ticari/mahremiyet kapıları ayrıca tamamlanır. [İlk sürüm eksiklerinin değerlendirmesi](plans/P04.md#p04-44--ilk-sürümün-zorunlu-eksiklerinin-kaynak-ve-kanıt-değerlendirmesi--29102026). MVP tek şube ve randevu başına tek hizmet/çalışandır. Firma izolasyonu, mevcut teknoloji, backend doğrulaması, cookie/CSRF/MFA ve mahremiyet sözleşmeleri tasarım yenilenirken korunur; teknik kurallar [AGENTS](../AGENTS.md)dedir. AI, WhatsApp ve salon adına kart tahsilatı ilk sürümün dışındadır. Ürün sahibinin görsel üretim izni, ürüne AI özelliği ekleme izni değildir.

## Evidence on Hand

- Mevcut uygulama ve kabul kayıtları: [STATUS](STATUS.md), [P02](plans/P02.md), [çalışan kurulum/kontrol komutları](../README.md).
- Mevcut giriş görseli [salon-login.jpg](../src/Web/src/assets/salon-login.jpg), geliştirmede üretilmiş dekoratif kuaför fotoğrafıdır; gerçek işletme/müşteri referansı değildir. Üretim kaydı P02-10 içindedir.
- Ürün sahibi 04.10.2026'da işletme logosu özelliğinin tamamen kaldırılmasını istedi. Logo yükleme, ayar ve gösterim ürün kapsamında değildir; geçmiş işlem kayıtları korunur.
- İşletme görüşmeleri, gerçek pilot ve ölçülmüş ticari sonuçlar henüz yoktur. Müşteri yorumu, kullanım sayısı, başarı oranı veya hizmet/fiyat verisi uydurulmaz.

## Product Principles

1. Günlük işi kolaylaştır; telefon yükü ve çakışmayı azaltma hedefini ölçülecek ürün hipotezi olarak koru.
2. Yalnız mevcut yetkili işlemleri göster; fiyat, süre, yetki ve kayıt doğruluğunda sunucu son karardır.
3. Firma izolasyonunu ve asgari kişisel veri ilkesini koru; sır veya gerçek kişi verisi tasarım girdisi olmaz.
4. Kullanıcıyı hatadan kurtar; yükleme, boş, hata ve başarı durumlarını açıkça ayır.
5. Ortak ürünü ve tekrar kullanılan kuralları sürdür; firma başına kod dalı veya gereksiz gelecek altyapısı üretme.

## Accessibility & Inclusion

Türkçe, görünür alan etiketleri, klavye kullanımı ve görünür odak gereklidir. WCAG 2.2 AA, en az 44×44 px dokunma hedefi, 320 px'te temel akışın kullanılabilirliği ve hareket azaltma tercihi proje kabul sınırlarıdır. Durum yalnız renkle anlatılmaz; sürükle-bırakın form/klavye alternatifi bulunur. Bunlar her ekran için doğrulanacak hedeflerdir; init bütün uygulamanın erişilebilirliğini kanıtlamaz.

## 2. Ürün kararları

30.09.2026 ürün sahibi yanıtları kaydedildi. [P00 planındaki](plans/P00.md) pilot kapsamı ve işletme politikaları bu kararlara göre netleştirildi.

### Alınan kararlar

1. **İlk değer önerisi — ürün hipotezi.** AI olmadan, tek şubeli işletmeye markalı çevrim içi rezervasyon, güvenilir çakışma önleme ve sade günlük takvim sunmak. Ölçülecek yarar: telefonda randevu ayarlama yükü ve çakışma sayısı. Bu bir rakiplerden üstünlük veya doğrulanmış müşteri talebi iddiası değildir; işletme görüşmelerinde sınanır. İlk satılabilir sürümde AI kesinlikle yoktur. P17 yalnız ayrıca istenirse değerlendirme kaydıdır.
2. **WhatsApp.** İlk sürümde WhatsApp entegrasyonu ve hatırlatması yoktur. Ek yük ve maliyet gerekçesiyle P12'deki isteğe bağlı gelecek iş olarak kalır. İlk hatırlatma kanalı için SMS tercih edildi; kapsam ve kota aşağıda netleşecek.
3. **Teknoloji yönü.** Seçim geliştiricinin önceki deneyimine göre yapılmayacak. Başlangıç mimarisi ASP.NET Core 10, EF Core 10/Npgsql 10, PostgreSQL 18 ve React/TypeScript/Vite olarak korunur. Tek backend, PostgreSQL zaman çakışması kısıtları ve aynı origin'den sunulan statik web bu ürünün ihtiyaçlarına uyar. İki dil ve iki derleme zinciri öğrenme/bakım bedelidir; performans ve teslim hızı ölçülmeden garanti edilmez. Desteklenen patch, lisans ve uyum P01'de doğrulanıp ADR-001'e yazılır; hedef pilot tarihi henüz belirlenmedi. Kaynaklar: [.NET destek politikası](https://dotnet.microsoft.com/en-us/platform/support/policy), [EF Core 10](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew), [Npgsql 10](https://www.npgsql.org/efcore/release-notes/10.0.html), [PostgreSQL destek politikası](https://www.postgresql.org/support/versioning/).
4. **Pilot kapsamı.** Çevrim içi misafir rezervasyonunda SMS ile tek kullanımlık telefon doğrulaması (OTP); onaylanan randevuya bir SMS hatırlatma; gerektiğinde işletmenin manuel onayı. SMS tekrar/kota/harcama sınırı gerekir. **23.10.2026 ürün sahibi kararı: tek hatırlatma randevudan 3 saat önce planlanır.** Bu zaman seçimi gerçek gönderim/harcama veya hatırlatma uygulamasının tamamlandığı anlamına gelmez; gönderim kapalı dilimin kapsamı ve önerilen sınır davranışları [P05-07 planındadır](plans/P05.md#p05-07--gönderim-kapalı-tek-hatırlatma-planı--23102026). Ücretli pilot öncesinde mevcut randevu snapshot verisini gerçek Excel (.xlsx) olarak dışa alma ve işletmenin bize ödediği aylık aboneliğin dönem/ödeme kaydı gerekir. Abonelik kaydı, salon müşterisinden hizmet bedeli tahsilatı değildir. [P00 kabul listesi](plans/P00.md#pilot-kabul-listesi-henüz-karşılanmadı) bu kapsama göre güncellendi.
5. **İşletme görüşmeleri.** Görüşmeleri ürün sahibi, ürün pilot için hazır olduğunda yürütecek; agent görüşme veya satış yapmayacak. Hedef 3–5 işletme; henüz görüşme/talep doğrulaması yok. Gerçek ücretli pilot ve hosting kararı öncesinde bulgular değerlendirilecek. Sentetik yerel geliştirme bu süre boyunca sürebilir.
6. **İşletme politikaları.** Müşterinin bağlantıdan ne kadar kala iptal edebileceğini firma sahibi yönetim panelinden belirler; kural rezervasyon sırasında gösterilir ve sunucuda uygulanır. Manuel onay bekleyen `Pending` istek 30 dakika boyunca saati bloke eder; bu sürede onaylanmazsa otomatik iptal edilir ve saat serbest kalır. `NoShow` durumunu randevu başladıktan sonra Owner veya yetkili Staff işaretleyebilir. Pilot destek saatleri `Europe/Istanbul` ile Pazartesi–Cumartesi 08:00–19:00; kanallar e-posta ve telefon. İletişim adresi/numarası pilot hazırlığında belirlenecek. Destek formu sonraki aşama adayıdır; ilk yanıt süresi taahhüdü henüz yoktur. [P00 politika tablosu](plans/P00.md#işletme-politikaları) ayrıntıları izler. 14.10.2026 başlangıç rezervasyon ayarları ürün sahibi tarafından seçildi: en az 60 dakika önceden, en fazla 30 gün ileriye ve 15 dakikalık başlangıç aralıkları; firma sahibinin değiştirebilmesi P03 kapsamındadır. 15.10.2026 P03-05B kararı: müşteri bağlantısından iptal için başlangıç sınırı randevudan en az 120 dakika öncedir (tam sınır dahil); daha geç iptal talebinde işletmeyle iletişime geçilir. Çevrim içi müşteri rezervasyonu için başlangıç tercihi işletme onayıdır; telefon doğrulaması tek başına randevuyu onaylamaz. Pending en geç 30 dakika içinde, randevu daha erken başlıyorsa başlangıçta sona erer. Firma sahibi bu tercihleri ileride değiştirebilir; mevcut rezervasyonun müşteriye gösterilmiş koşulu sessizce değişmez. Bu karar kaydı müşteri iptalinin, onay tercihi ayarının veya ekranının uygulandığı anlamına gelmez. [P03 politika ve kapsamı](plans/P03.md#politika-ve-açık-kararlar) bu seçimin uygulama sınırını tutar.

### Yönetim kapsamı ve müşteri kartı kararı (28.10.2026)

Ürün sahibi aylık randevu görünümünü, ardından müşteri kartı/randevu geçmişini gereksiz bularak kapsamdan çıkardı; günlük ve haftalık yönetim korunur. Müşteri kartı için hazırlanmış P04-43 taslağı tarihsel kayıttır; görsel onay beklenmez ve müşteri uygulaması adayı geri çekilmiştir. Mevcut randevulardaki ad/telefon bilgileri korunur. [Güncel kapsam kararı](plans/P04.md#p04-43r--müşteri-kartı-ve-geçmişinin-kapsamdan-çıkarılması--28102026).

### Satış sonrası destek ekranı kararı (02.10.2026)

Ürün sahibi, kurulum/bakım/destek için PowerShell veya sunucu komutu gerektiren bütün ürün işlemlerini ileride kendisine özel bir destek ve işletim ekranından yürütmeyi istedi. Hedef: firma seç → izinli işlemi seç → gerekli doğrulama/onay → sonucu gör; rutin destekte komut yazmak zorunlu olmayacak. Normal Owner parola yenilemesi mevcut doğrulanmış e-posta üzerinden self servis kalır; destek ekranı acil kurtarma ve diğer operatör işlemlerini kapsar.

Durum `planned`: yalnız geleceğe yönelik plan kaydı onaylandı, ekranın geliştirilmesi başlamadı. P06/P07 hazırlığına bağlanır; ayrıntılı kapsam ve uygulama için ayrı onay gerekir. İşlem envanteri, güvenlik sınırları ve kabul hedefleri [OPERATIONS §5](OPERATIONS.md#5-planlanan-destek-ve-işletim-ekranı) içindedir.

## 3. Satış, ödeme, maliyet ve kapanış

- Şirket henüz yok. Ücretli sürekli hizmet öncesi mali müşavirle mükellefiyet/kuruluş, fatura/e-belge, vergi ve SGK'yı netleştir. Bireysel ödeme hesabı/havale vergi muafiyeti değildir. KVKK/VERBIS ve ticari ileti/IYS uygulanırlığını uzmanla belirle; agent uygunluk belgesi vermez. Sentetik geliştirme sürer.
- Satış: görüşme → demo → kapsam/fiyat → sözleşme/aydınlatma → ödeme → kurulum/eğitim → teslim. Abonelik kullanım/hosting/bakım/tanımlı destek; kod sahipliği/devir, özel iş ve mesai dışı destek ayrıca yazılır. Agent izinsiz satış mesajı göndermez.
- İlk tahsilat havale/uygun ödeme linki; banka/sağlayıcı kaydıyla doğrula, ekran görüntüsüyle paid yapma. Ayın 1'inde ay peşin. İlk kısmi dönem = aylık bedel × kalan takvim günü / ayın gün sayısı; başlangıç dahil, sonraki ayın 1'i hariç. Yuvarlama/vergi fatura ile tutarlı. Abonelik erişimini firma sahibi değiştiremez; kontrollü operatör işlemi veya doğrulanmış webhook değiştirir, audit edilir.
- Durum: Active → PastDue → Suspended → Closed. Öneri 7 gün ek süre, sonra yeni rezervasyon kapalı; sahibin görüntüleme/ihracı açık. İptal ödenmiş dönem sonunda, sonraki çekim yok. Kesinti önceden bildirilir; yaklaşan randevulara geçiş planı. Öneri 30 gün ihracat, sonra gerekli yasal kayıtlar ayrılarak silme/yedeklerin sona ermesi. Bunlar ürün önerisidir, yasal süre değildir; sözleşmesiz otomatik silme yok.
- Standart firma.markan.com; subdomain başına kayıt bedeli yok, DNS/sertifika kotasını kontrol et. Subdomain tek başına performansı düşürmez. Özel domain müşterinin, kayıt/yenileme yıllık peşin ve sorumluluk açık; HTTPS otomatik yenilenir.
- Maliyet: VPS+IP+yedek+harici depolama+domain/12+mesaj/e-posta+komisyon+lisans+muhasebe/SGK/vergi+emek+kur/risk. P07'de resmi tarifeyi tarih/para birimi/vergiyle kaydet. Fiyat = değişken gider+sabit gider payı+emek+kâr. Başabaş = sabit gider/(net abonelik geliri−firma başı değişken gider); payda pozitif olmalı. Henüz olmayan müşterilere güvenerek gideri bölme.
- Hetzner saatlik/aylık tavanlı; kapatmak faturayı durdurmaz, silmek gerekir. Ayrı IP/volume/snapshot'ı kontrol et. Müşteri ayrılınca ortak VPS gideri sürer. Son müşteriden sonra ihracat/saklama tamamlanarak gereksiz kaynaklar silinir; zorunlu depolama gideri kalabilir.
- Tarihli gözlem (bayatlayabilir, uygulama zamanında yeniden doğrula): 29.09.2026'da Hetzner CX23/CX33 stok yok görünüyordu; en ucuz planı bulunur varsayma.


## Brand Commitments

### 4. Görsel tasarım yetkisi (04.10.2026)

Ürün sahibi mevcut tasarımı beğenmediğini belirterek Impeccable ile yeniden tasarıma yetki verdi. Tercih minimalist, tutarlı ve sürdürülebilir arayüzlerdir; görsel kararların seçimi agent'a bırakılmıştır. Önceki koyu palet, font ve yerleşim tercihleri yeni tasarımı bağlayan kurallar değildir. Kullanıcı `docs/UI-UX.md`yi kaldırdı; bu belge artık tasarım kaynağı değildir.

Sektöre uygun fotoğraf ve arka plan görselleri üretilebilir; bunlar ürünün gerçek müşteri/işletme kanıtı gibi sunulmaz. Firma markasına uyarlanabilirlik, Türkçe anlatım, mevcut işlevler, erişilebilirlik ve güvenlik korunur. Init adımında görsel kararlar seçilmedi; ürün sahibinin sonraki renk ve yapı tarifi aşağıda kaydedildi.

Her küçük adımın sonunda sonuç ve sonraki kapsam sunulur; ürün sahibinin ayrı onayı beklenir. Init adımının izni yalnız bağlamın hazırlanmasını kapsıyordu; ardından giriş ekranı taslağının hazırlanması ayrıca onaylandı. Taslak hazırlığı ekran kodu veya P03 yetkisi değildir. Kalıcı çalışma yöntemi `.impeccable/config.json` içinde tutulur; burada ikinci kopyası oluşturulmaz.

04.10.2026 devam kararı: ürün sahibi mevcut frontend'in tamamını, özellikle giriş sonrası eski düzende kalan ekranları, onaylı giriş sayfasının görsel diliyle Impeccable aşamalarından geçirerek yenilemeyi istedi. Görünümle birlikte işlem adımları ve gezinme de düzenlenecek. Yönetimde form/listeler öncelikli; salon fotoğrafı yalnız küçük bir marka alanında kullanılacak. Bu kapsamın aşama ve kabul planı [P02 yönetim tasarımı](plans/P02.md#yönetim-yüzeyleri--impeccable-tasarım-planı-04102026) bölümündedir. Girişteki büyük fotoğraf düzeni yönetim formlarına taşınmaz. Ürün sahibi 2. ve 3. seçeneklerin birleşik masaüstü/mobil taslağını ve T01 kod kapsamını onayladı; dar grup menüsü, kapanabilir alt menü, form ve taslak özeti uygulanır. Son fotoğraf kabul edildi; ek görsel/motor onay turlarıyla vakit kaybedilmemesi istendi. Sonraki ekranların ayrıntılı taslak/onay sınırı korunur.

### Renk paleti ve minimalizm tarifi (05.10.2026)

Ürün sahibi krem zemini açmayı, beyaz/açık gri zemin ve lacivert/mavi butonları seçti; kapsamı giriş, MFA ve diğer kimlik işlemleri dahil bütün mevcut frontend olarak belirtti. Baskın zemin `#FAFBFD`, alan yüzeyi `#FFFFFF`, ana metin `#253044`, ana işlem `#4C72AD`, yardımcı işlem `#405775`, hata/tehlikeli işlem `#A44D45` olur. Ana ve yardımcı işlem butonları beyaz yazıyla dolu yüzey taşır; gezinme/metin eylemleri hafif dolguludur. Renkler merkezi `tokens.css` değişkenlerinden yönetilir; şeffaf buton yüzeyi kullanılmaz.

Bu karar 04.10.2026 krem/petrol/turuncu paletinin yerini alır. Eski taslaklar yerleşim kararının tarihsel kanıtıdır; güncel renk kaynağı [DESIGN](../DESIGN.md) ve merkezi tokenlardır. Aynı gün devam kararında ürün sahibi yalnız mavi değil kullanılan tüm renkleri çok hafif pastel tonlarla yumuşatmayı, sağdaki krem fotoğrafı uyumlu hale getirmeyi ve krem olmayan benzer bir fotoğraf üretimini istedi. Tek nötr gün ışıklı beyaz/açık gri salon görseli giriş ve küçük yönetim fotoğrafında kullanılır; yerleşim korunur. Koyu tema geliştirilmiş değildir.

Minimalizm; buton, form alanı, checkbox/checklist ve benzeri arayüz elemanlarının sadeliğidir. Girişte soldaki form, sağdaki geniş salon fotoğrafı/karşılama metni korunur. Mobilde fotoğraf kısa üst alandır; yönetimde form/listeler öndedir. Renk/durum eşleşmeleri kontrast ve erişilebilirlik kontrolüyle doğrulanır; kimlik/API/CSRF/oturum kuralları korunur.
