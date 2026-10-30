# ADR-011 — Firma içi abonelik kaydı ve ayrı operatör yazma yetkisi

Tarih:29.10.2026. Durum: P07-02 dar temeli ve P07-03 ayrı operatör yazma/MFA kanalı yerelde kabul edildi (30.10.2026). P07-04 Owner salt okunur UI yerelde kabul edildi; otomatik kısıtlama, merkezi çok firma yönetimi ve gerçek internet kurulumu sonraki işlerdir. İlk kararın P07-02 sınırları aşağıda tarihsel olarak korunur.

## İhtiyaç

Onaylanan abonelik taslağı operatörün dönem/ödeme kaydı yapmasını, firma Owner'ının yalnız kendi özetini görmesini gerektiriyor. İlk sürüm gecikmeyi bildirir; abonelik nedeniyle hizmet/rezervasyon kapatmaz. Mevcut kimlik sistemi firmanın Owner/Staff hesaplarıdır; bu hesaplara ödeme doğrulama veya süre uzatma yetkisi vermek ürün kararına aykırıdır.

## Seçenekler

1. Firma Owner'ına ödeme yazma yetkisi: ödeme otoritesini işletmeye verir; reddedildi.
2. Merkezi yeni abonelik/müşteri veritabanı ve senkronizasyon servisi: ek işletim/kimlik/dağıtım maliyeti; bu dar dilimde gerekli değil. Firma randevu/müşteri kayıtları merkezileştirilmez.
3. Her firmanın kendi DB'sinde abonelik kaydı, normal uygulama rolüne yalnız SELECT; ayrı ve açık operatör yazma sınırı: mevcut kurulum/yedek/izolasyon örüntüsünü korur. Seçildi.

## Karar

P07-02 iki ek tablo ve gerçek Owner+MFA salt okunur API içerir. Dönem, doğrulanmış ödeme ve düzeltme hareketi firma veritabanındadır; finansal tutarlar decimal/TRY, tarihler İstanbul takvimidir. Pozitif ödeme ve ona bağlı tek negatif ters hareket değiştirilemez. Ters çevrilmiş ödeme, aynı gerçek banka referansını koruyan tek bağlantılı yeni tutar/dönem kaydıyla düzeltilebilir; zincir dallanamaz. DB constraint/trigger; çakışan dönem, fazla ödeme, çift ters kayıt, mükerrer referans ve eşzamanlı bakiye yarışını engeller. Read API tutarlı snapshot ve sonlu sayfalama kullanır; operatör kimliği/banka referansı/notlar Owner DTO'suna girmez.

`app_user` iki tabloya yalnız SELECT alır; INSERT/UPDATE/DELETE/TRUNCATE alamaz. Owner/Staff cookie'si veya müşteri HTTP isteği ödeme/dönem yazamaz; bu dilimde public operatör endpoint'i/hesabı/ortak admin sırrı yoktur. Test verileri yalnız ayrı sentetik DB'de hazırlanır. Yeni operatör hesabı/MFA bu dilimde teslim edilmiş sayılmaz.

P07-03'te kayıt işlemleri, ayrı operatör kimliği+MFA ve açık firma hedefiyle [işletim sınırı](../OPERATIONS.md#5-planlanan-destek-ve-işletim-ekranı) üzerinden ele alınacaktır. Son kullanıcı uygulamasına operatör oturumu, genel terminal/SQL, Docker socket veya başka firmanın DB sırrı verilmez. Taşıyıcı/dağıtım ve bağımsız operatör MFA kabulü somutlaştırılmadan yazma kanalı açılmaz. Bu karar bütün işletim panelini veya uzaktan kontrol servisini bugünden kurma yetkisi değildir.

## Bedel ve kabul

### P07-03 somut taşıyıcı kararı — 29.10.2026

Kullanıcı operatör alanına internetten bilgisayar ve telefonla erişmek istedi. Ayrı `src/Operator` ASP.NET uygulaması, ayrı yedi tabloluk Identity DB'si, cookie/Data Protection kimliği/anahtar volume'u ve zorunlu MFA kullanılır. Mevcut firma `AppUser`/Owner/Staff kimliği genişletilmez. Aynı özel PostgreSQL sunucusunda farklı DB ve roller kullanılabilir; başka firmanın kayıtları merkezileştirilmez. İlk dilim tek açık firma hedefidir; çok firmalı filo/kurulum/kurtarma paneli geliştirilmez.

Operatör kimlik rolü yalnız kendi Identity DB'sine; `subscription_operator` yalnız hedef firmanın iki abonelik tablosunda SELECT/INSERT ve dönem `Version` sütununda UPDATE alır. Randevu/kişi/Owner kimlik tablolarına erişmez. DB'de kontrollü provisioning ile kalıcı kurulum işaretçisi bulunur; HTTP gövdesindeki hedef ve gerçek DB işaretçisi uyuşmadan yazılmaz. Rol, süper kullanıcı veya başka rolün üyesi olamaz. Ayrı kimlik DB işaretçisi her API isteğinde doğrulanır. Normal `app_user` salt okunur kalır. Sırlar operatörün özel env dosyasındadır; firma web uygulamasına verilmez.

İlk operatör hesabı açık kayıt endpoint'inden oluşturulmaz. Yalnız doğru işaretçili, boş ayrı Identity kurulumunda kontrollü `initialize-operator` komutu çalışır; mevcut hesap yenilenmez. Şema v1 için yedi Identity tablosu yalnız bu açık komutla oluşturulur; HTTP başlangıcında DDL/migration yoktur. Sonraki Identity şema değişimi ayrı migration planı ister. Production host allowlist ve HTTPS zorunludur; forwarded başlıklar yalnız açık tek proxy IP'sinden kabul edilir. Yeni imaj/yerel sentetik TLS kabulü, P06 internet/VPS/HTTPS/host dışı restore/pilot kabulü sayılmaz.

Tekrarlanan mali istek anahtarı aynı içerikte aynı sonucu verir, farklı içerikte409'dur. Dönem anahtarı dönem ID'sidir; ödeme/ters/düzeltme anahtarları hash ile kayıttadır. Tek firma için transaction advisory kilidi, DB sürümü ve mevcut trigger'lar birlikte kullanılır; başka yazıcının bakiye güvencesi kaldırılmaz. Düzeltmenin ters/yeni hareketi tek transaction'dır. Ters kayıt banka iadesi değildir. Gecikmede otomatik erişim kısıtlaması yeni ürün kararıdır; bu operatör diliminde rezervasyon gate'i açılmaz, Owner görünümü ve kısıtlama/açılma kabulü sıradaki bağımlı dilimlerdir.

P07-03 tek açık firma hedefi üzerinden operatör özetini ve yazma ekranını gerçek API ile teslim eder; merkezi tek sorgu yerine firma bazında izinli erişim gerekir. P07-04 Owner abonelik ekranının gerçek UI bağlantısını mevcut salt okunur API ile teslim eder; çok firma işletim ekranı sonraki iştir. P07-02 API hazırlığı bunları yapılmış göstermez. Şema eklemelidir; eski imajla uyumlu kalır. Veri varsa Down kayıtları silmez, açık hata verir; imaj geri dönüşünde ek tablolar korunur.

Mevcut54 tablonun tüm eski satır/kolonları ve anahtar/ayarlar korunarak migration uygulanır. Yeni iki tablo ana kurulumda boş kalır; gerçek ödeme, Owner POST veya SMS yoktur. Gerçek PostgreSQL'de tutar/ters hareket/yarış/rollback/rol ve gerçek auth API kabulü gerekir. Abonelik yok/gecikmiş/okuma hatasında randevu/Excel davranışına gate eklenmez.

### P07-05 kabul sırasında hesaplanan erişim — 30.10.2026

Erişim için ikinci bir güncel durum tablosu/cron/senkronizasyon kuyruğu yerine mevcut dönem ve değiştirilemez net ödeme kayıtları her korunan yazımda okunur. İstanbul gününde başlamış/vadesi geçmiş herhangi bir eksik dönem kısıtlar; kayıt yok veya gelecek dönem kapatmaz. Ortak transaction kilidi70703, operatör yazımında exclusive/işletme yazımında shared'dir. Kontrol ReadCommitted içinde, bekleme sonrası taze snapshot ile yapılır; commit'e kadar kilit tutulur. Kabul anında karar verilir,5 saniyede doğrulanamazsa503 ve yazmama. Giriş/güvenlik, mevcut müşterinin iptal/ret hakkı, geçmiş okuma/Excel ve kayıtlı işlemin idempotent sonucu açık kalır. Bedeli: korunan yazım başına DB sorgusu ve mali işlem sırasında kısa bekleme; cache eskimesi veya restart ile erişim açılması yoktur. Eski imaj rollback'i bu yeni kısıtlamayı kaldırır; şema veya mali geçmişi değiştirmez. Uygulama/son teslim kanıtı P07 planında tutulur.
