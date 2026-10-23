# ADR-010 — Randevu başına tek hatırlatma ve ortak SMS bütçesi

Durum: kabul edildi; P05-08 uygulama onayı 23.10.2026. Gerçek SMS/hesap/ödeme kapalıdır; [kapsam ve kabul](../plans/P05.md#p05-07--gönderim-kapalı-tek-hatırlatma-planı--23102026).

İhtiyaç: onaylı randevudan üç saat önce bir hatırlatma; taşıma/iptal, restart veya OTP'nin ayrı harcaması ikinci hatırlatma ya da ayrı bütçe yaratmamalıdır.

Karar: randevu transaction'ında benzersiz AppointmentId ile ayrı kalıcı iş; ilk denemeye kadar aynı satır taşınır. İlk çağrıyı başlatma anı ve deneme sayısı geriye alınamaz; terminal satır randevu yaşadığı sürece tekrar önleme kaydıdır. Alıcı şifreli, terminal durumda temizlenir. Owner'ın tek yönlü engellemesi aynı kayıtta aktör/zamanla korunur; ilk dilimde yeni UI yoktur.

OTP adaptörü altı rakam içindir. Hatırlatma ayrı dar taşıyıcı arayüzü ve ürün DI'da kapalı taşıyıcı kullanır; sahte yalnız testtedir. Mevcut HMAC/KeyId bağı OTP açılmadan da okunabilir, ancak biçimi değişmez. Bütçe denemelerindeki değiştirilemez Purpose, OTP ve hatırlatma bakımını ayırır; adet/TRY sayaçları ortaktır. Hatırlatma bütçesi planlandığı gün değil çağrı öncesi kısa transaction'da ayrılır. İşin bütçe onay kimliği sessizce yenilenmez.

Worker randevu → SMS advisory → iş sırasıyla güncel sürüm/durum/engelleme/pencereyi denetler; claim kilidi önceki transaction'da bırakılır. Started commit'inden sonra dış çağrı açık DB transaction'ı dışında yapılır. Sonraki iptal uçuşa geçmiş SMS'i geri alamaz; Unknown kör tekrar edilmez. Kesin geçici hata aynı hedef/pencere içinde toplam üç çağrıya kadar denenebilir. Her çağrı bütçe tüketir.

Bedel: üç saatten geç onaylanan veya 15 dakikalık pencereyi kaçıran iş gönderilmez; ilk çağrıdan sonraki taşıma yeni hatırlatma oluşturmaz. Tek SMS mantıksal bir iştir, dış serviste exactly-once teslim garantisi değildir. Sonlanmış kaydın saklama/silmesi randevu yaşam döngüsüyle birlikte yönetilmelidir. Gerçek normal SMS adaptörü, fiyat/segment kabulü ve kullanılabilir ret arayüzü ayrı pilot kapılarıdır.

Alternatif: tek OTP kuyruğuna serbest metin eklemek OTP sözleşmesini genişletir; bağımsız hatırlatma bütçesi tavanı katlar; terminal satırı 24 saatte silmek taşıma sonrası ikinci gönderime izin verir. Bu dilimde bunlar seçilmedi.
