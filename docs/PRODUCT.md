# PRODUCT — İş modeli, satış, ödeme ve maliyet

Hazırlanma: 2026-09-29. Bu dosya iş kararlarını ve varsayımlarını tutar; teknik kurallar AGENTS.md'dedir, aşamalar ROADMAP.md'dedir. Şirket henüz kurulmadığı için buradaki hukuki/vergisel notlar uzman doğrulaması gerektirir.

## 1. Ürün özeti

Türkiye'deki küçük hizmet işletmelerine markalı randevu/yönetim yazılımı, aylık peşin abonelikle. İlk hedef tek şubeli berber/kuaför; sonraki hedef güzellik salonları. Amaç telefon yükünü ve çakışmaları azaltmak, günlük işi kolaylaştırmaktır.

Ticari politikalar: Modelimiz aylık ücretli ve reklamsızdır; kota/maliyet şartları sözleşmede açıktır. Rakibin ücretsiz veya sınırsız mesaj vaadini maliyet hesabımıza aktarma. Deneme sentetik demo ile başlar; kaynak tüketen ücretsiz canlı kurulum iş kararı ister. Ücretli özel domain ve destek seviyesi ayrıca tanımlanır.

## 2. Ürün kararları

30.09.2026 ürün sahibi yanıtları kaydedildi. [P00 planındaki](plans/P00.md) pilot kapsamı ve işletme politikaları bu kararlara göre netleştirildi.

### Alınan kararlar

1. **İlk değer önerisi — ürün hipotezi.** AI olmadan, tek şubeli işletmeye markalı çevrim içi rezervasyon, güvenilir çakışma önleme ve sade günlük takvim sunmak. Ölçülecek yarar: telefonda randevu ayarlama yükü ve çakışma sayısı. Bu bir rakiplerden üstünlük veya doğrulanmış müşteri talebi iddiası değildir; işletme görüşmelerinde sınanır. İlk satılabilir sürümde AI kesinlikle yoktur. P17 yalnız ayrıca istenirse değerlendirme kaydıdır.
2. **WhatsApp.** İlk sürümde WhatsApp entegrasyonu ve hatırlatması yoktur. Ek yük ve maliyet gerekçesiyle P12'deki isteğe bağlı gelecek iş olarak kalır. İlk hatırlatma kanalı için SMS tercih edildi; kapsam ve kota aşağıda netleşecek.
3. **Teknoloji yönü.** Seçim geliştiricinin önceki deneyimine göre yapılmayacak. Başlangıç mimarisi ASP.NET Core 10, EF Core 10/Npgsql 10, PostgreSQL 18 ve React/TypeScript/Vite olarak korunur. Tek backend, PostgreSQL zaman çakışması kısıtları ve aynı origin'den sunulan statik web bu ürünün ihtiyaçlarına uyar. İki dil ve iki derleme zinciri öğrenme/bakım bedelidir; performans ve teslim hızı ölçülmeden garanti edilmez. Desteklenen patch, lisans ve uyum P01'de doğrulanıp ADR-001'e yazılır; hedef pilot tarihi henüz belirlenmedi. Kaynaklar: [.NET destek politikası](https://dotnet.microsoft.com/en-us/platform/support/policy), [EF Core 10](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew), [Npgsql 10](https://www.npgsql.org/efcore/release-notes/10.0.html), [PostgreSQL destek politikası](https://www.postgresql.org/support/versioning/).
4. **Pilot kapsamı.** Çevrim içi misafir rezervasyonunda SMS ile tek kullanımlık telefon doğrulaması (OTP); onaylanan randevuya bir SMS hatırlatma; gerektiğinde işletmenin manuel onayı. SMS tekrar/kota/harcama sınırı gerekir. Ücretli pilot öncesinde müşteri/randevu verisini CSV olarak dışa alma ve işletmenin bize ödediği aylık aboneliğin dönem/ödeme kaydı gerekir. Abonelik kaydı, salon müşterisinden hizmet bedeli tahsilatı değildir. [P00 kabul listesi](plans/P00.md#pilot-kabul-listesi-henüz-karşılanmadı) bu kapsama göre güncellendi.
5. **İşletme görüşmeleri.** Görüşmeleri ürün sahibi, ürün pilot için hazır olduğunda yürütecek; agent görüşme veya satış yapmayacak. Hedef 3–5 işletme; henüz görüşme/talep doğrulaması yok. Gerçek ücretli pilot ve hosting kararı öncesinde bulgular değerlendirilecek. Sentetik yerel geliştirme bu süre boyunca sürebilir.
6. **İşletme politikaları.** Müşterinin bağlantıdan ne kadar kala iptal edebileceğini firma sahibi yönetim panelinden belirler; kural rezervasyon sırasında gösterilir ve sunucuda uygulanır. Manuel onay bekleyen `Pending` istek 30 dakika boyunca saati bloke eder; bu sürede onaylanmazsa otomatik iptal edilir ve saat serbest kalır. `NoShow` durumunu randevu başladıktan sonra Owner veya yetkili Staff işaretleyebilir. Pilot destek saatleri `Europe/Istanbul` ile Pazartesi–Cumartesi 08:00–19:00; kanallar e-posta ve telefon. İletişim adresi/numarası pilot hazırlığında belirlenecek. Destek formu sonraki aşama adayıdır; ilk yanıt süresi taahhüdü henüz yoktur. [P00 politika tablosu](plans/P00.md#işletme-politikaları) ayrıntıları izler.

## 3. Satış, ödeme, maliyet ve kapanış

- Şirket henüz yok. Ücretli sürekli hizmet öncesi mali müşavirle mükellefiyet/kuruluş, fatura/e-belge, vergi ve SGK'yı netleştir. Bireysel ödeme hesabı/havale vergi muafiyeti değildir. KVKK/VERBIS ve ticari ileti/IYS uygulanırlığını uzmanla belirle; agent uygunluk belgesi vermez. Sentetik geliştirme sürer.
- Satış: görüşme → demo → kapsam/fiyat → sözleşme/aydınlatma → ödeme → kurulum/eğitim → teslim. Abonelik kullanım/hosting/bakım/tanımlı destek; kod sahipliği/devir, özel iş ve mesai dışı destek ayrıca yazılır. Agent izinsiz satış mesajı göndermez.
- İlk tahsilat havale/uygun ödeme linki; banka/sağlayıcı kaydıyla doğrula, ekran görüntüsüyle paid yapma. Ayın 1'inde ay peşin. İlk kısmi dönem = aylık bedel × kalan takvim günü / ayın gün sayısı; başlangıç dahil, sonraki ayın 1'i hariç. Yuvarlama/vergi fatura ile tutarlı. Abonelik erişimini firma sahibi değiştiremez; kontrollü operatör işlemi veya doğrulanmış webhook değiştirir, audit edilir.
- Durum: Active → PastDue → Suspended → Closed. Öneri 7 gün ek süre, sonra yeni rezervasyon kapalı; sahibin görüntüleme/ihracı açık. İptal ödenmiş dönem sonunda, sonraki çekim yok. Kesinti önceden bildirilir; yaklaşan randevulara geçiş planı. Öneri 30 gün ihracat, sonra gerekli yasal kayıtlar ayrılarak silme/yedeklerin sona ermesi. Bunlar ürün önerisidir, yasal süre değildir; sözleşmesiz otomatik silme yok.
- Standart firma.markan.com; subdomain başına kayıt bedeli yok, DNS/sertifika kotasını kontrol et. Subdomain tek başına performansı düşürmez. Özel domain müşterinin, kayıt/yenileme yıllık peşin ve sorumluluk açık; HTTPS otomatik yenilenir.
- Maliyet: VPS+IP+yedek+harici depolama+domain/12+mesaj/e-posta+komisyon+lisans+muhasebe/SGK/vergi+emek+kur/risk. P07'de resmi tarifeyi tarih/para birimi/vergiyle kaydet. Fiyat = değişken gider+sabit gider payı+emek+kâr. Başabaş = sabit gider/(net abonelik geliri−firma başı değişken gider); payda pozitif olmalı. Henüz olmayan müşterilere güvenerek gideri bölme.
- Hetzner saatlik/aylık tavanlı; kapatmak faturayı durdurmaz, silmek gerekir. Ayrı IP/volume/snapshot'ı kontrol et. Müşteri ayrılınca ortak VPS gideri sürer. Son müşteriden sonra ihracat/saklama tamamlanarak gereksiz kaynaklar silinir; zorunlu depolama gideri kalabilir.
- Tarihli gözlem (bayatlayabilir, uygulama zamanında yeniden doğrula): 29.09.2026'da Hetzner CX23/CX33 stok yok görünüyordu; en ucuz planı bulunur varsayma.


## 4. Görsel tasarım yönü (01.10.2026 onayı)

Ürün sahibi koyu zeminli, görselli giriş ekranını örnek gösterdi ve sonraki ekranların da bu dili taşımasını istedi. Petrol/lacivert zemin, açık okunur metin ve turkuaz vurgu ortak renk değişkenleriyle uygulanır. Masaüstü girişte sade form ve sektöre uygun dekoratif görsel yan yana; mobilde form önceliklidir. Yönetimde aynı palet, belirgin bölüm gezinmesi, ana/yardımcı işlem ayrımı ve görünür klavye odağı kullanılır.

Son form tercihi: dış kart çerçevesi kaldırılır; tek belirgin ana giriş düğmesi kullanılır. Parola yardımı parola etiketinin yanında, çalışan parola yardımı ve davet seçenekleri aynı satırda metin eylemleridir. Dar ekranda da yardımcı işlemler alt alta büyük düğmeler olarak sunulmaz; gerekirse metin kendi sütununda satır kırar.

Örnek ekranın logo, satış, iletişim, hukuki onay veya henüz uygulanmamış özellikleri ürüne taşınmaz. Mevcut işlevler esas alınır; tasarım tercihi yeni özellik veya aşama yetkisi vermez. İlk görsel, yerleşik imagegen ile üretilmiş kişisiz/yazısız kuaför fotoğrafıdır; harici resim/font isteği yoktur. Görselin üretim kaydı [P02-10](plans/P02.md#p02-10--frontend-düzeni-ve-tasarım) içindedir.
