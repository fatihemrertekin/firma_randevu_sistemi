# ADR-001 — Başlangıç teknolojisi

Durum: kabul edildi
Tarih: 2026-09-30

## İhtiyaç

Tek firmaya ait kurulumu basit tutarken çakışmasız randevu, yetkili erişim, mobil web ve ölçülebilir bakım maliyeti üretmek. İlk satılabilir sürümde AI, WhatsApp ve salon adına kart tahsilatı yoktur.

## Alternatifler

1. ASP.NET Core + PostgreSQL + React/TypeScript: güvenilir sunucu sınırı, veritabanı zaman kısıtları ve etkileşimli takvim; iki dil/derleme zinciri bedeli.
2. ASP.NET Core + Razor/Blazor: daha az teknoloji; yoğun takvim ve mobil arayüz için ekip deneyimi ve bileşen seçimi ayrıca değerlendirilmeli.
3. Node.js/TypeScript ile tek dil: dil sayısı azalır; mevcut ürün sözleşmesindeki Identity/EF Core ve C# yönü değişir, kararın geri dönüş bedeli yüksektir.

## Karar

Tek ASP.NET Core 10 LTS uygulaması, EF Core 10/Npgsql 10 ve PostgreSQL 18; arayüz için React, TypeScript strict, Vite ve CSS Modules. Statik web çıktısı ASP.NET ile aynı origin'den sunulur. Node yalnız build/test aracıdır. İlk hacimde ayrı API servisi, Redis veya mesaj kuyruğu eklenmez. Zaman çakışmasının son güvencesi P03'te PostgreSQL constraint ve transaction'dır.

Resmi durum 30.09.2026: [.NET 10 SDK 10.0.401 / runtime 10.0.12](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [EF Core 10 desteği](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew), [Npgsql EF provider 10](https://www.npgsql.org/efcore/release-notes/10.0.html), [PostgreSQL 18.6](https://www.postgresql.org/support/versioning/) ve [Node 24 LTS](https://nodejs.org/en/about/previous-releases) doğrulandı. Paketlerin kesin patch sürümleri, lock dosyaları ve imaj etiketleri P01 uygulamasında sabitlenir. Üretim güncellemesinde yeniden doğrulanır.

## Bedel ve takip

- C# ve TypeScript birlikte öğrenme, test ve CI süresini artırır. Takvim arayüzü için React build zinciri, backend'in tek başına sunduğu sayfalardan daha fazla bakım ister. Kullanıcının C# öğrenme hedefi seçimde tek belirleyici değildir.
- EF Core sorgularında N+1 ve fazla veri çekme; PostgreSQL bağlantı/indeks tasarımı ve ölçüm gerekir. Hiçbir stack tek başına hız garantisi vermez. Performans P03/P04 yük ve uçtan uca ölçümleriyle doğrulanır.
- Yerel geliştirmede Docker ve PostgreSQL kaynak tüketir. Tek backend ve statik web, üretimde ikinci bir Node sunucusunun işletim maliyetini kaldırır.
- ASP.NET Core [MIT](https://github.com/dotnet/aspnetcore), Npgsql [PostgreSQL lisansı](https://github.com/npgsql/npgsql) ve Vite [MIT](https://github.com/vitejs/vite/blob/main/packages/vite/LICENSE.md) kaynakları incelendi. Diğer doğrudan ve transitif paketler lock/restore sonrasında ayrıca taranır; ücretli/belirsiz lisans fark edilirse karar görünür kılınır.
- Somut pilot tarihi verilmediği için öğrenme eğrisinin takvime gün olarak etkisi hesaplanamaz. P01 iş süreleri ve sonraki aşama kanıtıyla tahmin güncellenir.
