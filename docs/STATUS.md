# STATUS — Güncel durum ve sıradaki iş

Güncelleme: 2026-09-30

## Aşamalar

| Aşama | Durum | Kanıt / engel |
| --- | --- | --- |
| P00 — İş ve ürün tanımı | `done` | [P00 planı](plans/P00.md), [ürün kararları](PRODUCT.md#2-ürün-kararları), kapsam/politika ve belge kontrolü tamam. Bu yalnız planlama kanıtıdır. |
| P01 — Temel ve CI | `done` | Yerel kontroller ve [P01 kanıtı](plans/P01.md#kanıt) tamam. `10527c1` (PR #1) `main` ve `origin/main` üzerinde doğrulandı. Kullanıcının GitHub ekran görüntüsünde `320401f` ve `5b36130` için CI #1/#2 yeşil. |
| P02 — Kimlik ve tanımlar | `in_progress` | P02-01–P02-04 tamamlandı; [P02 planı ve kanıtı](plans/P02.md), P02-04 için PR #3 ve başarılı push CI aşağıda. |
| P03–P17 | `planned` | Yalnız [ROADMAP](ROADMAP.md) düzeyinde; uygulama kanıtı yok. |

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
- PR #1'in birleşme commit'i `10527c1`, 30.09.2026 tarihinde yerel `main` ve izlenen `origin/main` başında görüldü. Bu kayıt yerel Git durumuna dayanır; bu oturumda GitHub'da yeniden CI çalıştırılmadı.
- Docker Compose `config` ve `up -d --build --wait` geçti. Yerel `/health/live`, `/health/ready` ve ana sayfa HTTP 200 döndü; uygulama kullanıcı kimliği 1654, DB uygulama rolü superuser değil. Sentetik kayıt PostgreSQL yeniden başlatıldıktan sonra okundu ve test tablosu kaldırıldı.
- [CI iş akışı](../.github/workflows/ci.yml) eklendi; yereldeki eşdeğer kontroller geçti. `320401f` ve `5b36130` commit'leri `feature/p01-temel-ci` dalına gönderildi. Kullanıcının paylaştığı GitHub Actions ekranında bu iki commit için CI #1 ve #2 yeşil göründü.
- Projeye özel `.gitignore` ile `deploy/.env`, derleme çıktıları, paket önbellekleri ve yedekler dışarıda tutuluyor; örnek `.env` takip edilebilir. Gerçek veri, SMS, ödeme ve üretim dağıtımı yapılmadı.

## İleride netleşecek ayrıntılar

- Hedef pilot tarihi verilmedi; P01 ADR-001 teslim hızı/öğrenme bedelini yazarken somut takvim ancak tarihle değerlendirilebilir. Bu, P00 kapanışını engellemez.
- Destek e-posta adresi/telefon numarası pilot hazırlığında belirlenecek; ilk yanıt süresi taahhüdü yok. Gerçek tarife ve mesaj kotası ilgili aşamalarda ölçülecek.

## P02-01'de doğrulananlar

- ASP.NET Core Identity ve EF Core migration'ı, tek seferlik Owner kurulumu, giriş/çıkış, Owner hesabı API'si ve web giriş ekranı eklendi. Varsayılan veya açık kayıt hesabı yok.
- Gerçek PostgreSQL testinde Owner kurulumunun tekrar reddi, anonim 401, CSRF 400, yanlış parola 401, beş hatadan sonra hesap kilidi, giriş/çıkış ve istek sınırı 429 doğrulandı. Sunucu testleri 4/4; web tip/lint/test/build geçti.
- Yerel Compose imajı derlenip sağlıklı başladı. Manuel Identity migration'ı iki kez sorunsuz uygulandı; `/health/live` ve `/health/ready` 200, anonim `/api/auth/me` 401 ve CSRF ucu 200 döndü. Uygulama rolünün tablo okuma yetkisi var, şema oluşturma yetkisi yok. Anahtar dosyası restart sonrasında korundu. Etkileşimli yerel Owner script'i çalıştırılmadı; kurulum işlevi testte doğrulandı. Uzak CI bu oturumda çalıştırılmadı.

## P02-02'de doğrulananlar

- Owner doğrulayıcı anahtarı kurulumu, TOTP ile ikinci adım, tek kullanımlık kurtarma kodları ve ilgili ekranlar eklendi. Yeni migration veya ücretli dış servis yok.
- Gerçek PostgreSQL testinde anonim kurulum 401, CSRF'siz kurulum/kod 400, yanlış parola/kod reddi, MFA sonrası parola ile yalnız geçici adım, eski oturumun iptali, TOTP ile Owner yetkisi ve kurtarma kodunun ikinci kez reddi doğrulandı. Sunucu testleri 5/5; web tip/lint/test/build ve sunucu biçim kontrolü geçti.
- Yerel Compose imajı yeniden derlenip çalıştı; sağlık uçları 200 ve anonim MFA kurulum isteği 401 döndü. Fiziksel doğrulayıcı cihazla elle test ve uzak CI bu oturumda çalıştırılmadı.

## P02-03'te doğrulananlar

- Ürün sahibi 30.09.2026 tarihinde tek küçük iş olarak yetkili Owner MFA kurtarmayı onayladı. Firma/Owner UUID ve açık onayla çalışan `recover-owner-mfa` komutu, yerel PowerShell script'i ve yalnız bu işleme özel audit tablosu eklendi. Mevcut MFA kurulum ekranı yeniden kullanılır; yeni web endpoint veya dış servis yoktur.
- Kurtarma eski doğrulayıcı/kurtarma kodlarını ve ana/geçici MFA oturumlarını geçersizleştirir. Parola, rol ve hesap kilidi korunur; yeni MFA ile giriş tamamlanmadan Owner yönetim yetkisi yoktur. İşlem kaydı ve Identity değişiklikleri aynı transaction'dadır.
- Kilitli restore, derleme (0 uyarı/0 hata), sunucu testleri 7/7, format ve PowerShell parse kontrolü geçti. İki yeni gerçek PostgreSQL testinde yanlış hedef/rol/referans/onay reddi, eski kod/oturum reddi, yeniden kurulum, audit hatasında rollback, iki paralel komuttan tek başarı ve referansın yeniden kullanımının reddi doğrulandı.
- Yerel Compose config/build/up ve migration'ın iki kez uygulanması geçti. Sağlık uçları 200, anonim `me` 401. Audit tablosunda uygulama rolünün SELECT/INSERT yetkisi var, UPDATE/DELETE/TRUNCATE yok; satır değiştirmeyen UPDATE denemesi reddedildi. İmaj içindeki komut eksik onayla çıkış kodu 1 verdi.
- Mevcut yerel Owner hesabında kurtarma yapılmadı; yerel audit satırı sayısı 0. Başarılı kurtarma komut girişi ve ekran API akışı PostgreSQL testinde çalıştı. Etkileşimli script'in başarılı uçtan uca kullanımı, fiziksel doğrulayıcı cihaz, uzak CI ve üretim dağıtımı çalıştırılmadı. Web kodu değişmedi; Compose web derlemesi geçti, ayrı web tip/lint/test kapıları yeniden çalıştırılmadı.

## GitHub işlem yetkisi ve doğrulama

- Ürün sahibi 30.09.2026 tarihinde bu depo için commit/push, PR, CI takibi ve yeşil kontrollerden sonra `main` birleştirmesini agent'a yetkilendirdi. Önceki `main` merge yasağı bu yetkiyle kaldırıldı; doğrudan `main` push yerine PR kullanılır. Yeni geliştirme işi için ayrı onay sınırı devam eder.
- GitHub hazırlığında web typecheck/lint/test (1/1)/build yeniden çalıştırıldı ve geçti. P02-01–P02-03 tek PR kapsamındadır; P02'nin tamamlandığı veya pilot yayına hazır olduğu iddia edilmez.
- P02-03 `82ced92` commit'iyle GitHub'a gönderildi ve [PR #2](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/2) açıldı. Bu commit'in [push CI koşusu](https://github.com/fatihemrertekin/firma_randevu_sistemi/actions/runs/36752334356) GitHub'da başarılı olarak doğrulandı: web/sunucu kapıları, Compose imajı ve PostgreSQL restart kanıtı geçti. Bu, yukarıdaki yerel geliştirme kayıtlarından sonra alınmış uzak CI kanıtıdır. PR'ın son commit kontrolleri yeşil olmadan merge yapılmaz.

## Sıradaki iş

30.09.2026 devam oturumunda PR #2'nin `c8dca57` ile birleştiği, son PR kontrollerinin 2/2 ve [main CI #10](https://github.com/fatihemrertekin/firma_randevu_sistemi/actions/runs/36753252586) sonucunun başarılı olduğu GitHub'da yeniden doğrulandı. Başlangıçta yerel main/origin/main/GitHub main eşit ve çalışma ağacı temizdi.

## P02-04'te doğrulananlar

- Ürün sahibi MFA Owner'ın mevcut parolayla parola değiştirmesini ve mevcut oturum dahil tüm eski ana/geçici MFA oturumlarını iptal etmeyi onayladı. Güncel `origin/main` üzerinden `codex/p02-owner-parola-degistirme` açıldı; eski `feature/p02-owner-oturum` dalından devam edilmedi.
- API ve form eklendi. Mevcut Identity şeması/politikası, CSRF, hesap kilidi ve istek limiti kullanılıyor; kullanıcı satırı transaction içinde kilitleniyor. MFA anahtarı ve kullanılmamış kurtarma kodları korunuyor. Yeni migration/ücretli servis yok; yalnız web etkileşim testleri için MIT lisanslı jsdom devDependency eklendi.
- Sunucu 11/11, web 9/9 test geçti. PostgreSQL'de yetki/CSRF/alan/parola reddi, eski ana/geçici oturum iptali, tekrar MFA, kurtarma kodunun korunması, hesap kilidi/429, commit hatasında rollback ve paralel iki istekte yalnız bir başarı doğrulandı. Web testleri bekleme/çift gönderim, hata, alan temizliği ve yeniden giriş davranışını doğruladı.
- Temiz npm ci, typecheck/lint/build; kilitli NuGet restore, derleme (0 uyarı/0 hata) ve format geçti. npm audit ve NuGet transitif bağımlılık kontrolü bulgu göstermedi. Compose config/build/up sağlıklı; live/ready 200, anonim me/change-password 401.
- Mevcut yerel Owner hesabında parola değiştirilmedi; gerçek tarayıcı/cihaz E2E ve üretim deploy yapılmadı. Ayrıntılar ve geri dönüş [P02 planında](plans/P02.md#p02-04--owner-parola-değiştirme).
- `1588dd3` ile GitHub'a gönderildi; [PR #3](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/3) açıldı. Bu commit'in [push CI koşusu](https://github.com/fatihemrertekin/firma_randevu_sistemi/actions/runs/36759215065) başarılı olarak GitHub'da doğrulandı. Bu belge kanıt commit'ini izler; PR'ın güncel son commit kontrolleri yeşil olmadan merge yapılmaz. Güncel merge/son CI durumu PR'dan doğrulanır.

## Sıradaki iş ve onay sınırı

P02-04 tamamlandı; P02 bütünü `in_progress`. Sonraki tek küçük iş adayı Owner parola sıfırlama akışının kapsamını, token teslimini ve kabul kontrollerini belirlemek; uygulamadan önce kullanıcı onayını bekle. P03'e geçme. GitHub işlemleri yeşil kontroller sonrası PR üzerinden yapılır; doğrudan main push yoktur.
