# ADR-009 — Gönderim denemesinden önce kalıcı SMS bütçesi

Durum: kabul edildi; P05-04 uygulama onayı 20.10.2026. Gerçek SMS, hesap ve ödeme yetkisi verilmedi. Kabul/teslim [P05](../plans/P05.md) ve [STATUS](../STATUS.md) içindedir.

İhtiyaç: telefon/IP kotası para sınırı değildir. Retry, restart, iki worker ve belirsiz sağlayıcı yanıtı aynı bakiyeyi tekrar harcayabilir; DB restore'u dışarıdaki tüketimi geri almaz.

Karar: firma/HMAC kimliğiyle bağlı, en çok 31 günlük salt okunur `BookingSmsBudgetPolicies` onayı ve iş/deneme numarasıyla tekil `BookingSmsAttempts` rezervasyonu kullan. İlk rezervasyon challenge, kuyruk ve kota ile aynı transaction'dadır. Worker her çağrıdan önce mevcut OTP advisory kilidi altında günlük, takvim ayı ve onay toplamı adet/TRY sınırını yeniden doğrular. Gün/ay Europe/Istanbul'dur. Gün/ay tüketimi yeni onayla sıfırlanmaz. İlk rezervasyon çağrı gününe yeniden ayrılır; retry ayrı rezervasyondur. Tutar `numeric(10,2)`; bütün başlatılmış denemeler, kesin ret dahil, ihtiyatlı azami segment bedeliyle sayılır. Yalnız hiç başlamamış iptal/sona ermiş iş serbest bırakılır.

Onay üst sınırları 20/gün, 200/ay ve toplam 200 çağrı; 10 TRY/gün, 100 TRY/ay ve toplam 100 TRY; segment rezervasyonu en fazla 0,50 TRY'dir. Daha düşük sınırlar mümkündür. Uygulama rolü onay yaratamaz/değiştiremez; rezervasyonun iş/numara/onay/bedel kolonlarını güncelleyemez. Onay veya deneme varken Down reddedilir. Onay otomatik seed edilmez. Operatör sağlayıcı fiyatı ve bakiye uzlaştırmasını tamamlayıp ayrı aktivasyon onayı aldıktan sonra migration yetkisiyle yeni süreli onay yazar; bu teslim bunu yapmaz.

Netgsm için dar `HttpClient` adaptörü sabit HTTPS OTP POST, Basic Auth, tek TR numara, sabit ASCII tek segment ve sonlu timeout kullanır. Redirect ve otomatik HTTP retry kapalı; HTTP loglayıcıları kaldırılır. Sadece `00` ve geçerli string `jobid` kabul sayılır. Kesin hatalar ret; timeout, 429/5xx, bozuk/eksik yanıt ve lease kaybı `Unknown` kalır. Kabul teslim kanıtı değildir. Referans deneme satırında tutulur; OTP/telefon/gövde yoktur. Bilinen referanslar yetkili operatörün sağlayıcı POST raporu/bakiye kontrolü için kullanılır. Bu dilimde otomatik rapor worker'ı veya kamu webhook'u yoktur.

`BookingSms:DispatchEnabled=false` yeni request/resend ve worker gönderimini keser; doğrulanmış challenge'ın randevu/erişim akışı kendi mevcut kapıları altında devam eder. Ana teslimde Netgsm, OTP, dispatch ve müşteri erişimi kapalıdır. Gönderim açılırken geçerli onay ve fiyat bağı startup'ta, her rezervasyonda da tekrar kontrol edilir.

Bedel: başlatılmış fakat kesin reddedilmiş çağrılar da bütçeyi tutar; sağlayıcı bakiyesiyle aynı muhasebe olduğu iddia edilmez. Deneme kayıtları işin 24 saatlik temizliğinden bağımsız, en az 30 gün, ilgili ayın sonundan ve onayın bitişinden sonra korunur; işlem bakiyesi değil ihtiyatlı harcama tavanıdır. Firma başına düşük pilot hacminde mevcut tek kilit yeterlidir. Yeni kuyruk, SDK veya bağımlılık yoktur.

Restore uygulama/gönderim kapalı yapılır; [Invalidate-RestoredPublicAccess.sql](../../deploy/Invalidate-RestoredPublicAccess.sql) eski onayları da iptal eder. Sağlayıcı raporu/bakiyesi ve restore sonrasında kaybolan denemeler operatörce uzlaştırılmadan yeni onay verilmez. Prosedür atlanan ham restore'u uygulamanın kendiliğinden algıladığı iddia edilmez. Gönderim kapalı önceki imajla eklemeli şema uyumu ayrıca kanıtlanır; gerçek gönderim sonrası eski bütçesiz imajla gönderim açılmaz.

Alternatifler: yalnız config/sayaç restart veya restore'da bütçe kanıtı sağlamaz. Sağlayıcı exactly-once/idempotency garantisi doğrulanmadığından timeout tekrarına güvenilmez. Kredi/muhasebe sistemi, ikinci sağlayıcı ve otomatik uzlaştırma bu dar pilot için eklenmez.
