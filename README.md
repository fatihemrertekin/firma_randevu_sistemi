# Firma Randevu Sistemi

## CI çalışma düzeni

Geliştirme dalına `push` CI başlatmaz; main hedefli PR ve main'e birleşme kontrol edilir. PR'ın eski çalışması yeni sürüm gelince iptal edilir; main çalışmaları iptal edilmez. `CI / verify` kontrolü korunur. PR ve main'de yalnız işle ilgili testler çalışır; otomatik tam test çalışması yoktur. Belge/CI yardımcı değişikliklerinde yalnız yardımcı regresyonları, diff ve yerel belge bağlantıları doğrulanır.

Bağlantı kontrolü, Git dışında tutulan `.impeccable/mocks/` tasarım referanslarını sayısıyla ayrıca raporlar; bu yerel dosyalar CI veya görsel kabul kanıtı sayılmaz. Diğer eksik dosya/başlık bağlantıları kontrolü durdurur.

Ürün değişikliğinde [.github/test-plan.json](.github/test-plan.json) aynı PR'da güncellenir: `files` değişen ürün dosyalarının tam listesi; `reason` etkilenen çağıranlar ve seçimin gerekçesi; `web` Web klasörüne göre mevcut test dosyaları; `server` tam sunucu test sınıf adları; `compose` dağıtım kabulü gerekip gerekmediğidir. Örnek seçimler: `src/features/services/Services.test.tsx`, `Server.Tests.AppointmentReminderTests`. CI dosya listesini gerçek diff ile eşleştirir; eksik/eski plan, geçersiz test veya belirlenemeyen kapsamda durur. Değişen test dosyaları seçime dahil edilmelidir. Ortak DB, kimlik, başlangıç veya bağımlılık değişikliğinde etkilenen daha geniş grup gerekçesiyle açıkça listelenir; `*` kabul edilmez. Test kapsamının yeterliliği kod incelemesinde değerlendirilir.

Yerel CI yardımcı kontrolleri: `python -m unittest discover -s .github/scripts -p "test_*.py"` ve PowerShell 7 ile `pwsh -NoProfile -File .github/scripts/Validate-Documentation.ps1`. Seçili web testi: Web dizininde `npm.cmd test -- src/app/money.test.tsx`; seçili sunucu sınıfı: derleme sonrası kökte `dotnet run --project tests/Server.Tests/Server.Tests.csproj --no-build -- -class Server.Tests.HealthEndpointsTests`. Web kontrolü yalnız seçildiyse typecheck/lint/build, sunucu kontrolü yalnız seçildiyse restore/build/format da çalışır. Sunucu HTTP fikstürleri statik sayfaları da kullandığından sunucu testinden önce web çıktısı derlenir; bu işlem web testlerini başlatmaz. Geliştirmede yeni değişiklik/hata yoksa başarılı kontrolleri tekrarlama; CI takibinde özet/hata çıktısını kullan. Yalnız CI/belge değişmiş ve ürün kaynağı eşitse 8080 imajını yeniden üretme; eşitlik/sağlık kanıtını kaydet.

## Misafir rezervasyonu

20.10.2026 UI/API entegrasyonu [UI PR #121](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/121) ve [tekrar düzeltmesi PR #122](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/122) ile main'e teslim edildi; exact-head ve birleşme CI 424 web/407 sunucu testiyle başarılı. Ana 8080 sağlık/tarayıcı kabulü ve 51 tablonun veri/anahtar/ayar koruması geçti. Ana OTP/erişim ve gerçek SMS kapalıdır; [kanıt/geri dönüş](docs/plans/P04.md#misafir-ekranı-kabul-ve-ana-8080-teslimi--20102026).

`/randevu`, işletme oturumu gerektirmeyen hizmet → çalışan → saat → iletişim → telefon doğrulama/son kontrol akışıdır. Kod doğrulaması saat tutmaz; ayrı gönderim mevcut API ile tek talep oluşturur. Sunucunun Pending/Confirmed sonucu gösterilir. `/randevu/sonuc/{id}#erisim=<yetki>` yalnız o randevunun sonucunu ve süre uygunsa açık onaylı iptalini açar; fragment router/API'den önce temizlenir, yetki yalnız bellekte Bearer olarak kullanılır. Sayfa yenilenirse kaydedilen özgün bağlantı yeniden açılmalıdır. Telefonla geçmiş araması veya müşteri üyeliği yoktur.

Ana ortamda OTP/erişim ve gerçek SMS kapalı tutulur; katalog okunması SMS veya rezervasyonun etkin olduğu anlamına gelmez. Kapalı işlem 503 ile açıklanır; sağlayıcı, harcama veya pilot aktivasyonu bu UI teslimine dahil değildir. [Kapsam ve kabul](docs/plans/P04.md#misafir-ekranı--güncellenmiş-kapsam-ve-durum-taslağı--18102026).

Frontend kapıları kök komutlarla çalışır. İzole gerçek PostgreSQL/Kestrel ve sahte SMS kontrol sunucusu hazırken `node tests/Web.E2E/public-booking.cjs` dört genişlikte oluşturma, Pending/Confirmed, tek iptal, yanıt kaybı ve yetkisiz bağlantıyı kontrol eder. Varsayılan test portları 8111/8112; gerekirse `PUBLIC_BOOKING_TEST_ORIGIN`, `PUBLIC_BOOKING_SMS_TEST_ORIGIN` ve `PUBLIC_BOOKING_EVIDENCE` ortam değişkenlerini kullanın. Yalnız loopback sentetik hedefler kabul edilir; ana 8080 yazma hedefi olarak reddedilir. Test kontrol sunucusu gerçek ortam/ürün özelliği değildir.

P04-10 geliştirme/GitHub/ana yerel teslimi tamamlandı: başarılı Public makbuzuyla `POST /api/public-booking/appointments/{id}/access` (`challengeId`, `challengeSecret`, `replaceExisting`); ayrı bearer token ile `GET /api/public-booking/appointments/{id}` ve `POST /api/public-booking/appointments/{id}/cancel` (`version`, `requestKey`). POST ayrıca aynı Origin+CSRF ister. `PublicBooking:AccessEnabled` varsayılan kapalıdır; ana 8080'de açılmaz. Token/yenileme/iptal/restore sınırı [ADR-008](docs/adr/008-public-appointment-access.md), gerçek kabul/teslim durumu [P04](docs/plans/P04.md#p04-10--sonuç-ve-müşteri-iptal-yetkisi-dar-planı--17102026) içindedir. Test: `dotnet test --project tests/Server.Tests/Server.Tests.csproj --no-restore --filter-class Server.Tests.PublicAppointmentAccessTests`. Müşteri ekranı/gerçek SMS eklenmez.

Türkiye'deki tek şubeli hizmet işletmeleri için planlanan markalı randevu uygulaması. P01 temel kurulum tamamlandı; P02'de Owner oturumu eklendi. Gerçek rezervasyon henüz yoktur. Güncel durum için [STATUS](docs/STATUS.md), ürün kararları için [PRODUCT](docs/PRODUCT.md), aşamalar için [ROADMAP](docs/ROADMAP.md).

Frontend `src/Web/src/features/auth`, `features/business` ve `features/staff` altında mevcut özelliklere göre düzenlenir. `app` oturum API yardımcısını ve yönetim yerleşimini, `components` ortak bileşenleri, `styles` ortak renk/form kurallarını barındırır. Testler ilgili özelliğin yanındadır. Yönetim ekranında işletme bilgileri, hesap ve güvenlik, çalışan erişimleri ayrı bölümlerdir; çalışan hesabında yalnız hesap ve güvenlik görünür.

Giriş, kimlik formları ve ortak yönetim alanının görsel sistemi [DESIGN](DESIGN.md) içinde tutulur. Projeye kurulu [Impeccable](.agents/skills/impeccable/SKILL.md) yeni ekranlarda önce taslak/onay akışını kullanır. Dekoratif salon fotoğrafları yerleşik `image_gen` ile üretilmiştir; gerçek işletme kanıtı değildir. `assets/plates/` altında exact prompt ve onay yan kayıtlarıyla saklanır, Docker derlemesine dahildir. Harici stok fotoğraf lisansı veya yeni görsel paketi eklenmedi. Yerel Unna Bold fontu SIL Open Font License 1.1 ile kullanılır; [lisansı](src/Web/src/assets/fonts/Unna-OFL.txt) imajda `/app/Unna-OFL.txt` olarak da bulunur. Yerel motor, önbellek ve inceleme çıktıları sürümlenmez; Windows başlatıcısı motoru ilk kullanımda doğrulayarak indirir.

## Ekran adresleri

İşletme sahibi günlük randevu ekranı `/yonetim/randevular` adresindedir. `?tarih=2026-10-16&calisan=<UUID>&sayfa=2&boyut=20` filtre/sayfa durumunu taşır; listede telefon yoktur. Ayrıntı `/{UUID}`, yeni kayıt `/yeni`, taşıma `/{UUID}/tasi` alt adreslerindedir. Günlük kayıtlar çalışanlara göre gruplanır; sayfalı bir gün tam yüklenmiş gibi sunulmaz. Elle kayıt, ayrıntı, onay/iptal/tamamlandı/gelmedi ve formdan taşıma mevcut Owner API'lerine bağlıdır. Müşteri rezervasyon sayfası/OTP/SMS veya Staff yönetimi değildir.

Liste API'si `GET /api/appointments/?date=yyyy-MM-dd&staffMemberId=<UUID>&page=1&pageSize=20` kullanır; tarih/çalışan isteğe bağlıdır, tarih verilmezse sunucunun İstanbul günü seçilir. MFA Owner, `no-store`, 1–50 sayfa boyutu, başlangıç/kimlikle sıralama ve gerçek toplam vardır; kişi telefonu ve işlem anahtarı yalnız liste için dışarı verilmez. İlgili PostgreSQL kapısı: `dotnet test --project tests/Server.Tests/Server.Tests.csproj --no-restore --filter-class Server.Tests.AppointmentListTests`; web: `cd src/Web; npm.cmd test -- Appointments.test.tsx appointmentsApi.test.tsx`. [Sentetik gerçek tarayıcı komutu ve önkoşulları](tests/Web.E2E/README.md#günlük-randevu-yönetimi-kabulü), [kabul/teslim sınırı](docs/plans/P04.md).

Yönetim ekranları ayrı adresler kullanır: `/yonetim/isletme`, `/yonetim/isletme/saatler`, `/yonetim/personel`, `/yonetim/hizmetler`, `/yonetim/rezervasyon-ayarlari`, `/yonetim/calisan-erisimleri`, `/yonetim/hesap` ve `/yonetim/degisiklik-kayitlari`. Personel ayrıntısı/görevleri ve hizmet düzenlemesi de doğrudan açılabilir. Geri/ileri, yenileme ve yeni sekme ekranı korur; liste sayfası `?sayfa=2` ile taşınır. Giriş gereken adres, tamamlanmış oturum/MFA sonrasında açılır; yetkiyi mevcut API kontrol eder. Kaydedilmemiş tanım formları gezinmede onay ister, işlem sürerken geçiş engellenir. İşletme profili taslağı yönetim içinde korunur; yenilemede kalıcı olarak saklanmaz.

E-posta doğrulama/sıfırlama bağlantısının kanıtı açılışta adres çubuğundan kaldırılır, yalnız bellekte tutulur ve ekran terk edildiğinde silinir. Tekrar yenilemede e-postadaki bağlantının yeniden açılması gerekir. [Mimari karar](docs/adr/004-url-navigation.md) ve [gerçek API gezinme testi](tests/Web.E2E/README.md) ayrıntıları içerir. React Router ve bağımlılıklarının [lisansları](docs/navigation-licenses.txt) imajda `/app/navigation-licenses.txt` olarak bulunur.

## Telefon doğrulama — gönderim kapalı

Telefon doğrulaması `/api/public-booking/verification/request`, `/resend`, `/verify` POST uçlarını kullanır. Ana kurulumda doğrulama ve gönderim kapalıdır; geçerli Origin/CSRF sonrası 503 döner ve kayıt oluşturmaz. P05-04 Netgsm adaptörü varsayılan kapalıdır; gerçek SMS/hesap/ödeme yapılmaz. Dar dispatch anahtarı ve kalıcı adet/TRY rezervasyonu [P05 yapılandırma ve işletim kaydında](docs/plans/P05.md#p05-04--gönderim-kapalı-netgsm-ve-kalıcı-bütçe--20102026), restore/para sınırı [ADR-009'da](docs/adr/009-booking-sms-budget.md) açıklanır. OTP tek başına randevu/müşteri oturumu oluşturmaz. [Güvenlik](docs/SECURITY.md).

Hatırlatma kapısı `AppointmentReminders:Enabled=false` kalır; ürün taşıyıcısı kapalıdır. Onaylı randevunun üç saat öncesi hedefi, tek iş ve ortak SMS bütçesi [P05 kaydındadır](docs/plans/P05.md#p05-08--gönderim-kapalı-hatırlatma-uygulaması--23102026). MFA Owner ayrıntısındaki “SMS hatırlatması” bölümü `GET /api/appointments/{id}/reminder` ile tercihi okur; Owner/MFA ve CSRF korumalı `POST /api/appointments/{id}/reminder/disable`, `{ "version": "güncel-randevu-UUID" }` ile yalnız bu randevuyu tek yönlü engeller. Gönderim kapalıyken de ret kaydedilir; bu işlem gönderimi açmaz, diğer randevuları/OTP'yi değiştirmez ve başlamış SMS'i geri alamaz. 204 veya kayıp yanıt sonrası GET ile sonuç doğrulanır; [P05-10 kabul kaydı](docs/plans/P05.md#p05-10--kullanılabilir-randevu-bazında-ret-akışı--23102026). İlgili sentetik test: kökten `dotnet run --project tests/Server.Tests/Server.Tests.csproj --no-build -- -class Server.Tests.AppointmentReminderTests` (önce restore/build; Docker çalışır olmalı).

Kilitli restore/build sonrasında ağsız adaptör ve gerçek PostgreSQL bütçe testleri: `dotnet run --project tests/Server.Tests --no-build -- -class '*NetgsmBookingSmsDeliveryTests' -class '*BookingSmsBudgetTests'`. Sahte taşıyıcı yalnız test sürecindedir; gerçek Netgsm çağrısı bu testlerde yoktur.

P04-09 geliştirme/GitHub/ana yerel teslimi tamamlandı: `POST /api/public-booking/appointments`, doğrulanmış challenge'ın aynı `requestKey`, `challengeId/challengeSecret`, hizmet/politika sürümleri, çalışan/saat ve ad/telefonunu alır. Güncel kurallar ve politika snapshot'ıyla tek Pending/Confirmed kayıt oluşturur; 201 ilk kabul, 200 aynı başarılı talebin 24 saatlik tekrarıdır. Sonuç yalnız tek kaydın dar özetidir; telefon/ad veya müşteri geçmişi içermez. Ana kapalı doğrulama ayarı bu yazımı da 503 ile kapatır; gerçek SMS ve sonuç/iptal token'ı yoktur. Test: `dotnet test --project tests/Server.Tests/Server.Tests.csproj --no-restore --filter-class Server.Tests.PublicAppointmentTests`. [Kapsam ve gerçek teslim durumu](docs/plans/P04.md#p04-09--atomik-müşteri-randevu-yazımı--17102026).

## Müşteriye açık okuma API'leri

P04-06 yalnız okuma sunar: `GET /api/public-booking/catalog?page=1&pageSize=20`, `GET /api/public-booking/staff?serviceId=<UUID>&page=1&pageSize=20` ve `GET /api/public-booking/availability?serviceId=<UUID>&staffMemberId=<UUID>&date=yyyy-MM-dd`. Oturum gerekmez. Katalog işletmenin adı/telefonu/adresi, ön süre/ufuk/adım ve iptal süresi/onay tercihini içeren politika ile aktif hizmetleri; çalışan listesi yalnız seçili hizmete atanmış aktif personelin kimlik/adını döndürür. Listeler 1–50 sayfa boyutu ve gerçek toplamla sınırlıdır; sayfa aralığı 1–10000, son sayfayı aşan istek son sayfaya çekilir. Fiyat mevcut hizmet API'sindeki gibi iki ondalıklı TRY metnidir.

Uygunluk aynı mesai/mola/izin/tampon/doluluk motorundan hesaplanan UTC `startsAt`, `timeZone`, `serviceVersion`, `policyVersion` taşır. Pasif/silinmiş/atanmamış seçim 404, geçersiz parametre veya İstanbul günü/rezervasyon ufku dışındaki tarih 400, geçerli fakat boş gün 200 ve boş dizidir. Müşteri/randevu bilgisi veya çalışan izin/mola gerekçeleri dönmez. Yanıtlar `no-store`, işlemler 15 saniye timeout; üç uç aynı gerçek IP için toplam 60/dakika ve mevcut 600/dakika uygulama limiti altındadır. Kamu 429 yanıtı ProblemDetails ve gerçek `Retry-After` taşır; istemci `X-Forwarded-For` yazarak limiti değiştiremez.

Kayıt/OTP/iptal ucu veya müşteri ekranı bu adımda yoktur; saat sorgusu randevu oluşturmaz ve saat tutmaz. Owner API'lerinin MFA/yetki sınırı değişmez. Gerçek PostgreSQL regresyonu: `dotnet run --project tests/Server.Tests/Server.Tests.csproj --no-build -- -class Server.Tests.PublicBookingTests` (önce `dotnet build FirmaRandevu.slnx --no-restore`). [Kapsam ve teslim kanıtı](docs/plans/P04.md#p04-06--müşteriye-açık-okuma-apileri--16102026).

## Gerekenler

- .NET SDK 10.0.401 (`global.json`)
- Node.js 24 LTS (`.node-version`; CI 24.21.0 kullanır)
- Docker Desktop / Docker Compose

Windows PowerShell'de `npm.ps1` imza politikası nedeniyle engelleniyorsa aşağıdaki gibi `npm.cmd` kullanın. Linux/macOS'ta aynı komutlar `npm` ile çalışır.

## Yerelde çalıştırma (Windows PowerShell)

Depo kökünde:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\Initialize-LocalEnv.ps1
docker compose --env-file deploy/.env -f deploy/compose.local.yaml up -d --build --wait
dotnet tool restore
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\Apply-LocalMigration.ps1
Invoke-WebRequest http://127.0.0.1:8080/health/live
Invoke-WebRequest http://127.0.0.1:8080/health/ready
```

İlk Owner hesabını yalnız boş yerel kurulumda bir kez oluşturun:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\Bootstrap-LocalOwner.ps1
```

Tarayıcıda `http://127.0.0.1:8080/` adresini açın. İlk girişten sonra doğrulayıcı uygulamada anahtarı girip altı haneli kodla iki aşamalı girişi açın; ekranda bir kez gösterilen kurtarma kodlarını güvenli yerde saklayın. Sonraki girişlerde parola ile birlikte uygulama kodu veya kullanılmamış bir kurtarma kodu gerekir. Doğrulayıcı ve tüm kurtarma kodları kaybolursa aşağıdaki yetkili manuel kurtarma süreci kullanılır.

İlk komut yalnız yerel geliştirme için ayrı, rastgele iki veritabanı parolası oluşturur. `deploy/.env` Git'e eklenmez; komutu tekrar çalıştırmak mevcut dosyayı değiştirmez. Migration komutu Identity tablolarını idempotent olarak ekler; uygulama kullanıcısına yalnız bu tabloların veri erişimini verir. Owner parolası komut satırına yazılmaz ve script sonunda geçici ortam değişkenleri temizlenir. Docker Compose veritabanını ve çerez anahtarlarını ayrı named volume'larda tutar; sıradan `down` bunları silmez.

Yerel Compose HTTP kullanır; güvenli çerez HTTPS gerektirdiğinden bu kurulum yalnız geliştirme içindir. Üretim kurulumu ve şifreli anahtar saklama P06'da doğrulanmadan gerçek firma verisiyle kullanılmaz. Migration uygulama başlangıcında kendiliğinden çalışmaz; canlı geçiş için ayrıca yedek ve ayrı migration yetkisi gerekir.

Servisleri durdurmak için:

```powershell
docker compose --env-file deploy/.env -f deploy/compose.local.yaml down
```

Bu Compose dosyası yalnız yerel geliştirme içindir. Gerçek firma kurulumu, yedek ve dağıtım P06'da hazırlanacaktır.

## Owner MFA kurtarma (yerel, yetkili operatör)

Bu işlem yalnız doğrulayıcı ve bütün kurtarma kodları kaybolduğunda, mevcut parola biliniyorsa kullanılır. Kurtarma isteyen kişinin işletme sahibi olduğunu ve işlem yetkisini mevcut sözleşme/kurulum kayıtları üzerinden bağımsız olarak doğrulayın; yalnız telefon veya e-posta bilgisi yeterli değildir. Onayı ayrı bir destek kaydında tutun. Script kimlik doğrulaması yapmaz; yetki işletim sistemi/Docker erişimine ve operatörün bu kontrolüne dayanır. Ortak admin hesabı veya web kurtarma ucu yoktur.

1. `Auth:InstanceId`, hedef DB/Compose projesi ve Owner UUID kimliğini kurulum envanterinden doğrulayın. Yerel firma kimliği `firma-randevu-local`dir. UUID'yi gerekirse aşağıdaki salt okunur sorguyla bulun; gerçek kişi verisini terminal kaydına veya prompt'a taşımayın.

   ```powershell
   'SELECT u."Id" FROM "AspNetUsers" u JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id" JOIN "AspNetRoles" r ON r."Id" = ur."RoleId" WHERE r."Name" = ''Owner'';' | docker compose --env-file deploy/.env -f deploy/compose.local.yaml exec -T db psql -U postgres -d firma_randevu -v ON_ERROR_STOP=1
   ```

2. Güncel imajı derleyip yukarıdaki migration komutunu uygulayın. Kurtarma kaydı için `P02OwnerMfaRecoveryAudit` tablosu gerekir. Uygulama rolünün bu tabloda yalnız `SELECT/INSERT` yetkisi vardır.
3. Komutu çalıştırın. Operatör ve benzersiz destek/onay kaydı referansları 1–64 karakterlik harf/rakam/alt çizgi/tire kodları olmalı; isim, telefon, e-posta veya serbest açıklama kullanmayın. Hedef firma/UUID çiftini aynen yazarak işlemi onaylayın.

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\Recover-LocalOwnerMfa.ps1
   ```

4. Komut eski doğrulayıcı anahtarı/kodları ve ana/geçici MFA oturumlarını geçersizleştirir; parola, rol ve hesap kilidini değiştirmez. Owner mevcut parolasıyla giriş yapıp mevcut ekranda MFA'yı yeniden kurar. Yeni MFA ile giriş tamamlanana kadar işletme yönetimine erişemez. Hesap kilitliyse mevcut kilit süresi beklenir.
5. Sonucu `OwnerMfaRecoveryAudits` tablosundaki firma/Owner UUID, UTC zaman, operatör ve işlem referansından doğrulayın. Hata veya timeout'ta otomatik tekrar yapmayın; önce referansın kaydedilip kaydedilmediğini kontrol edin. Aynı başarılı işlem referansı yeniden kullanılamaz. İşlem kaydı yazılamazsa MFA değişiklikleri de geri alınır.

Bu kurtarma eski anahtarı geri etkinleştirerek geri alınmaz; tamamlanması yeni MFA kurulumuyladır. Kod rollback'i için audit tablosunu silmek gerekmez. Üretim operatör yetkileri, yedek ve gerçek firma runbook'u P06 kapsamındadır; bu script yerel sentetik kurulum içindir.

## Owner parola sıfırlama (yerel, yetkili operatör)

Owner parolayı unuttuğunda destek, sahipliği bağımsız kurulum kayıtları üzerinden doğrular. Başvuranın söylediği e-posta veya telefon tek başına yeterli değildir. Operatör firma/Owner UUID, benzersiz destek referansı ve teslim kanalını doğrulamadan token üretmez. Operatör erişimi uygulamada gizli bir admin hesabı değildir; host/DB erişimi ve ayrı destek kaydıyla denetlenir.

1. Güncel imajı derleyin ve yukarıdaki migration komutunu uygulayın. `P02OwnerPasswordResetAudit` yalnız yeni işlem tablosunu ekler; uygulama rolüne bu tabloda yalnız SELECT/INSERT verilir.
2. Windows PowerShell'de aşağıdaki komutu çalıştırın. Script firma/Owner hedefini açıkça onaylatır ve token yazılmadan önce yalnız mevcut Windows kullanıcısının erişebildiği bir dizin hazırlar:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\Issue-LocalOwnerPasswordReset.ps1
   ```

   Ayrı sentetik kurulumlarda `-ComposeFile` ve `-EnvFile` ile hedef dosyalar seçilebilir; firma kimliği kontrolü yine zorunludur. Token `.local/owner-password-reset/<rastgele-dizin>/token.txt` içindedir. İçerik konsola/Docker loguna yazılmaz, mevcut dosya üzerine yazılmaz. Yeni script Windows PowerShell'in Türkçe kaynak metnini okuyabilmesi için UTF-8 BOM kullanır.
3. **30 dakika geçerli** tokenı önceden doğrulanmış özel kanaldan yalnız Owner'a teslim edin. Tokenı sohbet, destek kaydı, log veya Git'e koymayın; yeni parolayı operatör istemez/belirlemez. Teslim/kullanım sonrası geçici dosyayı silin. Bu iş otomatik e-posta/SMS göndermeyi veya herkese açık token üretme API'sini içermez.
4. Owner giriş ekranında “Parolamı unuttum” → “Özel kurtarma kodum var” formuna tokenı, yeni parolayı ve tekrarını girer. Token URL'ye veya kalıcı tarayıcı deposuna yazılmaz. POST, CSRF ve IP/hesap/global istek sınırlarıyla korunur. Token üretmek mevcut parolayı, oturumları veya MFA'yı değiştirmez.
5. Başarıda mevcut dahil tüm eski ana/geçici oturumlar ve eski sıfırlama tokenları geçersizleşir; otomatik giriş yapılmaz. MFA anahtarı/kullanılmamış kurtarma kodları, rol, kilit bitişi ve hata sayacı korunur. Yeni parola ve mevcut ikinci adımla giriş gerekir; hesap kilitliyse kilit süresi beklenir. MFA da kayıpsa ayrı yetkili MFA kurtarma prosedürü gerekir.

`OwnerPasswordResetAudits` üretim ve tamamlanmayı ayrı, değiştirilemez satırlarda kaydeder; token/parola içermez. Başarıyla kullanılana kadar aynı hesabın ayrı destek referanslarıyla üretilmiş birden çok tokenı geçerli olabilir; başarılı sıfırlama hepsini geçersizleştirir. Yeniden üretim eski oturumları kapatmaz. Aynı başarılı üretim referansı tekrar kullanılamaz.

DB/audit hatasında parola değişikliği geri alınır. Commit veya dosya teslimi sırasında bağlantı kesilirse otomatik tekrar yapmayın; önce işlem kaydını/özel dosyayı ve normal giriş sonucunu kontrol edin. Başarılı işlem kod rollback'iyle eski parolayı veya oturumları geri açmaz. Migration `Down` işlem kayıtlarını sildiği için gerçek kurulumda otomatik çalıştırılmaz; eski kodla yeni tablo korunabilir. Linux'ta doğrudan CLI kullanımı için operatör özel çıktı dizinini önceden `0700` ile hazırlamalıdır; komut dosyayı `0600` oluşturur. Üretim teslim kanalı/HTTPS/host yetkileri P06 kabulünün yerine geçmez.

## Kendi parolanızı değiştirme

Giriş yaptığınız hesap ekranında mevcut parolanızı, yeni parolanızı ve tekrarını girin.
Staff yalnız kendi parolasını değiştirebilir; Owner için tamamlanmış MFA girişi gerekir.
Yeni parola mevcut Identity kurallarını karşılamalıdır. Başarıda mevcut dahil bütün eski
oturumlar geçersizleşir. Staff yeni parolayla; Owner yeni parola ve mevcut ikinci adımla
yeniden giriş yapar. Başka hesapların parolaları ve oturumları değişmez.

Yanlış mevcut parola hesap deneme sayacına katılır; beş hatada 15 dakika kilit uygulanır.
DB işlemi başarısızsa parola/stamp değişikliği geri alınır. Bağlantı kesilirse başarı
varsaymayın ve otomatik tekrar göndermeyin; yeniden girişle sonucu kontrol edin.
Bu işlem yeni şema gerektirmez. Kod rollback'i değişmiş parolayı veya iptal edilen
oturumları geri açmaz. Staff parola kurtarma bu akışın kapsamına dahil değildir.

## Kontroller (Windows PowerShell)

Önce web çıktısını üretin; sunucu testi ana sayfanın aynı uygulamadan sunulduğunu doğrular.

```powershell
Set-Location src/Web
npm.cmd ci --ignore-scripts
npm.cmd run typecheck
npm.cmd run lint
npm.cmd test
npm.cmd run build
Set-Location ../..
dotnet restore FirmaRandevu.slnx --locked-mode
dotnet tool restore
dotnet build FirmaRandevu.slnx --no-restore
dotnet test FirmaRandevu.slnx --no-restore
dotnet format FirmaRandevu.slnx --verify-no-changes --no-restore
docker compose --env-file deploy/.env -f deploy/compose.local.yaml config --quiet
```

`GET /health/live` yalnız uygulamanın yanıt verdiğini, `GET /health/ready` PostgreSQL bağlantısının açılabildiğini gösterir. Her iki uç da müşteri verisi veya sır döndürmez.

## Yapı

- `src/Server`: ASP.NET Core uygulaması; derlenmiş web dosyalarını aynı origin'den sunar.
- `src/Web`: React/TypeScript başlangıç ekranı; `npm run build` çıktısı `src/Server/wwwroot` içine gider.
- `tests/Server.Tests`: sağlık uçları, statik web ve gerçek PostgreSQL üzerinde Owner oturumu için xUnit testleri.
- `deploy`: yerel Compose, Dockerfile ve örnek ayarlar.
- `docs`: ürün, yol haritası, aktif aşama ve teknoloji kararı.

MFA ile giriş yaptıktan sonra hesap ekranındaki “Parola değiştir” formuyla mevcut parolanızı doğrulayarak yeni parola belirleyebilirsiniz. Başarıda bu tarayıcı dahil bütün eski oturumlar kapanır; yeni parola ve doğrulayıcı/kullanılmamış kurtarma koduyla yeniden giriş gerekir. Doğrulayıcı kurulumu ve kurtarma kodları korunur. Bağlantı kesilip sonuç belirsiz kalırsa otomatik tekrar yerine yeniden girişle durumu kontrol edin.

Şu anki ekranda rezervasyon işlemi yoktur; çalışan ve hizmet tanımları sonraki P02 işleridir. Web etkileşim testleri Vitest/jsdom üzerinde çalışır; otomatik Playwright akışı henüz yoktur. Elle tarayıcı kontrolleri ve sınırları STATUS/P02 planında ayrıca kaydedilir.

## Staff daveti — manuel teslim

1. `P02StaffInvitation` migration'ını yönetici rolüyle uygulayın; yerel `deploy/Apply-LocalMigration.ps1` bunu ve gerekli tablo yetkilerini verir. Migration `Staff` rolünü ve iki yeni tabloyu ekler; hesap oluşturmaz.
2. MFA ile giriş yapmış Owner, hesap ekranındaki “Staff daveti” formunda çalışanın e-postasını yazar. Adresin çalışana ait olduğunu bağımsız olarak doğrulayıp yalnız ona teslim edeceğini onaylar. Bu manuel doğrulama, e-posta servisinden teslim/posta kutusu sahipliği kanıtı değildir.
3. “Davet oluştur” **24 saat** geçerli, tek kullanımlık kodu yalnız o yanıtta verir. “Kodu göster”e basın, kod alanına tıklayıp seçili kodu bilgisayarda Ctrl+C, telefonda kopyalama menüsüyle elle kopyalayın. Doğrulanmış özel kanaldan teslim ettikten sonra “Kodu teslim ettim, temizle”ye basın ve geçici kopyayı/clipboard'u temizleyin. Kod DB'de yalnız SHA-256 hash'idir; URL/log/kalıcı tarayıcı deposuna yazılmaz. Otomatik e-posta/SMS veya dış servis yoktur.
4. Staff giriş ekranında “Staff davetim var”ı seçer; davet edilen e-posta, kod, kendi belirlediği parola ve tekrarını girer. Başarıda hesap ve sabit Staff rolü aynı transaction'da oluşur; otomatik giriş yapılmaz. Normal e-posta/parola girişiyle yalnız kendi hesap/çıkış ekranına erişir. Owner davet/MFA/parola yönetimine erişemez; çalışan/randevu izinleri bu işin kapsamında değildir.
5. Aktif mükerrer davet veya mevcut hesap 409'dur. Bekleyen daveti “Daveti iptal et” ile geçersizleştirin; iptal/süre dolumu sonrası aynı adres için yeni kod üretilebilir. Eski kod çalışmaz. İptal tekrarı güvenlidir; kabul edilmiş davetin iptali 409'dur ve mevcut hesabı kapatmaz. Liste süresi en yakın ilk 20 bekleyen daveti gösterir; iptal/kabul/süre dolumuyla sonraki kayıtlar görünür.

Üretim/kabul/iptal audit satırları Actor UUID/işlem/tarih/davet UUID'sini içerir; audit'te e-posta/token/parola kopyası yoktur. Audit uygulama rolünde yalnız SELECT/INSERT; davet tablosunda SELECT/INSERT/UPDATE vardır. Hash/stamp/rol, davet tüketimi ve audit aynı PostgreSQL transaction'ında; paralel kabulden yalnız biri başarı verir. DB/audit/commit hatasında hesap ve tüketim geri alınır. Sonuç/teslim belirsizliğinde otomatik tekrar yoktur: Owner listeyi yenileyip kullanılmamış daveti iptal ederek yeniden üretebilir; Staff önce normal girişle sonucu kontrol eder.

Kod rollback'inde ek tablolar/Staff rolü korunabilir. `Down` audit/davet kayıtlarını ve Staff rolünü sildiği için gerçek kurulumda otomatik kullanılmaz; kabul edilmiş hesapların erişimini etkiler. Staff MFA/hesap kapatma sonraki ayrı onaylı işlerdir. Üretim HTTPS/teslim/izolasyon/yedek kabulü henüz bu yerel işin kanıtı değildir.

## Staff parola sıfırlama — manuel teslim

1. `P02StaffPasswordResetAudit` migration'ını ayrı yönetici rolüyle uygulayın. Yerel `deploy/Apply-LocalMigration.ps1` yeni işlem tablosunu ekler; uygulama rolüne yalnız SELECT/INSERT verir. Başlangıçta otomatik migration yoktur.
2. MFA ile giriş yapan Owner, “Staff parola sıfırlama kodu” formunda mevcut çalışanın e-postasını girer. Çalışanın kimliğini/adresini bağımsız olarak doğrulayıp yalnız kendisine teslim edeceğini onaylar. Owner veya çift rollü hesap hedeflenemez. Kod üretimi mevcut parolayı veya oturumları değiştirmez.
3. Kodu gösterin, alana tıklayıp seçin ve elle kopyalayın. Özel kanaldan doğrulanmış çalışana teslim edip ekrandan temizleyin. Kod 30 dakika geçerlidir; URL, log veya kalıcı tarayıcı deposuna yazılmaz. E-posta/SMS gönderilmez; posta kutusu sahipliği otomatik doğrulanmış sayılmaz.
4. Staff çıkış durumunda “Staff parolamı unuttum”u seçer, kodu ve kendi belirlediği yeni parolayı/tekrarını girer. Başarıda kod tüketilir, eski oturumlar ve önceki kodlar geçersizleşir. Yeni parolayla normal giriş gerekir; otomatik giriş yoktur. Owner parolası/MFA'sı ve diğer hesaplar korunur. Mevcut hesap kilidi/hata sayacı sıfırlanmaz.

CSRF, rol/hesap doğrulaması, IP/endpoint/global ve hesap sınırları sunucudadır. Issue ve Complete kayıtları firma/Grant/Staff/Actor UUID, tür ve UTC zaman tutar; e-posta, kod ve parola içermez. Parola/stamp ve tüketim kaydı aynı transaction'dadır; audit veya commit hatasında geri alınır, paralel kullanımdan yalnız biri başarılı olur. Bağlantı belirsizliğinde otomatik tekrar yoktur; önce normal girişle sonucu kontrol edin. Kod teslimi belirsizse teslim etmeyin, süre dolduktan sonra yeniden üretin.

Kod rollback'inde audit tablosu korunur. `Down` işlem kayıtlarını sildiğinden gerçek kurulumda otomatik kullanılmaz. Başarılı sıfırlama eski parolayı/oturumları geri açarak geri alınmaz; gerekirse yeniden doğrulanmış sıfırlama yapılır. Staff MFA, hesap kapatma ve genel audit ekranı bu işin dışındadır.

## İşletme profili

`P02BusinessProfile` migration'ını ayrı yönetici rolüyle uygulayın; yerelde `deploy/Apply-LocalMigration.ps1` kullanılır. Migration boş bir profil ekler, hesapları/parolaları/MFA'yı değiştirmez. Uygulama rolünün profil tablosunda yalnız SELECT/UPDATE; bu işe özel işlem tablosunda yalnız SELECT/INSERT yetkisi vardır.

MFA ile giriş yapmış Owner, “İşletme profili” formunda işletme adını (2–150 karakter), isteğe bağlı telefonu, iletişim e-postasını ve adresi kaydeder. Telefon Türkiye cep/sabit biçiminde alınır ve `+90` biçiminde saklanır; e-posta en fazla 254, adres en fazla 500 karakterdir. İletişim e-postası giriş hesabının adresini değiştirmez ve bir mesaj servisi yapılandırmaz. Staff/anonim/MFA tamamlamamış Owner profil API'sini kullanamaz. `GET /api/business-profile/` okur, CSRF korumalı `POST /api/business-profile/` günceller.

Başarıda sunucudan gelen bilgiler gösterilir. Eski açık form `409` alır; “Güncel bilgileri yükle” kaydedilmemiş değişiklikleri silip son kaydı getirir. Bağlantı/500 sonucunda başarı iddia edilmez veya otomatik tekrar yapılmaz; yeniden yükleyerek sonucu kontrol edin. Profil ve Actor UUID/sürüm/UTC zaman içeren işlem kaydı aynı transaction'dadır; işlem kaydı iletişim değerlerini içermez. Kaydetme hesabın parolasını/oturumunu/MFA'sını değiştirmez.

Geri dönüşte önceki uygulama imajı kullanılabilir; yeni profil ve işlem tabloları korunur. `Down` bu tabloları sildiğinden gerçek kurulumda otomatik uygulanmaz. Logo, çalışan/hizmet/mesai, genel işlem kaydı ekranı ve kamuya açık profil bu işin dışındadır.

## İşletme sahibi kurtarma e-postasını doğrulama

P02-12, MFA Owner'ın **mevcut hesap e-postasını** doğrular; adres değiştirme veya otomatik parola sıfırlama içermez. “Hesap ve güvenlik” bölümündeki durum, eski Identity EmailConfirmed bayrağından ayrı tutulur. P02OwnerRecoveryEmail migration'ını ayrı yönetici DB rolüyle uygulayın; yerelde deploy/Apply-LocalMigration.ps1 kullanılır. OwnerRecoveryEmails için uygulama rolü SELECT/INSERT/UPDATE alır; DELETE/TRUNCATE alamaz. Migration mevcut hesap/parola/MFA/oturum verisini değiştirmez.

GET /api/auth/recovery-email/ ve CSRF korumalı POST /api/auth/recovery-email/request yalnız MFA Owner'a açıktır. Anonim CSRF POST /api/auth/recovery-email/confirm, yalnız 30 dakikalık tek kullanımlık kanıtı tüketir. Bağlantının açılması doğrulama yapmaz. Token firma/adres/stamp/Owner rolüne bağlanır; DB'de yalnız SHA-256 hash tutulur. Yeni istek önceki bekleyen token'ı iptal eder; DB'de 60 saniye yeniden gönderim aralığı, IP/genel istek limitleri vardır. Doğrulama giriş kimliğini, parolayı veya MFA'yı değiştirmez; hesap adresi değişirse doğrulanmış durum eşleşmez.

Bu işin teslim adaptörü **yalnız yerel Linux Development testi** içindir. Varsayılan kapalıdır; etkinleştirmek için özel tam dizin RecoveryEmail__LocalPickupDirectory ve sabit localhost HTTP origin RecoveryEmail__PublicOrigin tanımlanır. Örneğin ayrı sentetik Docker uygulamasında /app/keys/mail ve http://localhost:8081. Yalnız .test adresleri kabul edilir; iletiler 0700 dizinde 0600 .eml dosyalarına atomik yazılır, dışarı gönderilmez. Dizin web kökünde, symlink veya geniş izinli olamaz. Production'da veya Windows sürecinde bu modun teslim adaptörü hatayla reddedilir; Windows geliştirme testleri Linux Docker konteynerinde yürütülür. Ana yerel kurulumda bu ayarları açmayın ve mevcut Owner'ı sentetik kabul için kullanmayın.

Özel test iletisinin base64 gövdesindeki bağlantıyı test tarayıcısında açın, “E-postamı doğrula” ile onaylayın ve hesaba dönüp “Durumu yenile” ile sonucu görün. Token fragment'ten belleğe alınır ve URL'den temizlenir; token iletisi/sırrı loga, Git'e veya frontend depolarına kopyalanmaz. Teslim DB commit'inden sonra yapılır; dosya teslim hatası 503 verir ve bekleyen token iptal edilir. Kesinti/yanıt belirsizliğinde başarı varsaymayın; durumu okuyun veya bir dakika sonra yeni istek oluşturun. Parola sıfırlama için kalıcı gönderim kuyruğu P02-13, gerçek SMTP adaptörü P02-14 bölümündedir; bu yerel dosya modu gerçek posta kutusu tesliminin kanıtı değildir. Test iletilerini kabul sonunda özel test ortamıyla birlikte temizleyin.

Kod geri dönüşünde önceki imaj ve ek tablo birlikte kullanılabilir; doğrulama kaydı tutulur. Down doğrulama kayıtlarını sildiğinden gerçek kurulumda otomatik kullanılmaz; yalnız sentetik DB'de test edilir.

## Owner otomatik parola sıfırlama — yerel sentetik kabul

P02-13, P02-12 ile gerçekten doğrulanmış **mevcut hesap e-postası** için bağlantı isteme, yeni parola belirleme ve normal MFA girişini ekler. Bu bölüm özel yerel dosya teslimini anlatır; gerçek SMTP yapılandırması aşağıdadır. Varsayılan kapalıdır; ana yerel Owner hesabında açmayın. Ayrı Linux Docker Development testinde `OwnerPasswordReset__LocalEnabled=true`, açık `Auth__InstanceId`, firma başına kalıcı Data Protection anahtar dizini ve P02-12'nin özel yerel posta dizini/localhost origin ayarları gerekir. Yalnız `.test` adresleri teslim alır. Production bu yerel modu reddeder. Yeni paket veya ücretli sağlayıcı kullanılmaz; Internet e-postasının ücretsiz/sınırsız teslimi garanti edilmiş sayılmaz.

1. Ayrı yönetici rolüyle `P02OwnerSelfServiceReset` migration'ını uygulayın. Yerel `deploy/Apply-LocalMigration.ps1`, `OwnerSelfServiceResets` için yalnız SELECT/INSERT/UPDATE verir; DELETE/TRUNCATE yoktur. Migration hesap/parola/MFA verisini değiştirmez. Uygulama başlangıcında otomatik migration yapılmaz.
2. `GET /api/auth/password-reset-options` yalnız kurulumun yerel teslim özelliğinin açık olup olmadığını bildirir. “Parolamı unuttum” ekranı kapalı/erişilemeyen gönderimi açıkça gösterir; özel kurtarma kodu yolu korunur. CSRF korumalı anonim `POST /api/auth/password-reset-request` hesap e-postasını alır. Bilinmeyen, Staff, doğrulanmamış, uygunsuz veya yeniden gönderim aralığındaki hesaplar aynı 202 yanıtını alır. Bu yanıt gönderim başarısı değildir. IP/global limitleri ve hesap başına DB'de 60 saniye aralık vardır; yanıt en az 350 ms bekler, mutlak sabit süre garantisi değildir.
3. Uygun Owner satırı kilitlenir; 30 dakikalık mevcut Identity token'ı ve değiştirilemez `SelfIssued` işlem kaydı, firma anahtarıyla ayrıca şifrelenmiş DB teslim işiyle aynı transaction'da yazılır. Yeni kabul edilen talep önceki otomatik bağlantıyı geçersizleştirir; manuel kod kayıtları korunur. Talep mevcut parolayı, oturumları, kilidi veya MFA'yı değiştirmez.
4. `BackgroundService` DB işini 30 saniyelik lease ile alır; dış teslim transaction dışında ve en çok 10 saniye sürer. En çok dört deneme, 15/30/60 saniye yeniden deneme aralığı ve 30 dakika sona erme uygulanır. Worker yeniden açıldığında süresi dolmuş lease alınabilir. Firma/rol/adres/doğrulama/stamp/güncel talep teslim ve kullanım öncesi kontrol edilir; eski worker sonucu yeni talebi ezemez. Yerel ileti adı teslim kimliğiyle sabittir; yeniden deneme aynı dosyayı çoğaltmaz. Bu, gelecekteki dış sağlayıcı için mutlak exactly-once sözü değildir.
5. Özel `.eml` bağlantısındaki `reset-owner-password` fragment'i belleğe alınır ve URL'den temizlenir. Bağlantı açılması parolayı değiştirmez. Yeni parola/tekrar açık POST ile gönderilir; token için kopyalama alanı yoktur. Parola ve `Completed` kaydı atomiktir; tek kullanım/paralel kullanım korunur. Başarı eski oturumları iptal eder; otomatik giriş yapılmaz, mevcut MFA ile normal yeni parola girişi gerekir. MFA anahtarı/kurtarma kodları korunur.

Çalışan worker teslim, tamamlanma, iptal, son başarısızlık ve sona ermede DB'deki şifreli teslim içeriğini temizler. Worker kapalıysa bu temizlik çalışmaz; bağlantının sunucu tarafındaki süre sınırı yine geçerlidir. Özel test ileti/link/anahtar dosyalarını kabul sonunda yalnız ayrı test ortamıyla birlikte temizleyin. Kod geri dönüşünde önceki imaj kullanılabilir ve yeni tablo korunur; yeni worker/talepler durur. `Down` yalnız boş sentetik DB'de test edilir. Değişmiş parolayı veya kapatılmış oturumu imaj geri dönüşü geri açmaz. Staff/davet otomasyonu, adres değiştirme ve DNS/VPS ayrı onaylı işlerdir.

## Owner e-postası — SMTP gönderimi

P02-14, doğrulama ve otomatik parola yenileme iletilerini MailKit ile SMTP üzerinden gönderir. Varsayılan `IdentityEmail:Mode=Local` ve otomatik sıfırlama kapalıdır. Ana yerel hesabı değiştirmeden ayrı test kurulumunda deneyin. Normal Gmail parolası kullanılmaz; test hesabında iki aşamalı doğrulama ve Google uygulama şifresi gerekir.

Windows'ta test sırrını yalnız mevcut kullanıcının eriştiği Git dışı dosyaya kaydetmek için:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\Initialize-LocalSmtpTest.ps1
```

Bu komut `.local/p02-14-private/smtp.env` oluşturur; hesap açmaz, uygulamayı yapılandırıp başlatmaz veya ileti göndermez. Dosya içeriğini sohbet/log/Git'e koymayın. `.local` Docker build bağlamının da dışındadır. Mevcut dosyanın üzerine yazmaz. Ayrı test uygulamasına aşağıdaki ayarları ortam değişkeni olarak bağlayın; gerçek değerler özel ortam/sır yönetiminden gelmelidir:

| Ortam değişkeni | Anlamı |
| --- | --- |
| `IdentityEmail__Mode` | `Smtp` |
| `IdentityEmail__Smtp__Host` | Gmail testinde `smtp.gmail.com` |
| `IdentityEmail__Smtp__Port` | Genellikle `587` (zorunlu STARTTLS); `465` doğrudan TLS |
| `IdentityEmail__Smtp__Username` | SMTP hesap kullanıcı adı |
| `IdentityEmail__Smtp__Password` | SMTP sırrı; Gmail testinde uygulama şifresi |
| `IdentityEmail__Smtp__FromAddress` | Sağlayıcının izin verdiği gönderen adresi |
| `IdentityEmail__Smtp__DailyLimit` | Kurulum başına günlük deneme tavanı; varsayılan 50, geçerli aralık 1–500 |
| `IdentityEmail__Smtp__AllowedRecipients__0` | Development'ta zorunlu açık test alıcısı; gerekirse sonraki indeksler |
| `IdentityEmail__PublicOrigin` | Sabit kök HTTPS adresi; Development'ta yalnız loopback HTTP istisnası |
| `OwnerPasswordReset__Enabled` | Doğrulanmış Owner'a otomatik bağlantı için `true` |
| `Auth__InstanceId`, `Auth__KeysDirectory` | Açık firma kimliği ve firma başına kalıcı özel anahtar dizini |

SMTP ile yerel dosya teslimi birlikte açılamaz; `RecoveryEmail__LocalPickupDirectory` ve `OwnerPasswordReset__LocalEnabled` kaldırılmalıdır. Yanlış ayarlar uygulamanın başlamasını engeller. Sertifika denetimi açıktır; TLS yoksa düz metin gönderime geri dönülmez. Üretimde loopback bağlantısı reddedilir. Host header'ından e-posta bağlantısı üretilmez. Gönderen ve alıcı adresi SMTP şifresini/tokenı loglamayan yapılandırmadan gelir; alıcı mevcut Owner hesabının ayrıca doğrulanmış e-postasıdır. Adres değiştirme bu işin kapsamında değildir.

`P02IdentityEmailQuota` migration'ı yalnız gün/sayaç tablosu ekler. Yerel migration script'i uygulama rolüne SELECT/INSERT/UPDATE verir, DELETE/TRUNCATE vermez. Eşzamanlı istekler atomik sınırdan geçer; başarısız bağlantı ve yeniden deneme de kotayı tüketir. UTC gün değişiminde yeni sayaç açılır, restart sınırı sıfırlamaz. Aynı SMTP hesabı birden fazla firmada kullanılıyorsa toplam sağlayıcı kotası kurulumlar arasında ayrıca paylaştırılmalıdır.

Owner önce MFA ile giriş yapıp hesap e-postasına doğrulama iletisi ister, gelen bağlantıda açıkça onaylar. Sonra giriş ekranında e-posta ile parola yenileme bağlantısı isteyebilir. SMTP'de doğrulama isteği başarısız olursa 503 döner ve bekleyen token iptal edilir; otomatik yeniden gönderim yerine bir dakika sonra yeni istek gerekir. Sıfırlama kuyruğu geçici hatada en çok dört deneme yapar; kalıcı kimlik/TLS/5xx hatası veya kota bitişinde durur. `Delivered`, SMTP sunucusunun kabulünü ifade eder; gelen kutusu teslimini garanti etmez. Belirsiz timeout'ta aynı Message-ID ile tekrar olabilir; dış teslim için mutlak exactly-once sözü yoktur.

Gmail, Hotmail ve kurumsal adresler alıcı olabilir; ilgili posta sunucusu spam/ret kuralları uygular. MailKit ve bağımlılıkları MIT lisanslıdır; bildirimler `docs/identity-email-licenses.txt` ve imajdaki `/app/identity-email-licenses.txt` dosyasındadır. Google'ın kişisel Gmail gönderim sınırları vardır; ücretsiz ve sınırsız teslim taahhüdü verilmez. Uygulama şifresi için [Google yönergesi](https://support.google.com/mail/answer/185833?hl=en), sınırlar için [Gmail yardım sayfası](https://support.google.com/mail/answer/22839?hl=en).

Geri dönüşte otomatik sıfırlamayı kapatıp SMTP ayarlarını kaldırın; yerel varsayılanla önceki imaja dönülebilir. Ek kota tablosunu gerçek DB'de silmeyin; Down yalnız boş sentetik DB'de denenir. İmaj geri dönüşü değiştirilmiş parolayı veya iptal edilmiş oturumları geri açmaz. Test bitince ayrı konteyner/anahtar/özel dosyaları temizleyin ve Google uygulama şifresini kullanıcı hesabından iptal edin. Bu kabul üretim DNS/SPF/DKIM, diğer sağlayıcılarda gerçek teslim veya P06 canlıya çıkış kabulü değildir.

### Kalıcı yerel Owner e-posta gönderimi

Windows'ta mevcut tek Owner ve çalışan yerel DB için:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\Initialize-LocalIdentityEmail.ps1
docker compose --env-file deploy/.env -f deploy/compose.local.yaml config --quiet
docker compose --env-file deploy/.env -f deploy/compose.local.yaml up -d app --wait
```

Yardımcı, Gmail gönderen adresini ve **Google uygulama şifresini** bilgisayarda özel girişle alır; normal Gmail parolası kullanılmaz. Ayarlar `.local/identity-email-private/app.env` içinde yalnız Windows kullanıcısına verilen ACL ile saklanır. Dosya şifreli değildir; içeriğini sohbet/log/ekran görüntüsüne taşımayın. Git ve Docker build bağlamı `.local`'ı dışlar. Compose yalnız dosya varsa yükler; dosya yoksa önceki kapalı yerel varsayılan korunur. Mevcut dosyanın üzerine yazılmaz; Owner hesabı/e-postası/parolası/MFA değiştirilmez. Alıcı otomatik mevcut Owner adresidir, localhost:8080 origin ve günlük 50 SMTP denemesi sınırı kullanılır. Bu dosya çalışmaya devam etmesi için korunur; geçici test şifresi temizliğiyle birlikte kaldırılmaz. Şifre iptal edildiğinde veya değiştiğinde kullanıcı bu özel dosyayı yerelde güncelleyip app'i yeniden oluşturmalıdır. Değerleri gösteren `docker compose config` yerine `config --quiet` kullanın.

Owner, normal giriş + MFA sonrası **Hesap ve güvenlik → Doğrulama gönder** ile mevcut hesap adresini ayrıca doğrular. Sonra **Parolamı unuttum** ekranındaki e-posta isteği kullanılabilir. Adres doğrulanmadığında veya uygun hesap bulunmadığında genel yanıt verilir; bu yanıt teslim kanıtı değildir. Kurulum anahtarlarını kalıcı tutun. Yerel ek Compose override'ınız varsa yukarıdaki komutlara aynı `-f` dosyasını ekleyin.

Gönderimi geri almak için özel klasörde `app.env` dosyasını `app.env.disabled` olarak yeniden adlandırıp aynı Compose komutuyla app'i yeniden oluşturun. Yeniden etkinleştirmek için adı geri getirip app'i yeniden oluşturun. Veritabanını ve MFA/Data Protection volume'larını silmeyin. Bu yerel kurulum üretim HTTPS/DNS/operasyon kabulü yerine geçmez.

### Parola ve MFA kurtarmasının farkı

- E-posta bağlantısı **parolayı yeniler**; mevcut MFA uygulaması ve yedek kodları korunur.
- **Parola sıfırlama kodum var**, e-posta erişimi kaybedildiğinde kimlik ayrıca doğrulanarak yetkili işletmeci tarafından teslim edilen 30 dakikalık parola kodunu kullanır. MFA yedek kodları bu formda geçerli değildir; normal e-posta sıfırlama destek görüşmesi gerektirmez.
- **Kurtarma kodu kullan**, önce mevcut parola ile giriş yaptıktan sonra MFA uygulama kodunun yerine bir adet saklanmış MFA kodunu kullanır. Her kod tek kullanımlıdır.
- MFA Owner, **Hesap ve güvenlik** bölümünde kalan kod sayısını görür; mevcut parolasını ve açık onayını vererek sekiz yeni kod oluşturabilir. Eski kodlar ve bütün oturumlar iptal edilir; parola ve MFA anahtarı değişmez. Yeni kodlar yalnız bir kez gösterilir. Sonuç ağ kesintisiyle belirsiz kalırsa normal parola + uygulama koduyla yeniden giriş yapıp kodları tekrar oluşturun.
- Telefon ve tüm MFA kodları kaybedildiğinde e-posta sıfırlaması MFA'yı atlamaz. Mevcut bağımsız kimlik doğrulamalı işletmeci MFA kurtarma prosedürü son çaredir; ortak gizli yönetici girişi yoktur.
- **Girişe dön** MFA bekleme ekranında geçici giriş cookie'sini de kapatır. Çıkış CSRF gerektirir; kapalı/geçersiz oturumda tekrar güvenle yapılabilir.

## Haftalık işletme saatleri (P02-20)

MFA ile giriş yapan Owner, **İşletme saatleri** bölümünde Pazartesi–Pazar için kapalı günleri ve açık günlerin tek açılış/kapanış aralığını kaydeder. Saatler Europe/Istanbul bölgesindedir; dakika hassasiyetinde 00:00–23:59 kullanılır ve kapanış aynı gün içinde açılıştan sonra olmalıdır. İlk kurulumda saatler belirlenmemiştir; ekrandaki başlangıç seçimleri işletme adına kaydedilmez. Personel mesaisi, istisna/mola ve randevu uygunluğu bu özelliğin dışındadır.

`BusinessOpeningHours` migration'ı yalnız saat/audit tablolarını ekler. Yerel migration komutu `powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\Apply-LocalMigration.ps1` komutudur; mevcut uygulamada önce tutarlı yedek alınır, kontrollü yönetici DB yetkisiyle uygulanır ve sonrasında imaj/sağlık/veri koruma doğrulanır. Başlangıçta otomatik migration yoktur. Saat başlığı SELECT/UPDATE, günler SELECT/INSERT/UPDATE, audit SELECT/INSERT ile sınırlıdır. `Down` kaydedilmiş saatleri ve audit'i siler; ana veride otomatik çalıştırılmaz. Eklemeli yeni şemada eski imajla çalışma kabulü aşama planına kaydedilir; imaj geri dönüşü DB restore kabulü değildir.

Özelliğe ait senaryolar dahil tüm gerçek PostgreSQL sunucu testleri depo kökünde `dotnet test FirmaRandevu.slnx --no-restore` ile çalışır. Tam kalite kapıları yukarıdaki test bölümündedir; kabul ve işletim kanıtının tek kaynağı [P02 planıdır](docs/plans/P02.md#p02-20--haftalık-işletme-saatleri).

## Personelin haftalık çalışma saatleri (P02-21)

MFA Owner, **Personel → Çalışma saatleri** bölümünde her personelin yedi günlük haftasını kaydeder. Çalışılmayan günlerde saat tutulmaz; çalışılan günde tek başlangıç/bitiş aralığı, Europe/Istanbul ve aynı gün içinde artan dakika kuralı kullanılır. Saatler kendiliğinden doldurulmaz. Pasif personelin saatleri korunur ve görüntülenir; düzenleme için personelin aktifleştirilmesi gerekir. Bu tanımlar giriş hesabını veya işletme saatlerini değiştirmez; mola/izin/özel tarih ve randevu uygunluğu bu işte yoktur.

`StaffWorkingHours` migration'ı yalnız iki boş saat/audit tablosu ekler. Yukarıdaki kontrollü migration/yedek yöntemi uygulanır; uygulama rolü günlerde SELECT/INSERT/UPDATE, audit'te SELECT/INSERT ile sınırlıdır. Personel sürümü ad/aktiflik/hizmet seçimiyle ortak kullanılır; eski taslak reddedilir. `Down` yeni saat/audit verisini siler; ana ortamda otomatik çalıştırılmaz. Kabul/geri dönüş kanıtı [P02 planındadır](docs/plans/P02.md#p02-21--personelin-haftalık-çalışma-saatleri); test ve kalite komutları yukarıdaki mevcut kapılardır.

## Logo özelliğinin kaldırılması

İşletme logosu yükleme/ayar/gösterim özelliği kaldırıldı. Eski `/api/business-logo/` ve görüntü adresleri 404 döner. SkiaSharp ve native görüntü işleme paketleri kullanılmaz. Önceki P02-22 kabulü tarihsel kayıttır.

`RemoveBusinessLogo` migration'ı yalnız `BusinessLogos` tablosunu ve saklanan görüntüyü kaldırır; geçmiş `BusinessLogoAudits` kayıtları korunur ve uygulama rolü yalnız SELECT alır. Eski migration'lar değiştirilmez. Mevcut kurulumda önce yedek alıp SQL'i inceleyin, sonra yukarıdaki kontrollü migration komutunu çalıştırın. `Down` boş logo tablosunu yeniden oluşturur; eski görüntüyü geri getirmez ve önceki imaj için tablo yetkileri ayrıca gerekir. Görüntülü geri dönüş migration öncesi yedeği gerektirir. [Kapsam ve kanıt](docs/plans/P02.md#logo-özelliğinin-kaldırılması--04102026).

## Değişiklik kayıtları (P02-23)

MFA Owner, **Değişiklik kayıtları** bölümünde mevcut tanım, çalışan erişimi ve kurtarma kayıtlarını tarih/işlem/yapan hesap/ilgili kayıt olarak görür. Tümü/Tanımlar/Hesap ve güvenlik kategorileri, yenileme ve önceki/sonraki sayfa vardır. Saatler İstanbul bölgesinde, hesap ve hedef adları güncel kayıtlardan gösterilir; yerel kurtarma işlemlerinin aktörü “Yerel bakım”dır. Eski/yeni değerler, giriş geçmişi, kodlar, silme, dışa aktarma veya yeni audit yazımı eklenmez.

`GET /api/audit-log/` yalnız MFA Owner içindir; no-store, 30/IP/dakika ve kategoriye bağlı cursor kullanır. Varsayılan sayfa 20 kayıt; sunucu sınırı 1–50'dir. `AuditLogIndexes` migration'ı yalnız 12 mevcut audit tablosuna sıralama indeksi ekler. Uygulama DB yetkileri değişmez; Down yalnız bu indeksleri kaldırır, kayıt silmez. Kontrollü yedek/migration ve kabul ayrıntıları [P02 planında](docs/plans/P02.md#p02-23--değişiklik-kayıtlarını-görüntüleme).

İlgili kontroller: kökten `dotnet test FirmaRandevu.slnx --no-restore --filter FullyQualifiedName~AuditLogTests`; web dizininde `npm.cmd test -- AuditLog.test.tsx`. Yayında yukarıdaki tüm kalite kapıları çalıştırılır.

## Randevu motorunun ilk API'si (P03-02)

MFA Owner için tek hizmet/çalışan: `GET /api/appointments/availability?serviceId&staffMemberId&date`, `POST /api/appointments/` ve `GET /api/appointments/{id}`. Slot sorgusu tek gün içindir; müşteri bilgisi içermez. Oluşturmada mevcut CSRF sözleşmesi, UUID requestKey, serviceId/serviceVersion, staffMemberId, saat dilimli startsAt ve asgari customerName/customerPhone gerekir. Fiyat/süre/durum sunucudan gelir; aynı anahtar/içerik aynı kaydı, farklı içerik veya zaman çakışması 409 döndürür. Başlangıç politikası 60 dakika ön süre/30 gün ufuk/15 dakika saat adımıdır. Müşteri rezervasyon ekranı/anonim API, SMS, taşıma/iptal bu ilk işte yoktur.

`AppointmentEngine` eklemeli migration'ı randevu/blok/audit/politika tablolarını ve GiST/btree_gist korumasını ekler; yukarıdaki kontrollü migration komutu kısıtlı app_user yetkilerini uygular. Mesai daraltması mevcut randevuyu dışarıda bırakırsa iki saat formu açıklama gösterir ve daha geniş saatlerle düzeltmeye izin verir. Randevu verisi oluştuktan sonra eski imaj mesai güvencesini bilmez: otomatik Down/restore/uyumsuz imaj dönüşü yapmayın. [Kabul ve geri dönüş sınırları](docs/plans/P03.md#p03-02-yerel-geliştirme-kabulü--14102026).

İlgili regresyon komutu: kökten `dotnet test FirmaRandevu.slnx --no-restore --filter "FullyQualifiedName~AppointmentTests|FullyQualifiedName~AppointmentStorageTests|FullyQualifiedName~BusinessHoursTests|FullyQualifiedName~StaffHoursTests"`; web dizininde `npm.cmd test -- BusinessHours.test.tsx StaffHoursEditor.test.tsx`. Yayında mevcut tam CI kapıları korunur.

## Hizmet tamponu API'si (P03-03A)

MFA Owner `GET /api/services/{id}/buffers` ile hizmet sürümü ve ön/arka tamponu okur; CSRF ile aynı yolun POST'u `version`, `bufferBeforeMinutes` ve `bufferAfterMinutes` alır. İki süre 0–120 tam dakikadır, eksik alan 400 ve eski sürüm 409'dur. Mevcut/yeni hizmette başlangıç 0; mevcut hizmet formu tampon ayarını sıfırlamaz. Tampon editörü veya yeni ekran bu işte yoktur.

Randevunun hizmet başlangıcı/bitişi değişmez; iki tampon snapshot'ı ile genişleyen dolu aralık uygunluk, mesai ve PostgreSQL GiST kontrolünde kullanılır. Yeni ayar geçmiş kaydı büyütmez; randevu okuması snapshot'ları ve dolu anları içerir. `AppointmentBuffers` migration'ı önceki blokları 0 tamponla korur. Tampon şeması eklendikten sonra eski P03-02 imajı yeni blok kolonlarını yazamaz; otomatik eski imaj/Down/restore kullanılmaz. Sıfır dışı tampon varsa Down ayrıca DB'de reddedilir.

İlgili regresyon: `dotnet test FirmaRandevu.slnx --no-restore --filter "FullyQualifiedName~AppointmentBufferTests|FullyQualifiedName~AppointmentStorageTests|FullyQualifiedName~AppointmentTests"`. [Kabul ve teslim](docs/plans/P03.md#p03-03a-hizmet-tamponu-kabulü--14102026).

## Haftalık mola API'si (P03-03B)

MFA Owner için GET/POST `/api/business-hours/breaks` ve `/api/staff-members/{id}/breaks`; POST `{version,days:[{day,breaks:[{startsAt,endsAt}]}]}` ile yedi günü birlikte gönderir. Saatlerle aynı parent sürümü kullanılır. Her gün en fazla 8 sıralı, kesişmeyen, aynı gün mesai içinde mola; boş liste o gün molayı kaldırır. Kapalı günde mola yoktur. CSRF ve mevcut Owner yetkisi gerekir; sürüm veya mevcut randevu/tampon kesişimi 409. Aynı değer yan etkisiz 200'dür.

Uygun saatler ve kayıt, işletme/personel molalarını tampon dahil aralıktan çıkarır. Mesai daraltması mevcut molayı dışarıda bırakırsa `breaks_outside_hours` ile 409; mevcut saat formları açıklama gösterir ve düzeltilebilir kalır. Bu teslim mola düzenleme ekranı, tarihli izin/istisna veya müşteri rezervasyon ekranı içermez.

`WeeklyBreaks` eklemeli migration'ı iki boş tablo ve eski saat audit'lerine başlangıç `Hours` türü ekler. Mola yapılandırması veya `Breaks` audit'i varken Down reddedilir. Eski imaj yeni molaları uygulamadığından yeni şemada otomatik eski imaj/Down/restore kullanılmaz. İlgili regresyon: `dotnet test FirmaRandevu.slnx --no-restore --filter "FullyQualifiedName~WeeklyBreakTests|FullyQualifiedName~AppointmentStorageTests"`. [Kabul ve sınırlar](docs/plans/P03.md#p03-03b-haftalık-mola-kabulü--14102026).

## Tarihli takvim ve izin API'si (P03-03C)

MFA Owner GET/POST `/api/business-hours/exceptions/{yyyy-MM-dd}` ve `/api/staff-members/{id}/exceptions/{yyyy-MM-dd}`; POST `{version,isClosed,opensAt,closesAt,breaks:[{startsAt,endsAt}]}` tam günü değiştirir. Kapalı günde saatler null ve mola listesi boş gerekir. Aynı yolun `/remove` POST'u `{version}` ile haftaya döner; mevcut randevuyu bozan dönüş 409. İstisna yalnız kendi kaynağının haftalık saat/molasının yerine geçer; işletme ve personelin etkin günleri birlikte uygulanır.

Personel izni: GET `/api/staff-members/{id}/leave?from=<offsetli-an>&to=<offsetli-an>&page=1&pageSize=50`; aralık en fazla 366 gün, sayfa boyutu 1–50, sıralama UTC başlangıç/kimlik. URL'de `+` işaretini `%2B` olarak kodlayın. POST aynı yola `{id,version,startsAt,endsAt}`; düzenleme POST `/{leaveId}` ile `{version,startsAt,endsAt}`, kaldırma POST `/{leaveId}/remove` ile `{version}`. Sonlu tam dakika, başlangıç < bitiş ve en fazla 366 gün; kesişen izin 409, bitişik izin geçer. Oluşturma kimliği ilk içeriğin özetiyle korunur; aynı içerikle tekrar yan etkisiz 200, farklı içerik veya kaldırılmış kimlik 409. Kaldırma kayıt kimliğini korur; GET listesine çıkarmaz. İzin açık istisnayla aşılmaz; iki tampon dahil kayıt ve uygunluk aynı kurala bağlıdır. Parent sürümü/CSRF/yetki zorunlu; eski sürüm veya mevcut randevu kesişimi 409.

`DatedCalendar` beş boş tablo ve audit türleri ekler; eski kolonlar/Kind değerleri korunur. Tarihli tanım, izin kimliği veya yeni audit geçmişi varsa Down reddedilir; eski imaj kuralları bilmediğinden otomatik eski imaj/Down/restore kullanılmaz. Yeni ekran/menü yoktur. İlgili regresyon: `dotnet test FirmaRandevu.slnx --no-restore --filter "FullyQualifiedName~DateCalendarTests|FullyQualifiedName~WeeklyBreakTests|FullyQualifiedName~AppointmentStorageTests"`. [Kabul ve sınırlar](docs/plans/P03.md#p03-03c-tarihli-takvim-kabulü--15102026).

P03-03D birleşik kabulü: `dotnet test --project tests/Server.Tests/Server.Tests.csproj --no-restore --filter-class Server.Tests.DateCalendarTests Server.Tests.AppointmentStorageTests`. Mola/istisna/izin/tampon, farklı Owner yarışları, doğrudan DB kısıtları, 429/timeout ve sentetik restore kanıtı [P03 planında](docs/plans/P03.md#p03-03d-birleşik-kabul--15102026). D yalnız kabul/test değişikliğiydi; sonraki P03-04A uygulaması aşağıdadır.

## Owner iptal ve durum API'si (P03-04A)

MFA Owner + CSRF: `POST /api/appointments/{id}/cancel`, `/complete`, `/no-show`; gövde `{ "version": "UUID", "requestKey": "UUID" }`. Confirmed başlangıçtan önce iptal, başlangıç anı dahil Completed/NoShow; geçmiş durum sıradan iptalle açılmaz. 200 başarı/aynı komut tekrarı, 400 alan/CSRF, 401/403 yetki, 404 eksik kayıt, 409 sürüm/durum/zaman/anahtar uyuşmazlığı, 429 limit. Aynı başarılı anahtar içerik değişirse reddedilir; timeout sonrası yeni anahtar üretmeden aynı istek tekrar edilir.

`AppointmentStatus` eklemeli migration'ı makbuz tablosu ve audit türü ekler; eski kolonlar korunur. Tarihçe varsa Down reddedilir. Ana ortamda otomatik eski imaj/Down/restore yoktur. P03-04A ile eklenen app_user yetkileri sürüm/durum kolonlarıyla sınırlıdır; snapshot ve append-only kayıtları değiştiremez. İlgili test: `dotnet test --project tests/Server.Tests/Server.Tests.csproj --no-restore --filter-class Server.Tests.AppointmentStatusTests Server.Tests.AppointmentStorageTests Server.Tests.AppointmentTests`. [Kabul/geri dönüş](docs/plans/P03.md#p03-04a-owner-iptal-ve-durum--15102026). Yeni ekran, taşıma veya Pending işçisi bu alt işte yoktur.

## Owner atomik taşıma API'si (P03-04B)

MFA Owner için `GET /api/appointments/{id}/move-availability?staffMemberId=UUID&date=yyyy-MM-dd`; güncel randevu sürümü, Europe/Istanbul ve uygun başlangıçlar döner. Mevcut randevunun kendi bloğu hariç tutulur, ilk hizmetin süre/tampon değerleri kullanılır. Yeni rezervasyon önizlemesi güncel hizmet değerlerini kullandığından taşıma için bu ayrı önizleme gerekir.

CSRF ile `POST /api/appointments/{id}/move`; gövde `{ "version": "UUID", "requestKey": "UUID", "staffMemberId": "UUID", "startsAt": "2026-10-16T11:00:00+03:00" }`. Yalnız başlamamış Confirmed taşınabilir. İlk fiyat/süre/tampon/hizmet/müşteri bilgisi korunur; hedef personel, yetkinlik ve güncel politika/takvim yeniden doğrulanır. Zaman çakışması 409 ve eski kayıt korunur. Aynı başarılı istek tekrarında mevcut kayıt döner; farklı içerik/eski sürüm 409. Aynı saat/personel yan etkisizdir fakat işlem anahtarı ayrılır. 400/401/403/404/429 ve 15 saniye timeout sözleşmesi mevcut randevu uçlarıyla aynıdır. Yeni ekran yoktur.

İlgili gerçek PostgreSQL kapısı: `dotnet test --project tests/Server.Tests/Server.Tests.csproj --no-restore --filter-class Server.Tests.AppointmentMoveTests Server.Tests.AppointmentStorageTests`. Taşıma komutu veya Moved audit geçmişi varsa migration Down reddedilir; uyumlu ileri düzeltme kullanılır. Ana ortamda otomatik Down/restore yapılmaz.

## Bekleyen randevu ve süre sonu (P03-04C)

MFA Owner + CSRF ile `POST /api/appointments/pending` mevcut oluşturma DTO'sunu alır ve Pending üretir. Normal `/api/appointments/` Confirmed üretmeye devam eder. İki uç aynı oluşturma anahtarı alanını kullanır; aynı anahtarı diğer uçta kullanmak 409'dur. İlk yanıt ve tekrar, PostgreSQL'de kaydedilmiş aynı son anı döndürür. DTO'da `pendingExpiresAt` UTC son anıdır; normal Confirmed kayıtta null, Pending'den çıkan kayıtta tarihçe olarak korunur.

`POST /api/appointments/{id}/confirm` mevcut `{version,requestKey}` sözleşmesini kullanır; yalnız son anından ve başlangıcından önce Pending onaylanır. Son an dahil onay 409'dur. `/cancel` süresi dolmamış Pending'i de iptal eder; Pending taşınamaz/tamamlanamaz/NoShow yapılamaz. Snapshot ve dolu aralık onayda değişmez; yeni rezervasyon hesabı yapılmaz. Eski başarılı işlem anahtarı güncel kaydı döndürür, yeni/eski sürümle tekrar işlem 409 verir.

Bekleme son anı oluşturma +30 dakika, daha yakın başlangıç varsa başlangıç anıdır; başlamış Pending oluşturulmaz. Varsayılan 60 dakika ön sürede bekleme tam 30 dakikadır. Gerçek BackgroundService başlangıçta ve sonra 5 saniyelik aralıklarla, 15 saniyelik bütçede en fazla 20 kayıt seçer. Personel→randevu kilidi, SKIP LOCKED ve yeniden durum/son an kontrolüyle tek transaction Cancelled/yeni Version/Expired audit yazar. Sistem audit'inde ActorId null'dır; Owner'a mal edilmez. Hata veya restart kayıt kaybettirmez; hata metnine kişi/bağlantı verisi yazılmaz. DB sorgu hatasında 30 saniye beklenir; son anı dolan slot açık Cancelled geçişi yapılana kadar DB'de bloke kalır.

İlgili kapı: `dotnet test --project tests/Server.Tests/Server.Tests.csproj --no-restore --filter-class Server.Tests.AppointmentPendingTests Server.Tests.AppointmentStorageTests`. Migration yeni tablo açmaz; son an kolonu, filtreli indeks ve audit/durum CHECK'lerini ekler. Eski şemada Pending varsa tarih uydurmak yerine Up reddedilir; açık inceleme gerekir. Yeni Pending tarihçesi varsa Down reddedilir. Yeni ekran, anonim rezervasyon veya mesaj gönderimi yoktur. [Kabul/geri dönüş](docs/plans/P03.md#p03-04c-pending-onay-ve-süre-sonu--15102026).

### Rezervasyon ayarları Owner API'si (P03-05A)

MFA Owner `GET /api/booking-policy/` ile sürüm, ön süre (dakika), ileri rezervasyon süresi (gün), başlangıç adımı (dakika), saat bölgesi ve P04-07A'nın `customerCancellationLeadMinutes`/`requiresBusinessApproval` alanlarını okur. CSRF korumalı `POST /api/booking-policy/` sürüm ve beş ayarın tamamını ister; eksik boolean dahil 400 olur. Ön süre 0–43200 dakika, ufuk 1–365 gün, adım 1–60 dakika; ön süre ufuktan kısa olmalıdır. İptal süresi 0–43200 tam dakikadır; 0, randevu başlamadan iptal sınırını ifade eder. Başlangıç 60/30/15 ve 120 dakika/işletme onayıdır. Güncel aynı değer no-op, eski sürüm 409; belirsiz sonuçta GET ile güncel kayıt okunur. Zaman ayarları yeni oluşturma/taşıma ve uygunluğu etkiler; mevcut randevu ve Pending son anı değiştirilmez.

Politika/audit aynı transaction'da yazılır; işlem mevcut “Değişiklik kayıtları” ekranının Tanımlar kategorisinde görünür. Yeni `BookingPolicyAudits` tarihçesi varken migration Down reddedilir; uyumlu ileri düzeltme gerekir. Yeni ayar ekranı, müşteri iptal sınırı/onay tercihi veya anonim rezervasyon bu alt işte yoktur. Build sonrasında ilgili kontroller: `dotnet run --project tests/Server.Tests/Server.Tests.csproj --no-build -- -class Server.Tests.BookingPolicyTests -class Server.Tests.AppointmentStorageTests -class Server.Tests.AuditLogTests`; web dizininde `npm.cmd test -- AuditLog.test.tsx`. [Kapsam ve kabul](docs/plans/P03.md#p03-05a-mevcut-rezervasyon-ayarları--15102026).

### Rezervasyon ayarları ekranı (P03-05C)

MFA Owner, İşletme → Rezervasyon ayarları üzerinden ön süre, ileri rezervasyon süresi ve başlangıç aralığını düzenler. P04-07A genişletmesi iptal süresini ve iki açıklamalı onay seçeneğini aynı forma/tek Kaydet işlemine ekler. Sayfa `/yonetim/rezervasyon-ayarlari`; mevcut GET/POST kullanılır. Çakışmada veya belirsiz kayıt sonucunda taslak korunur ve yeniden kaydetmeden önce güncel ayarlar yüklenir. Staff menüsü/API yetkisi genişletilmez.

İlgili frontend kontrolü `src/Web` içinde `npm.cmd test -- BookingPolicy.test.tsx URLNavigation.test.tsx ManagementLayout.test.tsx`; ayrıca typecheck/lint/tüm web testleri/build. [Onaylı taslak ve gerçek kabul](docs/plans/P03.md#p03-05c-rezervasyon-ayarları-görsel-taslağı--15102026).

### Müşteri politika ayarları (P04-07A)

`customerCancellationLeadMinutes` ve `requiresBusinessApproval` kamu katalog `policy` görünümünde de döner. Bu ayar teslimi müşteri rezervasyon yazımını, OTP/SMS'i veya iptal bağlantısını açmaz; Owner Confirmed/Pending oluşturma davranışı ve mevcut randevular korunur. Müşteriye gösterilen koşulun randevuda snapshot olarak saklanması sonraki müşteri yazma dilimidir.

`20261016222045_CustomerBookingPolicy` yalnız iki mevcut tabloya alan/CHECK ekler. `BookingPolicies` başlangıcı 120/true, eski `BookingPolicyAudits` yeni alanları null kalır; geçmiş koşul uydurulmaz. Yeni kayıtlar audit'e iki değeri birlikte yazar. `app_user` yalnız izinli politika kolonlarını günceller; audit append-only kalır. Yeni politika değeri veya yeni alanlı audit varsa Down reddedilir. Geri dönüşte şema Down kullanılmaz; eski imaj uyumu önce sentetik olarak doğrulanmalı, tercihler/tarihçe korunmalıdır.

Backend kapısı: `dotnet run --project tests/Server.Tests/Server.Tests.csproj --no-build -- -class Server.Tests.CustomerBookingPolicyTests -class Server.Tests.BookingPolicyTests -class Server.Tests.PublicBookingTests` (önce kilitli restore/build). [Kapsam, kabul ve ana teslim kaydı](docs/plans/P04.md#p04-07a--rezervasyon-ayarları-brief-ve-görsel-taslağı--17102026).

### Son motor kabulü (P03-06)

Locked restore/build sonrasında depo kökünde mevcut motor ve bağlı mesai/hizmet testleri:

```powershell
dotnet run --project tests/Server.Tests/Server.Tests.csproj --no-build -- -class 'Server.Tests.Appointment*' -class Server.Tests.BookingPolicyTests -class Server.Tests.DateCalendarTests -class Server.Tests.WeeklyBreakTests -class Server.Tests.BusinessHoursTests -class Server.Tests.StaffHoursTests -class Server.Tests.StaffServiceAssignmentTests -class Server.Tests.ServiceDefinitionTests
```

Bu komut gerçek PostgreSQL/Testcontainers için çalışan Docker ister. Paralel API/DB yazımları, snapshot/idempotency, takvim/politika yarışları, durum/Pending, yetki ve migration korumaları [P03-06 kabul kaydında](docs/plans/P03.md#p03-06-son-motor-kabulü--16102026) izlenir. Yeni müşteri ekranı veya anonim rezervasyon kabulü değildir; yayın için son commit'in tam CI kapıları da zorunludur.


### Abonelik okuma API temeli (P07-02)

`GET /api/subscription?page=1&pageSize=12` yalnız Owner+MFA içindir; en fazla24 kayıt, no-store ve bağımsız30/dakika kota. TRY tutarlar ondalık metin, `EndsOn` hariç bitiş; İstanbul `asOf`, güncel dönem ve sayfalı dönemler döner. Kayıt yok/gecikmiş durumda aboneliğe bağlı kısıtlama uygulanmaz. Operatör referansı/not/hash/kimliği yanıtta yoktur. Normal uygulama DB rolü iki yeni tabloya yalnız SELECT alır; public yazma API'si, operatör MFA/ekranı ve Owner UI bağlantısı henüz yoktur.

Yeni mali veri testleri depo kökünden (Docker açıkken):

```powershell
dotnet run --project tests/Server.Tests/Server.Tests.csproj --no-build -- -class Server.Tests.SubscriptionTests -class Server.Tests.SubscriptionLedgerTests
```

Önce yukarıdaki mevcut restore/build kapıları çalışmış olmalıdır. Eklemeli migration ve `Apply-LocalMigration.ps1` yeni tabloları/grant'leri hazırlar; gerçek kurulumda yedek ve kontrollü migration sırası geçerlidir. Kayıt varsa Down veri silmez, reddeder; imaj geri dönüşünde ek şema korunur. [Kabul ve sınırlar](docs/plans/P07.md#p07-02-kabul-ve-yedekli-ana8080-teslimi).
