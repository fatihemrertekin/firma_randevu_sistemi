# PRODUCT — İş modeli, satış, ödeme ve maliyet

<!-- impeccable:product-schema 1 -->

Hazırlanma: 2026-09-29. Bu dosya iş kararlarını ve varsayımlarını tutar; teknik kurallar AGENTS.md'dedir, aşamalar ROADMAP.md'dedir. Şirket henüz kurulmadığı için buradaki hukuki/vergisel notlar uzman doğrulaması gerektirir.

## Platform

web

## Users

İlk hedef Türkiye'deki tek şubeli berber/kuaför işletmeleridir. Owner işletme tanımlarını ve yetkili yönetim işlemlerini yürütür; Staff yalnız izinli işlemleri kullanır. Sonraki hedef güzellik salonları ve benzer randevulu hizmet işletmeleridir. Ürünün planlanan misafir rezervasyon akışını salon müşterileri kullanacaktır; bu akış henüz teslim edilmiş değildir.

## Product Purpose

### 1. Ürün özeti

Türkiye'deki küçük hizmet işletmelerine markalı randevu/yönetim yazılımı, aylık peşin abonelikle. İlk hedef tek şubeli berber/kuaför; sonraki hedef güzellik salonları. Amaç telefon yükünü ve çakışmaları azaltmak, günlük işi kolaylaştırmaktır.

Ticari politikalar: Modelimiz aylık ücretli ve reklamsızdır; kota/maliyet şartları sözleşmede açıktır. Rakibin ücretsiz veya sınırsız mesaj vaadini maliyet hesabımıza aktarma. Deneme sentetik demo ile başlar; kaynak tüketen ücretsiz canlı kurulum iş kararı ister. Ücretli özel domain ve destek seviyesi ayrıca tanımlanır.

## Positioning

Her firmaya ayrı kurulumla sunulan, firmanın markasına uyarlanabilen ortak bir randevu ürünüdür; tenant filtresine dayalı ortak veri izolasyonu kullanılmaz. Değer önerisi aşağıdaki ürün hipotezidir; rakip üstünlüğü, ölçülmüş tasarruf veya doğrulanmış pazar talebi iddiası değildir.

## Operating Context

Yönetim, işletme sahibi ve yetkili personelin günlük işi için Türkçe responsive web üzerinden kullanılır. Mevcut giriş, MFA ve yönetim akışları aynı origin'deki API ile çalışır; masaüstü ve mobil kullanım birlikte ele alınır. İşletme zamanı varsayılan `Europe/Istanbul`dur. Misafir rezervasyonunun planlanan hizmet → çalışan → zaman → iletişim → onay akışı, ilgili aşamada gerçek backend desteğiyle kurulacaktır.

## Capabilities and Constraints

Mevcut teslim kimlik/giriş/MFA/kurtarma, çalışan erişimleri, işletme bilgileri/logosu/saatleri, personel/hizmet tanımları ve değişiklik kayıtlarını kapsar. Owner yönetimi ile Staff yetkileri ayrıdır. Güncel kabul ve ortam sınırlarının kaynağı [STATUS](STATUS.md), ayrıntılı kanıtın kaynağı [P02](plans/P02.md)dir.

Randevu motoru ve müşteri rezervasyonu sonraki aşamalardır; tasarımda uygulanmış gibi sunulmaz. MVP tek şube ve randevu başına tek hizmet/çalışandır. Firma izolasyonu, mevcut teknoloji, backend doğrulaması, cookie/CSRF/MFA ve mahremiyet sözleşmeleri tasarım yenilenirken korunur; teknik kurallar [AGENTS](../AGENTS.md)dedir. AI, WhatsApp ve salon adına kart tahsilatı ilk sürümün dışındadır. Ürün sahibinin görsel üretim izni, ürüne AI özelliği ekleme izni değildir.

## Evidence on Hand

- Mevcut uygulama ve kabul kayıtları: [STATUS](STATUS.md), [P02](plans/P02.md), [çalışan kurulum/kontrol komutları](../README.md).
- Mevcut giriş görseli [salon-login.jpg](../src/Web/src/assets/salon-login.jpg), geliştirmede üretilmiş dekoratif kuaför fotoğrafıdır; gerçek işletme/müşteri referansı değildir. Üretim kaydı P02-10 içindedir.
- İşletme logosu firma ayarıdır; örnek veya üretilmiş görsel gerçek bir firmanın kimliği gibi sunulmaz.
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
4. **Pilot kapsamı.** Çevrim içi misafir rezervasyonunda SMS ile tek kullanımlık telefon doğrulaması (OTP); onaylanan randevuya bir SMS hatırlatma; gerektiğinde işletmenin manuel onayı. SMS tekrar/kota/harcama sınırı gerekir. Ücretli pilot öncesinde müşteri/randevu verisini CSV olarak dışa alma ve işletmenin bize ödediği aylık aboneliğin dönem/ödeme kaydı gerekir. Abonelik kaydı, salon müşterisinden hizmet bedeli tahsilatı değildir. [P00 kabul listesi](plans/P00.md#pilot-kabul-listesi-henüz-karşılanmadı) bu kapsama göre güncellendi.
5. **İşletme görüşmeleri.** Görüşmeleri ürün sahibi, ürün pilot için hazır olduğunda yürütecek; agent görüşme veya satış yapmayacak. Hedef 3–5 işletme; henüz görüşme/talep doğrulaması yok. Gerçek ücretli pilot ve hosting kararı öncesinde bulgular değerlendirilecek. Sentetik yerel geliştirme bu süre boyunca sürebilir.
6. **İşletme politikaları.** Müşterinin bağlantıdan ne kadar kala iptal edebileceğini firma sahibi yönetim panelinden belirler; kural rezervasyon sırasında gösterilir ve sunucuda uygulanır. Manuel onay bekleyen `Pending` istek 30 dakika boyunca saati bloke eder; bu sürede onaylanmazsa otomatik iptal edilir ve saat serbest kalır. `NoShow` durumunu randevu başladıktan sonra Owner veya yetkili Staff işaretleyebilir. Pilot destek saatleri `Europe/Istanbul` ile Pazartesi–Cumartesi 08:00–19:00; kanallar e-posta ve telefon. İletişim adresi/numarası pilot hazırlığında belirlenecek. Destek formu sonraki aşama adayıdır; ilk yanıt süresi taahhüdü henüz yoktur. [P00 politika tablosu](plans/P00.md#işletme-politikaları) ayrıntıları izler.

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

### Renk paleti ve minimalizm tarifi (04.10.2026)

Ürün sahibi paleti `#001524`, `#15616D`, `#FFECD1`, `#FF7D00`, `#78290F` olarak belirledi. Açık temada arka planın baskın rengi `#FFECD1` olur. İleride koyu tema geliştirilirse baskın arka plan `#001524` olur; bu tercih koyu temanın şimdiden geliştirildiği anlamına gelmez.

Minimalizm; buton, form alanı, checkbox/checklist ve benzeri arayüz elemanlarının sadeliğidir. Giriş ekranının genel yapısı ilk fotoğraflı giriş ekranına yakın kalır: solda giriş formu, sağda geniş salon fotoğrafı/arka plan ve karşılama metni. İlk önerideki küçük çerçeveli fotoğraf ve fotoğrafsız alternatifler bu yeni yönün kaynağı değildir. Mobilde aynı palet ve sade kontroller korunur; fotoğraf formun kullanılabilirliğini bozmayacak kısa bir üst alan olarak taslaklanır.

Bu paletin rol önerisi: `#001524` ana yazı, `#15616D` bağlantı/ikincil vurgu, `#FFECD1` geniş zemin, `#FF7D00` ana eylem, `#78290F` ölçülü sıcak vurgu. Turuncu düğmede koyu yazı kullanılır; bütün renk/durum eşleşmeleri uygulama kabulünde kontrast ve erişilebilirlikle doğrulanır. Renkler sabittir; somut taslak ve uygulama onayı ayrı beklenir.
