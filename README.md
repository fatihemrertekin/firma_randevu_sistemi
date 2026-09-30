# Firma Randevu Sistemi

Türkiye'deki tek şubeli hizmet işletmeleri için planlanan markalı randevu uygulaması. P01 temel kurulum tamamlandı; P02'de Owner oturumu eklendi. Gerçek rezervasyon henüz yoktur. Güncel durum için [STATUS](docs/STATUS.md), ürün kararları için [PRODUCT](docs/PRODUCT.md), aşamalar için [ROADMAP](docs/ROADMAP.md).

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
4. Owner giriş ekranında “Parolamı unuttum” formuna tokenı, yeni parolayı ve tekrarını girer. Token URL'ye veya kalıcı tarayıcı deposuna yazılmaz. POST, CSRF ve IP/hesap/global istek sınırlarıyla korunur. Token üretmek mevcut parolayı, oturumları veya MFA'yı değiştirmez.
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

Kod rollback'inde ek tablolar/Staff rolü korunabilir. `Down` audit/davet kayıtlarını ve Staff rolünü sildiği için gerçek kurulumda otomatik kullanılmaz; kabul edilmiş hesapların erişimini etkiler. Staff MFA/parola kurtarma/hesap kapatma sonraki ayrı onaylı işlerdir. Üretim HTTPS/teslim/izolasyon/yedek kabulü henüz bu yerel işin kanıtı değildir.
