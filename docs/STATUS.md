# STATUS — Güncel durum ve sıradaki iş

**P07-04 Owner salt okunur abonelik ekranı `done` (30.10.2026).** İşletme sahibi Hesap → Aboneliğiniz bölümünde kendi dönemini, vadesini, doğrulanan ödeme/kalan borcu ve kapalı dönem kayıtlarını gerçek API ile okur. Staff/anonim reddedilir; ödeme yazma yetkisi verilmez. [Kapsam ve kabul](plans/P07.md#p07-04-kabul-ve-yedekli-ana8080-teslimi).

[PR194](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/194) tam head ve birleşme main CI başarılı;65 ilgili web/3 gerçek PostgreSQL testi, gerçek MFA/API/tarayıcı, son8 görüntü ve56 tablonun okuma değişmezliği geçti. Ana8080 şifreli/geri okunmuş yedekle güncellendi;56 tablo,anahtar/ayarlar korundu; restart/anonim tarayıcı ve imaj eşitliği doğrulandı. Migration/restore, gerçek ödeme/mesaj veya ana Owner login/POST yoktur. Ayrı operatör kayıt akışı P07-03 done kalır.

**Sıradaki tek iş P07-05:** İstanbul takviminde vadenin ertesi günü otomatik salt okunur kısıtlama ve gecikmiş borç kapanınca açılma. Kısıtlama henüz etkin değildir; mevcut randevu/abonelik görüntüleme ve Excel korunarak backend/yarış/hata kabulü tamamlanacaktır. Merkezi çok firma yönetimi ve gerçek domain/TLS/proxy/ilk operatör kurulumu/P06 kabulü ayrıca açıktır; yerel test internet yayını değildir.

P04 kullanıcı sıralama farkı PR dışında korunur. Geliştirme sırasında açık GitHub deposu kullanıcı tercihidir; görünürlük değiştirilmez. P06 izolasyon/host dışı restore/izleme ve ticari/mahremiyet/pilot kapıları açıktır. Aylık görünüm/müşteri kartı iptali ve [gerçek SMS'in proje sonuna ertelenmesi](plans/P05.md#ücretli-gerçek-sms-kabulünün-proje-sonuna-ertelenmesi--20102026) korunur.
