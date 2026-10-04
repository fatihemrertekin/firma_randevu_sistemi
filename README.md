# Firma Randevu Sistemi

Türkiye'deki tek şubeli hizmet işletmeleri için planlanan markalı randevu uygulaması. P01 temel kurulum tamamlandı; P02'de Owner oturumu eklendi. Gerçek rezervasyon henüz yoktur. Güncel durum için [STATUS](docs/STATUS.md), ürün kararları için [PRODUCT](docs/PRODUCT.md), aşamalar için [ROADMAP](docs/ROADMAP.md).

Frontend `src/Web/src/features/auth`, `features/business` ve `features/staff` altında mevcut özelliklere göre düzenlenir. `app` oturum API yardımcısını ve yönetim yerleşimini, `components` ortak bileşenleri, `styles` ortak renk/form kurallarını barındırır. Testler ilgili özelliğin yanındadır. Yönetim ekranında işletme bilgileri, hesap ve güvenlik, çalışan erişimleri ayrı bölümlerdir; çalışan hesabında yalnız hesap ve güvenlik görünür.

Giriş ve kimlik formlarının görsel sistemi [DESIGN](DESIGN.md) içinde tutulur. Projeye kurulu [Impeccable](.agents/skills/impeccable/SKILL.md) yeni ekranlarda önce taslak/onay akışını kullanır. Seçilen salon fotoğrafı `assets/plates/` altında üretim kaydıyla saklanır; Docker derlemesine dahildir. Yerel Unna Bold fontu SIL Open Font License 1.1 ile kullanılır; [lisansı](src/Web/src/assets/fonts/Unna-OFL.txt) imajda `/app/Unna-OFL.txt` olarak da bulunur. Yerel motor, önbellek ve inceleme çıktıları sürümlenmez; Windows başlatıcısı motoru ilk kullanımda doğrulayarak indirir.

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

## İşletme logosu (P02-22)

MFA Owner, **İşletme logosu** bölümünde PNG/JPEG dosyasını seçip önizleyerek kaydeder. En fazla 1 MiB ve 2048 × 2048 piksel; sunucu dosyayı yeniden PNG oluşturur ve oranı korunarak en fazla 512 × 512 piksele küçültür. SVG/HTML, animasyon, URL veya logo silme yoktur. Logo giriş ve yönetimde herkese görünür; logo API'si hesap/iletişim verisini açmaz. Dosya seçmek kaydetmez; eski sürüm reddedilir ve aynı güncel görüntü ikinci audit yaratmaz.

`BusinessLogo` migration'ı ayrı logo/audit tablolarını ve boş logo satırını ekler. Logo DB yedeğine dahildir; dosya volume'u gerekmez. Kontrollü migration ve app_user yetkileri yukarıdaki komutla uygulanır. `Down` logo/audit verisini siler; ana ortamda otomatik yapılmaz. SkiaSharp/native paketleri kilitlenir; [lisans bildirimleri](docs/logo-image-licenses.txt) imaja dahildir. Kabul/geri dönüş [P02 planında](docs/plans/P02.md#p02-22--işletme-logosu); kalite kapıları yukarıdadır.

## Değişiklik kayıtları (P02-23)

MFA Owner, **Değişiklik kayıtları** bölümünde mevcut tanım, çalışan erişimi ve kurtarma kayıtlarını tarih/işlem/yapan hesap/ilgili kayıt olarak görür. Tümü/Tanımlar/Hesap ve güvenlik kategorileri, yenileme ve önceki/sonraki sayfa vardır. Saatler İstanbul bölgesinde, hesap ve hedef adları güncel kayıtlardan gösterilir; yerel kurtarma işlemlerinin aktörü “Yerel bakım”dır. Eski/yeni değerler, giriş geçmişi, kodlar, silme, dışa aktarma veya yeni audit yazımı eklenmez.

`GET /api/audit-log/` yalnız MFA Owner içindir; no-store, 30/IP/dakika ve kategoriye bağlı cursor kullanır. Varsayılan sayfa 20 kayıt; sunucu sınırı 1–50'dir. `AuditLogIndexes` migration'ı yalnız 12 mevcut audit tablosuna sıralama indeksi ekler. Uygulama DB yetkileri değişmez; Down yalnız bu indeksleri kaldırır, kayıt silmez. Kontrollü yedek/migration ve kabul ayrıntıları [P02 planında](docs/plans/P02.md#p02-23--değişiklik-kayıtlarını-görüntüleme).

İlgili kontroller: kökten `dotnet test FirmaRandevu.slnx --no-restore --filter FullyQualifiedName~AuditLogTests`; web dizininde `npm.cmd test -- AuditLog.test.tsx`. Yayında yukarıdaki tüm kalite kapıları çalıştırılır.
