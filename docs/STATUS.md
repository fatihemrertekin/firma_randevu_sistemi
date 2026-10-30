# STATUS — Güncel durum ve sıradaki iş

**P07-05 otomatik salt okunur kısıtlama ve ödeme sonrası açılma `done` (30.10.2026).** İstanbul günü vadenin ertesi gününe geçtiğinde başlamış herhangi bir eksik dönem işletme yazımlarını kapatır; bütün gecikmiş borç kapanınca sonraki işlem otomatik açılır. Kısmi ödeme yetmez. Kayıt yok/henüz başlamamış dönem kapatmaz. Mevcut randevu/abonelik/Excel,kimlik güvenliği ve müşterinin iptal/hatırlatma ret hakkı korunur. [Kapsam ve kabul](plans/P07.md#p07-05-kabul-ve-yedekli-ana8080-teslimi).

[PR196](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/196) tam head ve birleşme main CI başarılı;265 ilgili PostgreSQL/267 web testi,gerçek ayrı operatör ödeme→kısıtlama/kısmi/tam/ters kayıt/açılma,sekiz görüntü,restart/anahtar ve56 tablo değişmezliği geçti. Yedekli ana8080 güncellendi;56 tablo/anahtar/ayarlar korundu,anonim tarayıcı/health/imaj eşitliği geçti. Şema/migration/gerçek ödeme/mesaj,ana Owner login/POST veya ana restore yoktur. P07-03/04 kayıt ve Owner görünümü done kalır.

**Sıradaki tek iş P07-06:** merkezi çok firma abonelik yönetimi için bağlantı modeli,kapsam ve firma seçimi/özet görsel taslağı. Mevcut operatör tek açık firma içindir; internet/P06/ilk gerçek hesap ve ücretli pilot kapıları kapanmış değildir. Yeni ekranın ayrı görsel onayı ve bağlantı modelinin güvenlik kararı olmadan çok firma kodu üretilmez.

P04 kullanıcı sıralama farkı PR dışında korunur. Depo geliştirme sırasında kullanıcı tercihiyle açıktır; görünürlük değiştirilmez. P06 izolasyon/host dışı restore/izleme ve ticari/mahremiyet kapıları açıktır. Aylık görünüm/müşteri kartı iptali ve [gerçek SMS'in proje sonuna ertelenmesi](plans/P05.md#ücretli-gerçek-sms-kabulünün-proje-sonuna-ertelenmesi--20102026) korunur.
