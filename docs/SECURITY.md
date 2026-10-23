# Güvenlik — uygulama kapsamı ve sınırlar

## Vite/Vitest güvenlik bakımı — 23.10.2026

Vite `8.3.3`, Vitest/mocker/spy `5.0.3` yamaları temiz lock kurulumunda doğrulandı; tam ve üretim npm taramaları **0 açık**. Aşağıdaki P05-08 taraması yama öncesi tarihli kayıttır. Kurulu upstream fonksiyonlarında mock kod enjeksiyonu, Windows backslash geçişi ve editor percent/CR/LF argümanları için normal girdilerle birlikte dokuz kontrol geçti; canlı exploit/shell/editor çalıştırılmadı. [Kapsam, ilgili testler ve teslim kapıları](plans/P05.md#vitevitest-güvenlik-bakımı--23102026). Üretim API/DB/kimlik ve kapalı gönderim ayarları değişmedi; NuGet kaynağı değişmediğinden önceki tarama tekrarlanmadı.

## P04-10 tek randevu erişimi

Token üretme/yenileme, GET ve müşteri iptalinin yetki/ömür/restore sözleşmesi [ADR-008](adr/008-public-appointment-access.md) içindedir. `PublicBooking:AccessEnabled` varsayılan kapalıdır; ana kurulumda açılmaz. Authorization bearer token URL/query/cookie'den alınmaz, Owner cookie'si yerine geçmez. POST aynı Origin+CSRF, 4 KiB/JSON allowlist ve mevcut ortak yazım limiti altında; GET mevcut okuma limitindedir. Kamu yanıtları hata/429 dahil no-store/no-referrer taşır. Header/gövde sırları loglanmaz.

İptal yalnız güncel sürüm ve rezervasyondaki koşulla tek transaction'da tüketilir; eski başarılı komut güvenli tekrardır. Rotasyon eski hash'i kaldırır, iptal makbuzunu yenilemez. Restore edilmiş DB yeniden açılmadan önce hem erişim hash'leri hem tüketilmiş makbuzların secret hash'leri geçersizleştirilir. [Gerçek kabul ve teslim durumu](plans/P04.md#p04-10--sonuç-ve-müşteri-iptal-yetkisi-dar-planı--17102026); yeni ekran/gerçek mesaj gönderimi ayrı kapsamdır.

Bu belge uygunluk sertifikası değildir. Genel yetki/izolasyon/sır sözleşmesi kök [AGENTS](../AGENTS.md) içindedir; gerçek kabul [STATUS](STATUS.md) ve ilgili aşama planlarında izlenir.

## P05-02 telefon doğrulaması

P05-08 hatırlatma ayrı kapalı taşıyıcı/iş ve ortak SMS bütçesi kullanır. Randevu sürümü/durumu/engelleme gönderim öncesi kilit altında denetlenir; Started sonrasında iptal uçuşa geçmiş çağrıyı geri alamaz. Şifreli alıcı terminal durumda temizlenir; tekrar önleme ve engelleme kaydı korunur. Amaç/bedel/kimlik kolonları uygulama rolünde değiştirilemez, DB trigger'ı ilk çağrı ve engellemenin geri alınmasını reddeder. Owner engelleme API'si nesne/sürüm/MFA/CSRF ve mevcut limitleri uygular. [ADR-010](adr/010-appointment-reminder.md); gerçek alıcı/ret/normal SMS sağlayıcı kabulü proje sonundaki ayrı kapılardır.

23.10.2026 P05-08 yayın taraması: NuGet ve `npm audit --omit=dev` açık sayısı 0; tam npm taraması mevcut `vite@8.3.1`, `vitest@5.0.2`/`@vitest/mocker` geliştirme zincirinde 1 yüksek/2 orta paket bildirir. Yüksek bulgu Windows'ta geliştirme sunucusunun editor çağrısı yolundadır ([sağlayıcı bildirimi](https://github.com/vitejs/launch-editor/security/advisories/GHSA-587h-7wmj-h5pm)); mocker bulguları erişilebilir dev-server'da açık `mockerPlugin`/`interceptorPlugin` kullanımına bağlıdır ([kod enjeksiyonu](https://github.com/vitest-dev/vitest/security/advisories/GHSA-qxg6-89mg-rqv8), [Windows yol kontrolü](https://github.com/vitest-dev/vitest/security/advisories/GHSA-xp26-3454-mjgp)). Repo Vite config'inde bu iki plugin çağrısı yoktur; CI `vitest run` kullanır, sunucuyu ağa açmaz. Docker son katmanı Linux ASP.NET ve derlenmiş statik dosyalardır; Node/Vite dev-server taşınmaz. Bunlardan üretim 8080'de çalıştırılabilir yol çıkmadığı kaynak incelemesi çıkarımıdır, tüm geliştirici ortamlarının güvenli olduğu iddiası değildir. Windows'ta Vite dev-server kullanımı için yüksek risk açık kalır; bu teslim bağımlılık değiştirmez. Hemen sonraki ücretsiz bakım adayı patch/lock güncellemesi ve frontend/CI kabulüdür; geliştirme sunucusu dış ağa açılmamalıdır. Ham sonuçlar `.local/p05-08/*audit.json` içindedir; sıfır toplam açık denmez.

Gönderim kapalı P05-02 geliştirme/GitHub/ana yerel kabulü `done`; [P05](plans/P05.md) ve [ADR-006](adr/006-booking-phone-verification.md) sözleşmeyi tutar. Anonim üç POST aynı origin ve CSRF, 4 KiB gövde sınırı ve ayrı IP limiti taşır. Ana ortamda kapalı özellik geçerli güvenlik isteğine 503 döner; challenge/kota/iş yaratılmaz. P05-04 Netgsm adaptörü de varsayılan kapalıdır; test kodunu kamuya açıklayan uç yoktur. [ADR-009](adr/009-booking-sms-budget.md) gönderim öncesi kalıcı adet/TRY rezervasyonunu, dar yetkileri, Unknown ve restore'da bütçe iptalini tanımlar. Gerçek gönderim ayrıca yetkilendirilir.

Telefon/ad, Identity hesabı veya müşteri geçmişi bulmak/birleştirmek için kullanılmaz. OTP yalnız tek talebin numarasına erişimi sınar; Owner MFA/oturum yerine geçmez ve randevu/blok yaratmaz. Secret/kod/gövde/telefon/IP log, URL veya kalıcı tarayıcı deposuna alınmaz. Kota HMAC'leri anonim veri sayılmaz.

`BookingVerification:HmacKey`, repo/imaj dışında 32 bayt Base64 sırdır; yalnız doğrulama açılacaksa gereklidir. Anahtar restartta üretilmez veya otomatik döndürülmez; kota anahtar kimliği açılışta ve her OTP kilidinde doğrulanır. Firma InstanceId, Data Protection amaçları ve kalıcı anahtarlar ayrıdır. Yedek DB, bu anahtarlar ve config'i birlikte kapsar; test anahtarı/sahtesi ana ortama taşınmaz.

Kota/retention/yarış ve restore kapısı P05'te tutulur. Timeout/lease kaybı kör retry üretmez. Sağlayıcı, bütçe ve aktarım zinciri ayrıca incelenmeden gerçek SMS yetkisi doğmaz.

## P04-09 müşteri randevu yazımı

Doğrulama tek başına randevu oluşturmaz; ayrı kamu POST'u aynı talebi/gizliyi, kısa doğrulama süresini ve güncel motor kurallarını transaction içinde denetler. Tüketim, Public randevu/blok/snapshot ve null aktörlü `PublicCreated` audit atomiktir. Owner oturumu doğrulama yerine geçmez. Aynı başarılı talebin makbuzla 24 saat tekrarı yalnız kendi güncel dar sonucunu verir; ad/telefon veya genel okuma/iptal yetkisi vermez. Makbuz temizlense de kalıcı Public anahtar benzersizliği korunur.

POST, doğrulamanın aynı 20/dakika IP limiti, Origin/CSRF, JSON allowlist, 4 KiB ve no-store sınırını kullanır; kapalı ana kurulumda 503/sıfır yazım. Randevu UUID'si müşteri okuma yetkisi değildir. P04-10 token/iptal veya gerçek gönderim bu kapsamda açılmaz. [Karar](adr/007-public-appointment-consumption.md), [kabul ve teslim](plans/P04.md#p04-09--atomik-müşteri-randevu-yazımı--17102026).
