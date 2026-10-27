# STATUS — Güncel durum ve sıradaki iş

**P04-22 kimlik eksik-kanıt incelemesi `done` (27.10.2026); kimlik dar kabulü tek P2 nedeniyle açık.** [37 maskeli kare, 64+1 ölçüm, gerçek akış ve bulgu](plans/P04.md#p04-22--kimlik-eksik-görüntü-klavye-ve-gerçek-api-teyidi--27102026). Yeni ürün düzeltmesi bu işte yapılmadı; belge PR'ının kendi son head/main CI'ı ve değişmeyen kaynak/imaj/sağlık ayrıca doğrulanır.

**Tek sonraki iş: P04-23 uzun busy etiketi kırılması — P04-22-01** (`planned`, başlamadı). 320 px/%200 root metinde “Parola sıfırlanıyor…” yazısı 104px eşit düğmeye sığmıyor. Kullanıcının input hizalı iki sütunu korunarak dar düzeltme/teyit yapılacak.

Gerçek MFA kurulum/e-posta onayı/outbox-link reset köprüsü ve ayrı eski oturumun401 olması geçti; yeni parola hâlâ MFA ister. Eski 28 kare korundu. A/B/C sentetik hedefler durmuş, volume/anahtarlar korunmuş; gerçek e-posta/SMS ve ana hesapla giriş/POST yok.

Ürün/CI/test-plan ve çalışan 8080 imajı aynı, live/ready200. Eski birim/DB paketleri ve imaj/deploy/yedek tekrarlanmadı; kullanıcı P04 farkı PR dışıdır. P04-20 misafir/P04-15 Owner kabulü korunur; genel UI/P04/P05/WCAG/native finish/gerçek SMS/pilot kapanışı yok.

Önceki durum özeti [sabit Git arşivinde](https://github.com/fatihemrertekin/firma_randevu_sistemi/blob/8c94648efe9911e2f3d115da2ce2e8dd02a49d90/docs/STATUS.md) korunur. Ayrıntı ilgili aşama planındadır; [ücretli gerçek kabul proje sonunda](plans/P05.md#ücretli-gerçek-sms-kabulünün-proje-sonuna-ertelenmesi--20102026).
