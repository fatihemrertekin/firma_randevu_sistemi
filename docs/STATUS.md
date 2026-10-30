# STATUS — Güncel durum ve sıradaki iş

**P07-07 izinli firma hedefleri ve izolasyon temeli `in_progress` (30.10.2026).** Kullanıcı P07-06 bağlantı modelini ve ekranı “Modeli ve ekranı onaylıyorum” yanıtıyla onayladı; ADR-012 accepted, karar dilimi done. [Dar kapsam/kabul](plans/P07.md#p07-07--izinli-firma-hedefleri-ve-izolasyon-temeli--30102026). Operatörde sunucudan izinli hedef kataloğu, metadata GET'leri, tek firma uyumluluğu ve çok firmada varsayılan seçimi engelleyen eski rota koruması uygulanıyor. Seçili firma ödeme/erişim özeti, mali routing ve onaylı ekranın gerçek API bağlantısı bu dilimde teslim edilmiş sayılmaz.

Kabul kapıları: iki ayrı sentetik PostgreSQL firma DB'sinde marker/rol/MFA/CSRF, veri karışmaması ve firma kesintisi; ilgili dört sunucu sınıfı/iki web suite, locked restore/build/format, operatör imajı ve tam PR head/main CI. Kanıtlar `.local/p07-07/` altında tutulur; bütün kapılar tamamlanmadan done yoktur. Şema/migration/grant, gerçek hesap/sır/ödeme/mesaj veya internet yayını yoktur. Normal uygulama kaynağı değişmediği için ana8080 kaynak/imaj/health eşitliğiyle korunur; ilgisiz rebuild/restart yapılmaz.

P07-06 [PR198](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/198) head CI41112349422 ve birleşme main CI41112520419 başarılı; ana8080 live/ready200 ve kabul imajı korundu. P07-05 otomatik salt okunur/açılma `done` kalır; [kural/kabul](plans/P07.md#p07-05-kabul-ve-yedekli-ana8080-teslimi). Önceki tek firma ödeme ve Owner görünümü korunur.

**Sıradaki tek iş:** P07-07 teslim kapılarından sonra P07-08 seçili firma mali API'si ve doğru gecikmiş borç/erişim özeti. Onaylı firma seçimi ekranının gerçek entegrasyonu P07-09'dur. Mevcut UI ile çok firma ödeme yönetimi henüz tamamlanmadı. İnternet/P06/ilk gerçek hesap ve ücretli pilot kapıları açıktır.

P04 kullanıcı sıralama farkı PR dışında korunur. Depo geliştirme sırasında kullanıcı tercihiyle açıktır; görünürlük değiştirilmez. P06 izolasyon/host dışı restore/izleme ve ticari/mahremiyet kapıları açıktır. Aylık görünüm/müşteri kartı iptali ve [gerçek SMS'in proje sonuna ertelenmesi](plans/P05.md#ücretli-gerçek-sms-kabulünün-proje-sonuna-ertelenmesi--20102026) korunur.
