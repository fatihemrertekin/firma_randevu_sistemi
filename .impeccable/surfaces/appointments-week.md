# Haftalık randevu gezinmesi — onaylı sade yüzey

Durum: **28.10.2026 kullanıcı görsel/kod onayı alındı; uygulama P04-42 kapsamında**. Kullanıcı revize tek-gün taslağına “onaylıyorum” dedi. Önceki yedi tam listeyi uzun/karmaşık bulduğu için reddetmişti; ilk üç kayıt/max21 önerisi geçerli değildir.

## Amaç ve görsel otorite

Operate: MFA Owner haftanın günleri arasında gezinir; aynı anda yalnız seçili günün randevularını görür. Yedi günün kayıt sayısı bir bakışta görünür. Menü ve [günlük randevu yüzeyi](appointments.md) korunur; [DESIGN](../../DESIGN.md) ve mevcut tokenlar mirastır. Yeni renk/font/fotoğraf/kütüphane yoktur.

Onay kaynağı `.impeccable/mocks/decision/p04-41-week.html?rev=compact`; sabit kopyası `.local/p04-42/approved-draft.html`. Onaylı masaüstü/mobil görüntüler `.local/p04-41-revision/preview/compact-1280.png` ve `compact-390.png`; kaynak/hash kaydı `.local/p04-41-revision/final-proof.json`. Bunlar yerel taslaktır, gerçek API kabulü değildir.

## Yapı ve işlem

- Başlık/Yeni randevu → Günlük/Haftalık → hafta okları/aralık/Bu hafta/çalışan → yedi gün seçimi → seçili günün tek sayfalı listesi → yenileme/sayfalama.
- Genişte saat, müşteri/hizmet, çalışan, yazılı durum ve Ayrıntılar aynı satır; mobilde yalnız seçili günün satırları doğal biçimde kırılır. Gün düğmeleri dar alanda4+3, çok dar içerikte kayıt satırı tek sütun.44px hedef/metin küçültülmez.
- Gün seçimi aria-pressed ve yazılı başlıkla belirgindir; native Tab/Enter çalışır, yükleme sonrasında odak seçili güne döner. Gün/sayfa/çalışan/hafta URL/F5/geri-ileri ve form/ayrıntı dönüşünde korunur.
- Hafta Pazartesi–Pazar/Europe-Istanbul. Oklar seçili güne ±7 gün, Bu hafta bugüne gider. Gün/çalışan/hafta değişimi sayfayı1'e alır. Haftalık varsayılan10;10/20/50 mevcut seçenekleri. Günlük varsayılan20 değişmez.

## Veri ve durumlar

P04-42 sözleşmesi: `GET /api/appointments/week?date=...&staffMemberId=...&page=...&pageSize=...`. Aynı RepeatableRead içinde7 sayım ve seçili tek günün en fazla50 satırı; günlük endpoint ve yazma motoru korunur. DTO yalnız mevcut randevu liste alanları +hafta sınırları/gün sayımları; telefon/aktör/işlem anahtarı yoktur. Satır limiti DB'de, sıralama StartsAt/Id. Tam haftanın bütün kayıtları ekrana yığılmaz.

Yükleme/hata yeni seçimin altında eski satır/sayım veya sahte sıfır göstermez. Gerçek boş gün uygun saat garantisi değildir. Hata→Listeyi yenile,401 mevcut giriş/MFA,403 ayrı yetki,429 gerçek Retry-After. Ayrıntı/Yeni randevu mevcut formlara gider; yeni hafta üzerinde saat seçme veya sürükleme yoktur. Mevcut dirty/busy/CSRF/sürüm/idempotency kuralları değişmez.

## Kabul ve kapsam sınırı

İlgili web testleri ve gerçek PostgreSQL günlük/haftalık liste testleri; gerçek sentetik MFA Owner API/Chrome'da dört genişlik, root, odak/klavye, boş/hata/yetkisizlik/başarı ve dönüş bağlamı gerekir. Kendi son head/main CI ve yedek/veri korumalı ana8080 teslimi P04-42 kaydında kanıtlanır. Yeni şema, ay görünümü, sürükleme, müşteri geçmişi, mali özet, Staff yetkisi veya gerçek gönderim yoktur.

Uygulama/kabul/teslimin güncel durumu [P04](../../docs/plans/P04.md) ve [STATUS](../../docs/STATUS.md) içindedir. Bu brief tek başına bitmiş uygulama veya genel P04/WCAG/pilot kapanış kanıtı değildir.
