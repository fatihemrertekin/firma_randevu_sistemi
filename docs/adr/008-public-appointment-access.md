# ADR-008 — Tek randevuya müşteri erişimi ve iptal

Durum: kabul edildi; P04-10 uygulama onayı 17.10.2026. Gerçek kabul/teslim [P04](../plans/P04.md#p04-10--sonuç-ve-müşteri-iptal-yetkisi-dar-planı--17102026) ve [STATUS](../STATUS.md) üzerinden izlenir.

İhtiyaç: Public create'in 24 saatlik başarılı işlem makbuzu genel müşteri yetkisi değildir. Müşteri yalnız kendi randevusunu görebilmeli ve kayıt anındaki iptal koşulunu uygulayabilmelidir; telefon/Owner oturumu erişim sağlamaz.

Karar: makbuzdan ayrı POST ile 256 bit CSPRNG bearer token üret, yalnız SHA-256 hash'ini tek randevulu `PublicAppointmentAccesses` satırında tut. İlk bitiş `min(firstIssuedAt + 31 gün, o anki startsAt + 24 saat)`; tam son anda yetki biter. İlk üretim bir kezdir; yanıt kaybında geçerli makbuzla açık rotasyon eski hash'i değiştirir. Paralel rotasyonlarda son commit geçerlidir; bütün yanıtların geçerli kalması vaat edilmez. İlk bitiş ve tüketilmiş iptal makbuzu korunur.

GET salt okunur; aynı sorgu snapshot'ında token ve Public sonuç okunur. POST iptal personel → randevu → erişim kilitleri altında sürüm, Pending/Confirmed ve kayıtlı dakika sınırını yeniden kontrol eder. Durum/sürüm/PublicCancelled audit/komut makbuzu atomiktir. Aynı başarılı komutun tekrarı yeni yan etki oluşturmaz; token'ın okuma hakkı sabit sona kadar sürer. Üretim/rotasyon randevu → erişim → OTP advisory sırasındadır; ardından personel/politika kilidi alınmaz. Dış çağrı transaction'da yoktur.

Alternatifler: UUID/telefonla okuma nesne yetkisi sağlamaz. Create makbuzunu uzun erişim yapmak OTP saklama/temizlik ömrünü uzatır. Eski token'ı yeniden döndürmek düz/şifreli sır saklamayı gerektirir. Ayrı komut tablosu tek iptal hakkı için gereksizdir; erişim satırındaki tek makbuz yeterlidir. JWT yeni bir doğrulama/iptal modeli gerektirdiği için seçilmedi.

Bedel: kayıp token yalnız ilk 24 saatlik makbuzla yenilenebilir; sonra işletmeyle iletişim gerekir. Taşıma erişim süresini uzatmaz. Bearer yetkisini ele geçiren kişi o randevuyu okuyabilir ve izin verilen zamanda iptal edebilir; URL/query/cookie/log yerine Authorization başlığı, no-store/no-referrer ve aynı Origin+CSRF uygulanır. Yeni anahtar, kişi verisi kopyası, bağımlılık veya SMS gönderimi yoktur.

`PublicBooking:AccessEnabled` varsayılan false'tur; GET/iptal bu bağımsız kapıya bağlıdır. Yeni token ayrıca OTP'nin açık olmasını gerektirir. Böylece OTP gönderimini kapatmak mevcut token ile iptali kesmez. Ana teslimde ikisi kapalı kalır. Erişim satırı randevu/iptal makbuzu kadar saklanır; bitmiş hash yetki sağlamaz, periyodik yeni temizlik işçisi yoktur. Bu kayıtların kişisel veriden bağımsız olduğu iddia edilmez; randevu saklama/silme kapsamı pilot öncesi tamamlanmalıdır.

Restore kapılar kapalı yapılır; yeniden açmadan önce [Invalidate-RestoredPublicAccess.sql](../../deploy/Invalidate-RestoredPublicAccess.sql) migration rolüyle bütün eski erişim hash'lerini ve tüketilmiş makbuz secret hash'lerini geçersizleştirir. İptal/audit/randevu korunur. Restore sonradan kaydedilmiş randevu/iptali kaybettirebilir; otomatik geri dönüş değildir. Erişim/iptal tarihçesi varken Down reddedilir; sonraki geri dönüş bu şemayı ve tüketimi bilen test edilmiş imaj/ileri düzeltmedir.
