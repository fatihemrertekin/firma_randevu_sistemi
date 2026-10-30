# ADR-012 — Merkezi abonelik panelinde açık firma hedefleri

Tarih: 30.10.2026. Durum: **accepted; kullanıcı “Modeli ve ekranı onaylıyorum” yanıtını verdi.** Bağlantı ve ekran kararı onaylıdır; ürün uygulaması küçük bağımlı dilimlerle ilerler. Onay yeni gerçek uzak erişim veya VPS kurulumu değildir. [ADR-011](011-subscription-record-authority.md) ile kabul edilen tek firma operatörü çalışmaya devam eder.

## İhtiyaç ve alternatifler

Ürün sahibi bilgisayar/telefondan tek özel panelle birden fazla firmanın dönem ve ödemesini yönetmek istiyor. Firma başına ayrı uygulama/DB/anahtar/yedek ilkesi korunmalıdır. Mevcut operatör tek yapılandırılmış hedefe bağlanır; firma seçimi ve toplu özet API'si henüz yoktur.

| Seçenek | Bedel | Öneri |
| --- | --- | --- |
| Mevcut dar DB rolünü yalnız özel ağdaki açık hedeflere genişletmek | Merkezi operatörün ele geçirilmesi bağlı firmaların abonelik kayıtlarını etkileyebilir; ağ/sertifika/sır yönetimi gerekir | İlk dar sürüm için önerilen |
| Her firmaya ayrı abonelik HTTP servisi ve servis kimliği eklemek | Ek servis, kimlik doğrulama, anahtar yenileme, sürüm ve dağıtım kabulü gerekir | Şimdilik eklenmesin |
| Merkezi mali DB ve firmalara erişim durumu senkronizasyonu | İki doğruluk kaynağı, kuyruk, kesinti ve olay sırası sorunu yaratır | İlk sürümde kullanılmasın |

## Onaylanan model

- Bir merkezi operatör host'u, ayrı operatör Identity DB'si, zorunlu MFA, host-only cookie ve kendi Data Protection anahtarları. Firma Owner/Staff kimlikleri kullanılmaz. İlk hesap kontrollü bootstrap ile açılır; mevcut tek firma hesabı/kimlik DB'si sessizce taşınmaz veya birleştirilmez.
- Sunucuda önceden tanımlanan izinli hedefler: değişmez kurulum ID'si, görünen firma adı, özel DB adresi ve firma başına ayrı sır. İlk sürümde panelden sunucu ekleme/parola girme yoktur. Tarayıcı yalnız hedef ID'sini gönderir; host/port/SQL/connection string seçemez. Sırlar Git'e, frontend'e, loga veya özet DTO'suna girmez.
- Mevcut `subscription_operator` yetkisi korunur: iki abonelik tablosunda SELECT/INSERT ve dönem Version alanında UPDATE. Randevu, müşteri ve firma kimlik tablolarına erişim yoktur. Her açılan bağlantıda dar rol ve kalıcı `app.subscription_instance` işaretçisi seçili kurulumla eşleşir. Hata halinde başka firmaya/varsayılan hedefe dönüş yapılmaz. Normal firma uygulamasına başka firma ağı/sırrı verilmez.
- Aynı VPS'te firmaların ayrı özel ağlarına yalnız operatörün ihtiyaç duyduğu kontrollü erişim verilebilir; firma konteynerleri birbirine bağlanmaz. Başka VPS gerektiğinde özel yönlendirme/VPN ve doğrulanmış DB TLS gerekir; DB portu internete açılmaz. Uzak Npgsql bağlantısında `SSL Mode=VerifyFull`, güvenilen kök ve doğru host adı kabul şartıdır; sertifika kontrolünü kapatan callback kullanılmaz. Ağ, firewall, sertifika/yenileme ve sır döndürme işlemleri P06'nın somut dağıtım kabulüne aittir; bugün VPN/sertifika/sunucu kurulmaz.
- Dönem ve ödeme için tek doğruluk kaynağı ilgili firmanın DB'sidir. Merkezde ikinci ödeme defteri veya kalıcı erişim önbelleği yoktur. Firma uygulaması kısıtlamayı kendi DB'sinden hesaplamaya devam eder; operatör panelinin kesilmesi firmanın erişimini kendiliğinden değiştirmez.
- Çok hedefli yazmada URL'deki ve DTO'daki hedef uyuşur; hedef listede yoksa sorgu bile açılmadan reddedilir. Dönem/ödeme/ters kayıt/düzeltme o hedefin transaction'ı ve mevcut 70703 kilidiyle yürür. Anahtar/içerik doğrulaması seçili firma içinde kalır; aynı anahtar başka firmada sonuç aramak için kullanılamaz. Firma değişiminde açık form korunur/uyarılır; bekleyen veya sonucu belirsiz yazma bitmeden hedef değişmez. Belirsiz POST yalnız aynı hedef/anahtar/içerikle çözümlenir.
- Özet, aynı firmanın tek tutarlı snapshot'ından son başlamış dönemi, bütün başlamış gecikmiş dönemlerin toplam borcunu ve mevcut erişim kuralını verir. Bu ayın ödenmesi eski borcu gizlemez. Başlangıçta sayfada en fazla 12 hedef, en fazla 3 eşzamanlı bağlantı; hedef açma 5 saniye, liste isteği toplam 15 saniye ile sınırlı önerilir. Havuz sınırı firma başına ve toplam hedef sayısıyla birlikte ölçülür; sınırsız fan-out yoktur. Bunlar ölçülmüş üretim kapasitesi iddiası değildir.
- Bir firma yanıt vermezse yalnız o satır `unavailable`/“bilinmiyor” olur; sıfır borç, ödendi veya kapalı varsayılmaz. Kısmi liste diğer firmaları kullanılabilir tutar. Borç/erişim filtreleri sunucuda izinli hedef sayfası üzerinde uygulanır; “bilinmiyor” ayrı bir durumdur ve arayüz gösterdiği kapsamı açıklar. Yanıtta kontrol zamanı bulunur; mali form açma ve kaydetme seçili firmadan tekrar doğrular.

## Güven sınırı, maliyet ve geri dönüş

Merkezi operatör yüksek güvenli yönetim bileşenidir: MFA ve dar rol, bağlı bütün firmaların abonelik kayıtlarına erişebilmesi riskini kaldırmaz. Ayrı VPS isteyen firmanın izolasyonu ayrıca değerlendirilir. Operatöre Docker socket, genel SQL/terminal, müşteri/randevu sorgusu, restore veya gizli admin yetkisi verilmez. Kurulum/domain satın alma, banka entegrasyonu ve elle erişim açma düğmesi kapsam dışıdır.

Yeni ücretli hizmet/paket seçilmedi. Gerçek ağ/sertifika, izleme, yedek ve operatör sırrı bakım maliyeti pilot öncesi ölçülür. Uygulama aşamasında eski tek firma config'iyle uyumluluk açıkça test edilir; birden fazla hedef varsa sessiz varsayılan seçilmez. Merkezi servisi durdurmak ve o servis için verilen ağ/rol erişimini kaldırmak geri dönüş yoludur; firma verisi/ödeme defteri/Identity anahtarları silinmez. Ayrı çalışan tek firma operatörünü aynı anda ikinci yazıcı olarak açmak kontrollü kabul olmadan önerilmez.

## Kabul kapısı ve kaynak

Bağlantı modeli ve [P07-06 görsel/kabul planı](../plans/P07.md#p07-06--merkezi-çok-firma-abonelik-yönetimi-karar-taslağı--30102026) onaylandı. Ardından ayrı sentetik A/B firma DB'lerinde yanlış marker/rol/route, A→B sızıntısı, paralel mali kayıt ve kısmi bağlantı kesintisi gerçek PostgreSQL ile kanıtlanır. Yerel taslak veya önceki tek firma testleri çok firma kabulü değildir.

P07-07 somut sınırı: yeni çoğul hedef yapılandırması Production'da aynı VPS dahil `VerifyFull` zorunlu tutar; GSS fallback yerine TLS yolu seçilir. Yalnız Development'ta sayısal loopback sentetik DB istisnadır; localhost/DNS adı istisna sağlamaz. Eski tek firma ayarı geriye uyumlu kalır. Çok hedefte readiness yalnız merkezi kimliği doğrular; firma bağlantı durumunu vaat etmez. İlk katalog API'si yalnız ad/kurulum kodu döndürür; ödeme/erişim özeti veya çok firma yazma API'si değildir. Eski mali rotalar birden fazla hedefte 409 ile seçim ister; ilk firmaya düşmez. P07-08 seçili firma mali API kabulü ayrı iş olarak kalır.

TLS seçiminin resmi dayanağı: [Npgsql Security and Encryption](https://www.npgsql.org/doc/security.html), [PostgreSQL 18 SSL Support](https://www.postgresql.org/docs/18/libpq-ssl.html), 30.10.2026 kontrolü. Npgsql `Require` yalnız şifreleme sağlar; `VerifyFull` sunucu kimliği/host doğrulamasını da sağlar. Özel ağ ile bu doğrulama birbirinin yerine sayılmaz.
