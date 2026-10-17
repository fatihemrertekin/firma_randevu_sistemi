# OPERATIONS — Kurulum, işletim ve büyüme

P04-10 erişim tablosu/audit CHECK'i ekler; uygulama rolü sadece gereken token/sürüm/iptal makbuzu kolonlarını güncelleyebilir, erişim bitişi ve ilk verilme anını değiştiremez. `PublicBooking:AccessEnabled` varsayılan false'tur. Ana 8080 tesliminde erişim ve OTP kapalı; token/iptal tarihçesi oluşmuşsa P04-09 imajı güvenli rollback sayılmaz. [ADR-008](adr/008-public-appointment-access.md) yetki ömrünü ve uyum sınırını tutar.

Restore sonrası eski müşteri yetkileri yeniden açılmaz: uygulama kapalıyken veya iki kapı da false iken doğru firma/restore DB hedefini doğrula; migration rolüyle `psql -v ON_ERROR_STOP=1 -f deploy/Invalidate-RestoredPublicAccess.sql` çalıştır. İşlem erişim hash/sürümünü ve tüketilmiş makbuzların secret hash'ini değiştirir; randevu/audit/iptal makbuzunu silmez. Eski token ve receipt yenilemesinin 403 olduğunu sentetik kabulde doğrula; gerçek yeniden açma ayrı işletim kararıdır. Eski müşteri bağlantıları çalışmayacağından işletmeyle iletişim gerekir. Restore veri kaybı/uzlaştırma incelemesinin yerine geçmez; sırlar ve SQL çıktısı loglanmaz.

Hazırlanma: 2026-09-29. Bu dosya plandır; çalışan altyapının kanıtı değildir. Uygulandıkça gerçek komutlar, adresler ve ölçümlerle güncellenir (sırlar hariç). Kısa ilkeler AGENTS.md Bölüm 8'dedir.

## 1. Kurulum ve kapasite

P05-02 gönderim kapalı telefon doğrulaması yedekli ana yerel teslimde doğrulandı. Ana kurulumda `BookingVerification:Enabled` verilmez veya false kalır; gerçek taşıyıcı adaptörü yoktur. HMAC anahtarı ana ortama testten kopyalanmaz. Enabled fakat kullanılabilir taşıyıcı/32 bayt anahtar eksikse veya kayıtlı kota anahtar kimliği farklıysa açılış reddedilir. [Sözleşme, migration ve kabul](plans/P05.md).

Geri dönüş/restore'da önce doğrulamayı ve gönderimi kapat; in-flight işleri durdur. DB ile aynı firmaya ait Data Protection/HMAC anahtarları ve config yedeklenir. Eski kuyruk kayıtları gönderim kapalı restore edilir; otomatik SMS veya kota sıfırlaması yapılmaz. Eklemeli şema uyumlu eski imajla korunur; veri/kota varken Down reddedilir. Gerçek SMS aktivasyonunun fiyat/harcama/aktarım kapısı ayrı onaydır.

P04-09, bu kapalı ayara kamu randevu yazımını da bağlar. Migration öncesi şifreli DB/anahtar/config yedeği ve eski kolon/satır karşılaştırması gerekir. İlk Public/null aktör veya tüketim kaydından sonra P05-02 imajına otomatik dönülmez; makbuz ömrünü ve yeni kaynağı anlayan test edilmiş imaj/ileri düzeltme gerekir. Veri varken silici Down reddedilir; restore otomatik seçenek değildir ve gönderim kapalı yapılır. [P04-09 kabul/teslim ve sınırlar](plans/P04.md#p04-09--atomik-müşteri-randevu-yazımı--17102026), [ADR-007](adr/007-public-appointment-consumption.md).

- Yerelde geliştir; VPS'i pilotta aç. 4 GB deneme, Coolify+ayrı DB süreçleri için 8 GB sınıfı üretim adayıdır; kapasite garantisi değildir. Firma sayısını yük testiyle belirle: RAM/CPU/disk, DB bağlantısı, p95 gecikme, kuyruk.
- Tek Compose şablonu ve tekrar güvenli kurulum komutu: firma/domain/sırlar → servis/migration/sağlık sonucu. CPU/RAM/log/disk sınırı ve yanlış firma kaynağını silmeme kontrolü. Coolify ortak proxy ağı izolasyon kanıtı değildir.
- SSH anahtarı, firewall, güncelleme; yönetim paneline kısıtlı erişim/MFA. Docker/panel tüm hostu etkiler. Web portları açık, DB iç ağda; uptime alarmı host dışında.

## 2. Dağıtım ve yükseltme

- CI imajını digest ile test → pilot → diğerlerine sırayla dağıt. Migration başlangıçta otomatik değil; yedek sonrası tek kontrollü iş. Silici değişimde genişlet/taşı/daralt. İmaj rollback'i DB uyumu ister; restore son veriyi kaybettirebilir. Sunucuda elle kod yamalama yok.
- Filo yönetimi (P06): N firmaya toplu sürüm dağıtımı ve migration için runbook/script. Sırayla ilerler, her firmada önce yedek alır, firma başına sonuç (başarılı/başarısız/atlandı) raporlar, ilk hatada durur. Firma sayısı arttıkça elle yapılan iş burada toplanır.

## 3. Yedek ve kurtarma

- Şifreli host dışı yedek: DB, dosya, anahtar ve kurulum bilgisi; ayrı erişim anahtarı. Canlı volume kopyası tutarlı DB yedeği değildir; Coolify yedeği uygulama verisini kapsıyor varsayılmaz. İlk hedef RPO <=24 saat/RTO <=4 saat; ölçmeden sözleşmeyle garanti etme. Gerekiyorsa WAL/PITR.
- Günlük yedek/hata/disk/kuyruk kontrolü; aylık restore/güncelleme provası. Öneri 7 günlük+4 haftalık yedek; hukuki silme/saklamayla kesinleştir. Restore silinen kişiyi geri etkinleştirmesin; kurtarma anahtarı ayrıca korunmalı.

## 4. Büyüme ve taşıma

- Sürekli %70 RAM/disk ilk alarm eşiği; ölç → sorgu/indeks/iş yükü → VPS büyüt/firmayı taşı. Taşırken yazmayı durdur, son tutarlı yedeği aktar/doğrula, DNS değiştir; iki yazıcı açma.
- Tek host kesintisi tüm firmaları etkiler; ilk aşamada HA kümesi yok.

## 5. Planlanan destek ve işletim ekranı

**Karar/durum:** 02.10.2026 ürün sahibi, PowerShell gerektiren bütün ürün kurulum/bakım/destek işlemlerini ileride kendisine özel bir ekrandan yapmayı istedi. `planned`; yalnız plan kaydı onaylıdır. P06 işletim hazırlığı ve P07 satış sonrası destek kabulüne bağlanır. Şimdi kod, ekran, servis veya uzak erişim kurulmaz; ayrıntılı uygulama planı ilgili aşamada ayrı onayla hazırlanır.

**Hedef akış:** Yetkili operatör MFA ile giriş yapar, firmayı seçer, izinli işlemi açar, gerekli bilgi ve onayı verir; işlem ilerlemesi/başarı/hata/geri dönüş sonucunu görür. Rutin ürün işletiminde PowerShell veya sunucu komutu yazması gerekmez. Mevcut komutlar doğrulanmış işlemlerin altyapısı ve acil teknik müdahale yolu olarak kalabilir; destek ekranı genel terminal değildir.

### İşlem envanteri ve kapsam

| İşlem | Mevcut temel / gelecekteki hedef |
| --- | --- |
| İlk firma kurulumu ve kurulum ayarları | [Initialize-LocalEnv.ps1](../deploy/Initialize-LocalEnv.ps1); doğru firma/kurulum seçimi, ayrı sırlar ve tekrar güvenli kurulum |
| İlk Owner hesabını açma | [Bootstrap-LocalOwner.ps1](../deploy/Bootstrap-LocalOwner.ps1); yalnız boş kurulumda, mevcut hesabı yeniden oluşturmadan |
| E-postaya erişim kaybında Owner parola kurtarma | [Issue-LocalOwnerPasswordReset.ps1](../deploy/Issue-LocalOwnerPasswordReset.ps1); bağımsız sahiplik doğrulaması sonrası süreli tek kullanımlık kod |
| Telefon ve bütün MFA yedek kodları kayıpken kurtarma | [Recover-LocalOwnerMfa.ps1](../deploy/Recover-LocalOwnerMfa.ps1); bağımsız doğrulama sonrası mevcut kurallarla yeniden MFA kurulumu |
| Gönderim ayarlarını hazırlama/güncelleme ve durumu kontrol etme | [Initialize-LocalIdentityEmail.ps1](../deploy/Initialize-LocalIdentityEmail.ps1); sırları koruyan giriş/değişiklik, günlük limit ve kontrollü gönderim testi |
| Ayrı test kurulumunda posta teslimini deneme | [Initialize-LocalSmtpTest.ps1](../deploy/Initialize-LocalSmtpTest.ps1); yalnız test ortamında, ana firma/hesapla karışmadan |
| Migration ve sürüm yükseltme/geri dönüş | [Apply-LocalMigration.ps1](../deploy/Apply-LocalMigration.ps1) ve planlanan dağıtım işlemleri; yedek sonrası kontrollü iş, şema/imaj uyumluluğu |
| Sağlık, yeniden başlatma, yedek/geri yükleme ve firma taşıma | İlgili aşamada geliştirilecek işletim işlemleri; açık firma hedefi, sonuç/ilerleme ve geri dönüş kanıtı |

Envanter mevcut `deploy/*.ps1` yardımcılarının tamamını kapsar. Bunlar bugün yerel araçlardır; tabloda canlı karşılıkları yapılmış sayılmaz. İleride eklenen ürün işletim komutları da aynı ekran hedefine eklenir. Geliştirme/CI komutları müşteriye verilen destek işlevi değildir. Normal Owner e-posta sıfırlaması, Staff daveti/parola işlemleri ve işletme panelinde zaten yetkili kullanıcıya açık işlevler kendi akışlarını korur.

### Güvenlik ve kabul hedefleri

- Müşterinin Owner/Staff panelinden ayrı, yalnız yetkili operatöre açık erişim ve MFA; ortak gizli müşteri admin hesabı veya firma izolasyonunu aşan giriş yolu yok. Merkezde randevu/müşteri verisi biriktirmez; firma hedefi ve gerekli işletim referanslarıyla sınırlıdır.
- Her işlem için açık firma/hesap hedefi ve sunucu tarafı yetki kontrolü; kurtarmada bağımsız sahiplik doğrulaması ve destek referansı, yıkıcı işlemde etki/geri dönüş bilgisi ve açık onay. Sadece onay kutusu sahiplik kanıtı sayılmaz.
- Yalnız önceden tanımlı eylemler; kullanıcı girdisini shell komutu yapma, serbest PowerShell/terminal/SQL çalıştırma yok. Müşteri uygulamasına Docker socket veya diğer firmaların sır/volume erişimi verilmez. Operatör yürütme yöntemi ilgili aşamada seçilir.
- Parola operatör tarafından belirlenmez; sıfırlama kodu yalnız yetkili kişiye sınırlı gösterilir/teslim edilir. Parola/token/SMTP sırrı log, işlem kaydı veya kalıcı tarayıcı deposunda tutulmaz; yeni kod gösterimi süreli ve tek kullanımlık kuralları değiştirmez.
- Kim/ne/zaman/hangi firma/sonuç kaydı, tekrar/çift tıklama koruması, sonlu işlem süresi ve kesinti sonrası durum takibi. Hata/belirsiz sonuç başarı olarak gösterilmez; kontrolsüz yeniden deneme yok.
- Her eylem kabulünde PowerShell açmadan ekran üzerinden uçtan uca işlem; anonim/yetkisiz/yanlış firma reddi, sır görünmezliği ve gerekli hata/rollback/restart/izolasyon kontrolleri. Yedek/geri yükleme/taşıma için ayrıca gerçek sentetik işletim kanıtı gerekir.

Planın yazılması ekranın veya üretim işletiminin hazır olduğu anlamına gelmez. Özellikler küçük onaylı işlere bölünür; mevcut P02 işleri ve aşama sırası bu kayıtla değiştirilmez.
