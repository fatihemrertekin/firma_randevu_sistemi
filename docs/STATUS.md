# STATUS — Güncel durum ve sıradaki iş

Güncelleme: 2026-09-30

## Aşamalar

| Aşama | Durum | Kanıt / engel |
| --- | --- | --- |
| P00 — İş ve ürün tanımı | `done` | [P00 planı](plans/P00.md), [ürün kararları](PRODUCT.md#2-ürün-kararları), kapsam/politika ve belge kontrolü tamam. Bu yalnız planlama kanıtıdır. |
| P01 — Temel ve CI | `planned` | Başlanmadı; [ROADMAP](ROADMAP.md) gereği kullanıcı geçiş onayı bekleniyor. Sürüm/lisans doğrulaması, ADR-001 ve çalışan proje henüz yok. |
| P02–P17 | `planned` | Yalnız [ROADMAP](ROADMAP.md) düzeyinde; uygulama kanıtı yok. |

## Bu oturumda doğrulananlar

- Başlangıçta klasörde yalnız `AGENTS.md`, `PRODUCT.md`, `ROADMAP.md`, `OPERATIONS.md` vardı; `docs/STATUS.md` ve uygulama kodu yoktu.
- Klasör Git deposu değildi (`git status` sonucu: `fatal: not a git repository`). Depo başlatma ve çalışan kurulum P01 temel işidir.
- Üç mevcut ürün/işletim belgesi `docs/` altına tek kopya olarak taşındı. P00 akış, kabul ve görüşme taslağı [planda](plans/P00.md).
- Kod, otomatik test, CI, Compose, deploy, gerçek görüşme, ödeme ve restore çalıştırılmadı.
- 30.09.2026 ürün sahibi yanıtları [PRODUCT §2](PRODUCT.md#2-ürün-kararları) içine işlendi. AI ve WhatsApp ilk sürüm dışında; SMS hatırlatma tercih edildi. İşletme görüşmelerini ürün pilot için hazır olduğunda ürün sahibi yapacak.
- Pilot için SMS OTP, onaylı randevuya tek SMS hatırlatma, gerektiğinde manuel onay, CSV veri ihracı ve B2B abonelik dönem/ödeme kaydı kabul edildi. Firma sahibi müşteri iptal sınırını panelden belirleyecek; Owner/yetkili Staff gelmemeyi işaretleyebilecek. Destek Pazartesi–Cumartesi 08:00–19:00 (`Europe/Istanbul`), e-posta ve telefonla verilecek; adres/numara pilot hazırlığında belirlenecek.
- Manuel onay bekleyen `Pending` istek, 30 dakikada onaylanmazsa otomatik iptal edilir ve blokladığı saat yeniden açılır.
- Gerekli belgelerin varlığı ve yerel Markdown dosya bağlantıları kontrol edildi; eksik dosya veya kırık yerel bağlantı görülmedi. P00-01–P00-05 görevleri [planda](plans/P00.md#küçük-görevler-ve-kanıt) `done`.

## İleride netleşecek ayrıntılar

- Hedef pilot tarihi verilmedi; P01 ADR-001 teslim hızı/öğrenme bedelini yazarken somut takvim ancak tarihle değerlendirilebilir. Bu, P00 kapanışını engellemez.
- Destek e-posta adresi/telefon numarası pilot hazırlığında belirlenecek; ilk yanıt süresi taahhüdü yok. Gerçek tarife ve mesaj kotası ilgili aşamalarda ölçülecek.

## Sıradaki iş

[ROADMAP](ROADMAP.md) gereği kullanıcıdan P01 geçiş onayı iste. Onaydan sonra yalnız aktif P01 planını ayrıntılandır; ilk iş olarak desteklenen kararlı sürüm/lisans uyumunu resmî kaynaklardan doğrula, ADR-001'de teknoloji seçiminin öğrenme ve bakım bedelini yaz, ardından küçük çalışan temel kurulumunu başlat. Onay gelmeden P01'e geçme.
