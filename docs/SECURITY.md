# Güvenlik — uygulama kapsamı ve sınırlar

Bu belge uygunluk sertifikası değildir. Genel yetki/izolasyon/sır sözleşmesi kök [AGENTS](../AGENTS.md) içindedir; gerçek kabul [STATUS](STATUS.md) ve ilgili aşama planlarında izlenir.

## P05-02 telefon doğrulaması

Gönderim kapalı P05-02 geliştirme/GitHub/ana yerel kabulü `done`; [P05](plans/P05.md) ve [ADR-006](adr/006-booking-phone-verification.md) güncel sözleşmedir. Anonim üç POST aynı origin ve CSRF, 4 KiB gövde sınırı ve ayrı IP limiti taşır. Ana ortamda kapalı özellik geçerli güvenlik isteğine 503 döner; challenge/kota/iş yaratılmaz. Gerçek SMS adaptörü veya test kodunu kamuya açıklayan uç yoktur.

Telefon/ad, Identity hesabı veya müşteri geçmişi bulmak/birleştirmek için kullanılmaz. OTP yalnız tek talebin numarasına erişimi sınar; Owner MFA/oturum yerine geçmez ve randevu/blok yaratmaz. Secret/kod/gövde/telefon/IP log, URL veya kalıcı tarayıcı deposuna alınmaz. Kota HMAC'leri anonim veri sayılmaz.

`BookingVerification:HmacKey`, repo/imaj dışında 32 bayt Base64 sırdır; yalnız doğrulama açılacaksa gereklidir. Anahtar restartta üretilmez veya otomatik döndürülmez; kota anahtar kimliği açılışta ve her OTP kilidinde doğrulanır. Firma InstanceId, Data Protection amaçları ve kalıcı anahtarlar ayrıdır. Yedek DB, bu anahtarlar ve config'i birlikte kapsar; test anahtarı/sahtesi ana ortama taşınmaz.

Kota/retention/yarış ve restore kapısı P05'te tutulur. Timeout/lease kaybı kör retry üretmez. Sağlayıcı, bütçe ve aktarım zinciri ayrıca incelenmeden gerçek SMS yetkisi doğmaz.
