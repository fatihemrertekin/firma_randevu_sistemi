# Firma Randevu Sistemi

Türkiye'deki tek şubeli hizmet işletmeleri için planlanan markalı randevu uygulaması. P01 temel kurulum tamamlandı: başlangıç sayfası ve sağlık kontrolleri vardır; gerçek rezervasyon henüz yoktur. Güncel durum için [STATUS](docs/STATUS.md), ürün kararları için [PRODUCT](docs/PRODUCT.md), aşamalar için [ROADMAP](docs/ROADMAP.md).

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
Invoke-WebRequest http://127.0.0.1:8080/health/live
Invoke-WebRequest http://127.0.0.1:8080/health/ready
```

Tarayıcıda `http://127.0.0.1:8080/` adresini açın. İlk komut yalnız yerel geliştirme için ayrı, rastgele iki veritabanı parolası oluşturur. `deploy/.env` Git'e eklenmez; komutu tekrar çalıştırmak mevcut dosyayı değiştirmez. Docker Compose veritabanını named volume'da tutar; sıradan `down` komutu bu veriyi silmez.

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
dotnet build FirmaRandevu.slnx --no-restore
dotnet test FirmaRandevu.slnx --no-restore
dotnet format FirmaRandevu.slnx --verify-no-changes --no-restore
docker compose --env-file deploy/.env -f deploy/compose.local.yaml config --quiet
```

`GET /health/live` yalnız uygulamanın yanıt verdiğini, `GET /health/ready` PostgreSQL bağlantısının açılabildiğini gösterir. Her iki uç da müşteri verisi veya sır döndürmez.

## Yapı

- `src/Server`: ASP.NET Core uygulaması; derlenmiş web dosyalarını aynı origin'den sunar.
- `src/Web`: React/TypeScript başlangıç ekranı; `npm run build` çıktısı `src/Server/wwwroot` içine gider.
- `tests/Server.Tests`: sağlık uçları ve statik web sunumu için xUnit testleri.
- `deploy`: yerel Compose, Dockerfile ve örnek ayarlar.
- `docs`: ürün, yol haritası, aktif aşama ve teknoloji kararı.

Şu anki ekranda rezervasyon işlemi yoktur; kimlik ve randevu akışları sonraki aşamalardır.
