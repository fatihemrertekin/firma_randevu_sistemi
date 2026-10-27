# STATUS — Güncel durum ve sıradaki iş

**P04-26 işletme ayarları eksik-kanıt incelemesi `done` (27.10.2026); dar kabul iki P2 nedeniyle açık.** [13 yeni kare, ölçümler ve iki bulgu](plans/P04.md#p04-26--üç-işletme-ayarı-yüzeyinin-eksik-kanıt-teyidi--27102026).12 normal+3 root+6 durum ölçümü, altı klavye grubu ve üç gerçek GET beklemesi tamamlandı. Profilde Koru sonrası odak BODY'ye düşüyor; politika320 px/%200 root metinde375 px'e taşıyor. Ürün bu işte düzeltilmedi.

**Tek sonraki iş P04-27: P04-26-01 profil Koru sonrası klavye odağı dönüşü** (`planned`, başlamadı). Mevcut yenileme tetikleyicisine görünür odak dönsün; taslak/hata/busy/yazmasız davranış korunsun. P04-26-02 politika taşması ayrıca open kalır; odak düzeltmesi onu kapatmaz.

Gerçek sentetik MFA/ilk saat kaydı/alan hatası/CSRF ret→tek politika kaydı ve loading/busy kilitleri geçti;1 sentetik kullanıcı,0 randevu/gerçek mesaj. Test DB durmuş, volume/anahtarlar korunmuş. Ana Owner login/POST yok. Ürün/CI/test-plan ve8080 imajı aynı, live/ready200; yeni imaj/yedek/deploy yok. Kullanıcı P04 farkı PR dışında; kendi son belge head/main CI ve kaynak/imaj/sağlık ayrıca doğrulanır.

Owner randevu/misafir/kimlik dar sınırları korunur; genel UI/P04/P05/WCAG/native finish/pilot kapanışı yok. [Gerçek SMS proje sonunda](plans/P05.md#ücretli-gerçek-sms-kabulünün-proje-sonuna-ertelenmesi--20102026). Önceki durum özeti [sabit Git arşivinde](https://github.com/fatihemrertekin/firma_randevu_sistemi/blob/7e61ea754ac7a82296ee47f9e0a112c01704e63a/docs/STATUS.md) korunur.
