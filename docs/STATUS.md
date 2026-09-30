# STATUS — Güncel durum ve sıradaki iş

Güncelleme: 2026-09-30

## Aşamalar

| Aşama | Durum | Kanıt / engel |
| --- | --- | --- |
| P00 — İş ve ürün tanımı | `done` | [P00 planı](plans/P00.md), [ürün kararları](PRODUCT.md#2-ürün-kararları), kapsam/politika ve belge kontrolü tamam. Bu yalnız planlama kanıtıdır. |
| P01 — Temel ve CI | `done` | Yerel kontroller ve [P01 kanıtı](plans/P01.md#kanıt) tamam. Kullanıcının GitHub ekran görüntüsünde `320401f` ve `5b36130` için CI #1/#2 yeşil. |
| P02–P17 | `planned` | Yalnız [ROADMAP](ROADMAP.md) düzeyinde; uygulama kanıtı yok. |

## P00'da doğrulananlar

- Başlangıçta klasörde yalnız `AGENTS.md`, `PRODUCT.md`, `ROADMAP.md`, `OPERATIONS.md` vardı; `docs/STATUS.md` ve uygulama kodu yoktu.
- Klasör P00 sırasında Git deposu değildi (`git status` sonucu: `fatal: not a git repository`). Kullanıcı daha sonra depoyu GitHub'a yükledi; P01 başında `main...origin/main` temizdi.
- Üç mevcut ürün/işletim belgesi `docs/` altına tek kopya olarak taşındı. P00 akış, kabul ve görüşme taslağı [planda](plans/P00.md).
- P00 sırasında kod, otomatik test, CI, Compose, deploy, gerçek görüşme, ödeme ve restore çalıştırılmadı.
- 30.09.2026 ürün sahibi yanıtları [PRODUCT §2](PRODUCT.md#2-ürün-kararları) içine işlendi. AI ve WhatsApp ilk sürüm dışında; SMS hatırlatma tercih edildi. İşletme görüşmelerini ürün pilot için hazır olduğunda ürün sahibi yapacak.
- Pilot için SMS OTP, onaylı randevuya tek SMS hatırlatma, gerektiğinde manuel onay, CSV veri ihracı ve B2B abonelik dönem/ödeme kaydı kabul edildi. Firma sahibi müşteri iptal sınırını panelden belirleyecek; Owner/yetkili Staff gelmemeyi işaretleyebilecek. Destek Pazartesi–Cumartesi 08:00–19:00 (`Europe/Istanbul`), e-posta ve telefonla verilecek; adres/numara pilot hazırlığında belirlenecek.
- Manuel onay bekleyen `Pending` istek, 30 dakikada onaylanmazsa otomatik iptal edilir ve blokladığı saat yeniden açılır.
- Gerekli belgelerin varlığı ve yerel Markdown dosya bağlantıları kontrol edildi; eksik dosya veya kırık yerel bağlantı görülmedi. P00-01–P00-05 görevleri [planda](plans/P00.md#küçük-görevler-ve-kanıt) `done`.

## P01'de doğrulananlar

- SDK, PostgreSQL, Node ve temel lisans seçimi resmi kaynaklarla [ADR-001](adr/001-teknoloji-secimi.md) içinde kaydedildi. SDK, paketler ve imajlar belirli sürüm/tag'lere sabitlendi; NuGet/npm lock dosyaları var.
- `dotnet restore --locked-mode`, derleme (0 uyarı), 3 sunucu testi ve `dotnet format --verify-no-changes` geçti. Web tarafında `npm ci`, tip, lint, 1 test, build ve `npm audit --audit-level=high` (0 bulgu) geçti.
- Docker Compose `config` ve `up -d --build --wait` geçti. Yerel `/health/live`, `/health/ready` ve ana sayfa HTTP 200 döndü; uygulama kullanıcı kimliği 1654, DB uygulama rolü superuser değil. Sentetik kayıt PostgreSQL yeniden başlatıldıktan sonra okundu ve test tablosu kaldırıldı.
- [CI iş akışı](../.github/workflows/ci.yml) eklendi; yereldeki eşdeğer kontroller geçti. `320401f` ve `5b36130` commit'leri `feature/p01-temel-ci` dalına gönderildi. Kullanıcının paylaştığı GitHub Actions ekranında bu iki commit için CI #1 ve #2 yeşil göründü.
- Projeye özel `.gitignore` ile `deploy/.env`, derleme çıktıları, paket önbellekleri ve yedekler dışarıda tutuluyor; örnek `.env` takip edilebilir. Gerçek veri, SMS, ödeme ve üretim dağıtımı yapılmadı.

## İleride netleşecek ayrıntılar

- Hedef pilot tarihi verilmedi; P01 ADR-001 teslim hızı/öğrenme bedelini yazarken somut takvim ancak tarihle değerlendirilebilir. Bu, P00 kapanışını engellemez.
- Destek e-posta adresi/telefon numarası pilot hazırlığında belirlenecek; ilk yanıt süresi taahhüdü yok. Gerçek tarife ve mesaj kotası ilgili aşamalarda ölçülecek.

## Sıradaki iş

[ROADMAP](ROADMAP.md) gereği P02 kimlik ve tanımlar aşamasına geçmeden kullanıcının açık onayını bekle. P01 özellik dalı henüz `main` ile birleştirilmedi; bu ayrı bir Git kararıdır.
