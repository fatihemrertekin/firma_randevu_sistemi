# STATUS — Güncel durum ve sıradaki iş

**P04-44 ilk sürüm gereksinimlerinin dar değerlendirmesi `done` (29.10.2026).** [Kaynak/11 mevcut kapanış özeti, tamamlananlar ve açık kapılar](plans/P04.md#p04-44--ilk-sürümün-zorunlu-eksiklerinin-kaynak-ve-kanıt-değerlendirmesi--29102026). Owner günlük/haftalık/formdan taşıma; misafir kayıt/sonuç/iptal, politika ayarları, kimlik/yönetim ve kapalı OTP/hatırlatma/ret/normal SMS mevcut. Motor/misafir ve DI taşıyıcısı için bayat üst belge ifadeleri düzeltildi; gerçek SMS/erişim kapıları açılmadı.

**Tek sonraki aday P04-45: MFA Owner için mevcut randevu kayıtlarının güvenli CSV ihracı** (`planned`, başlamadı). P00'dan beri pilot gereksinimi; kaynakta endpoint/arayüz yok. Sonlu tarih/satır, izinli snapshot alanları, formül enjeksiyonu/UTF-8, yetki/yazmama ve mevcut Randevular eylemi somutlaştırılacak; yeni ekran gerekirse önce görsel onay. Bu oturumda ihracat kodlanmadı.

Diğer açık pilot kapıları: B2B abonelik dönem/ödeme/erişim kaydı, P06 kurulum/izolasyon/host dışı yedek-restore/HTTPS/izleme ve ticari/mahremiyet hazırlığı. Eski yerel restore/şifreli yedek geri okuma canlı RPO/RTO garantisi değildir. Aylık görünüm ve müşteri kartı/geçmişi kapsam dışı; sürükleme/ayrı özet ihtiyaç doğrulanmadan başlamaz. [Gerçek SMS proje sonu kararı](plans/P05.md#ücretli-gerçek-sms-kabulünün-proje-sonuna-ertelenmesi--20102026) korunur.

Yalnız beş belge değişti; kullanıcı16/16 P04 sıralama farkı PR dışında. Kendi son head/main CI ve aynı ürün/CI/imaj/8080 sağlık eşitliği kapanışta doğrulanır; uygulama/DB/test/build/deploy/yeni görüntü tekrarı yok. Genel P04/P05/WCAG/pilot kapanışı değildir. Önceki durum [Git arşivinde](https://github.com/fatihemrertekin/firma_randevu_sistemi/blob/a7bf0bbd3149cdf1f88b3119496149e165291ca7/docs/STATUS.md).
