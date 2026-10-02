# ADR-002 — Kimlik e-postası için SMTP adaptörü

Tarih: 02.10.2026. Durum: kabul edildi; P02-14 kapsamı ürün sahibince onaylandı.

İhtiyaç: Owner hesap e-postasını kendisi doğrulayabilsin ve destekten kod istemeden parola yenileme bağlantısı alabilsin. Identity tokenları, MFA ve mevcut kalıcı sıfırlama işi korunmalıdır.

Alternatifler: özel sağlayıcı HTTP SDK'sı bakım/sağlayıcı bağımlılığı yaratır; uygulamanın doğrudan Internet'e posta sunucusu olarak çıkması DNS, itibar ve işletim yükü getirir; yerel dosya teslimi gerçek kullanıcıya ulaşmaz.

Karar: MailKit üzerinden güvenli SMTP adaptörü kullanılır. Doğrulama ve sıfırlama mevcut dar teslim arayüzlerinden geçer; MailKit taşıma sınırında kalır. İlk gerçek kabul ayrı Gmail test hesabıyladır. Sertifika kontrolü ve zorunlu TLS, sabit origin, Development alıcı listesi, kalıcı atomik günlük deneme sınırı uygulanır. Sıfırlama işi mevcut DB worker'ında sonlu retry yapar; doğrulama hatasında token iptal edilir ve kullanıcı tekrar ister.

Bedel: SMTP kabulü gelen kutusu teslimi değildir; spam/ret ve belirsiz timeout tekrarları mümkündür. MailKit/MimeKit ve kriptografi bağımlılıkları güncel tutulur, MIT bildirimleri imaja eklenir. Ücretsiz SMTP hesabı sınırsız değildir; ortak gönderen hesabının toplam kotası ayrıca yönetilir. Üretim domain doğrulaması, DNS, teslim izleme ve hukuki aktarım değerlendirmesi P06 kabulünü bekler; bu karar Staff/davet otomasyonu veya yeni mesaj kanalı yetkisi vermez.
