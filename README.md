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

Tarayıcıda `http://127.0.0.1:8080/` adresini açın. İlk girişten sonra doğrulayıcı uygulamada anahtarı girip altı haneli kodla iki aşamalı girişi açın; ekranda bir kez gösterilen kurtarma kodlarını güvenli yerde saklayın. Sonraki girişlerde parola ile birlikte uygulama kodu veya kullanılmamış bir kurtarma kodu gerekir. Doğrulayıcı ve tüm kurtarma kodları kaybolursa bu aşamada self servis hesap kurtarma yoktur.

İlk komut yalnız yerel geliştirme için ayrı, rastgele iki veritabanı parolası oluşturur. `deploy/.env` Git'e eklenmez; komutu tekrar çalıştırmak mevcut dosyayı değiştirmez. Migration komutu Identity tablolarını idempotent olarak ekler; uygulama kullanıcısına yalnız bu tabloların veri erişimini verir. Owner parolası komut satırına yazılmaz ve script sonunda geçici ortam değişkenleri temizlenir. Docker Compose veritabanını ve çerez anahtarlarını ayrı named volume'larda tutar; sıradan `down` bunları silmez.

Yerel Compose HTTP kullanır; güvenli çerez HTTPS gerektirdiğinden bu kurulum yalnız geliştirme içindir. Üretim kurulumu ve şifreli anahtar saklama P06'da doğrulanmadan gerçek firma verisiyle kullanılmaz. Migration uygulama başlangıcında kendiliğinden çalışmaz; canlı geçiş için ayrıca yedek ve ayrı migration yetkisi gerekir.

Servisleri durdurmak için:

```powershell
docker compose --env-file deploy/.env -f deploy/compose.local.yaml down
```

Bu Compose dosyası yalnız yerel geliştirme içindir. Gerçek firma kurulumu, yedek ve dağıtım P06'da hazırlanacaktır.

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

Şu anki ekranda rezervasyon işlemi yoktur; davet, parola sıfırlama, çalışan ve hizmet tanımları sonraki P02 işleridir.
