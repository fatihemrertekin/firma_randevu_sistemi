# ROADMAP — Özellik haritası ve aşamalar

Hazırlanma: 2026-09-29. Bu dosya plandır; uygulanmış özelliğin kanıtı değildir. Gerçek durum docs/STATUS.md'dedir.

P00–P07 ilk satılabilir sürüm; P08–P17 sonraki plandır. İleri aşamayı talep/bütçe/bağımlılık sağlanınca seç. Her aşamayı küçük dikey işlere böl; tamamlanma kanıtını STATUS'a yaz. Yerel ilerleme için her geliştirme aşamasından sonra diğerine devam etmek için yaptığın geliştirmeleri not olarak ver ve devamı için kullanıcıdan onay iste. Aşağıdaki testler asgari kabul koşullarıdır.

## 1. Rakip araştırması ve özellik haritası

29.09.2026'da RandevuNet'in kamuya açık ana sayfası ile kuaför/güzellik sayfaları incelendi; oturum içi davranış ve pazarlama vaatleri test edilmedi. Aşağıdaki kısa envanter ürün kabiliyetlerini kapsar; teknik gereksinimlerimiz kendi tasarımımızdır. Kod, marka, metin, ekran görüntüsü veya özgün tasarım kopyalanmaz. Yeni incelemelerde özellik kaynağını ve tarihini bu dosyaya ekle.

| Referansta görülen kabiliyet | Bizde aşama |
| --- | --- |
| Takvim, durumlar, mesai | P02–P04 |
| Markalı rezervasyon, doğrulama | P04–P05 |
| Müşteri engelleme | P08 |
| Temel randevu Excel (.xlsx) ihracı | P04-45 yerel teslim;31 gün/5000 kayıt ve mevcut çalışan filtresi |
| Toplu aktarım, birleştirme, gelişmiş dışa aktarım | P08 |
| Değerlendirme, doğum günü | P08, P12 |
| Hatırlatma, değişiklik bildirimi | P05 |
| WhatsApp, sesli arama, kanal yedeği | P12 |
| Instagram mesajlaşması | P14 |
| Telefonla konuşarak rezervasyon | P17 |
| Borç, taksit, gider, raporlar | P09 |
| Paket/seans, prim, stok | P10 |
| Çok hizmet/personel, koltuk, şube | P10–P11 |
| İşlem izi, ayrıntılı yetkiler | P02, P11 |
| Rehber, kurulum adımları, destek | P07–P08 |
| Mobil, push, biyometri, rehber aktarımı | P15 |
| Hasta dosyası, laboratuvar, reçete | P16 |

Ücretsiz/reklamlı/sınırsız paketler, komisyon ve öncelikli destek rakibin ticari politikalarıdır; bizim modelimiz için docs/PRODUCT.md'ye bak.

## 2. Pilot kapsamı ve kesme sınırları

Pilot kapsamı [PRODUCT](PRODUCT.md#2-ürün-kararları) ile kararlaştırıldı; ayrıntılı kabul ve açık politikalar [P00 planında](plans/P00.md). Bu bölüm sözleşme değildir.

- P00–P07 tek kişi için büyüktür. Pilot için rezervasyon/panel, SMS OTP ve tek hatırlatma, yedek/restore, MFA, veri ihracı ve abonelik dönem/ödeme kaydı kararlaştırıldı; daha dar teknik iş dilimleriyle uygula. Salon müşterisinden kart tahsilatı ilk sürümde yok.
- İlk pilot tek firmayla başlar; Coolify/Compose şablonunun tam otomasyonu ilk firmadan sonra olgunlaştırılabilir. Yine de yedek/restore ve izolasyon kanıtı pilottan önce şarttır.
- WhatsApp ilk sürümde yoktur; ek yük ve maliyet nedeniyle P12'de isteğe bağlı gelecek iş olarak kalır. İlk hatırlatma kanalı SMS'tir; tekrar/kota/harcama tavanı P05'te uygulanır.

## 3. Aşamalar

### P00 — İş ve ürün tanımı

- İş: PRODUCT/ROADMAP/STATUS ve ayrıntılı P00 planı. Owner/Staff/misafir akışları, MVP ekranları ve kapsam dışı. Varsayılan: tek şube, TRY, Europe/Istanbul, aylık peşin.
- Çıkış: Pilot kabul listesi; iptal/manuel onay/destek politikaları. 3–5 işletme görüşmesi için soru/demo hazırla; ürün pilot için hazır olduğunda görüşmeleri ürün sahibi yürütür. Görüşme yapıldı veya talep doğrulandı diye uydurma. Görüşme beklerken sentetik teknik çalışma sürer; talep oluşmadan hosting satın alma.

### P01 — Temel ve CI (P00)

- İş: Sürüm/lisans kontrolü, ADR-001 (teknoloji seçimi ve bedeli), minimal çözüm, frontend derlemesi, PostgreSQL'li yerel Compose, örnek ayarlar, sağlık uçları, CI ve non-root Docker imajı. README ve AGENTS.md'deki komutlar somutlaşır.
- Çıkış: Temiz checkout çalışır; web aynı uygulamadan sunulur; DB restart'ta korunur; CI sır içermeyen imaj üretir. Boş servis/ekran yığını yok.

### P02 — Kimlik ve tanımlar (P01)

- İş: Davet/giriş/çıkış/sıfırlama, Owner/Staff, MFA, audit; profil, çalışan/hizmet, fiyat/süre, mesai ve yetkinlik. Logo özelliği ürün sahibinin 04.10.2026 kararıyla kapsamdan çıkarıldı.
- Çıkış: Anonim/yetkisiz API işlemi ve CSRF engellenir; tanımların fiyat/süre/aktiflik ve sürüm kuralları sunucuda uygulanır, silmek yerine pasifleştirme kullanılır. Açık kayıt/varsayılan geliştirme hesabı yoktur; test hesapları ayrı sentetik ortamda kalır. Yerel kapanış ve üretim/pilot sınırları [P02 kabul kaydında](plans/P02.md#p02-kapanış-kontrolü--04102026) ayrıdır.

### P03 — Randevu motoru (P02)

- İş: Uygunluk, mola/izin/istisna/tampon; oluştur/taşı/iptal/durum; constraint, concurrency, idempotency. [P03 planı ve kabulü](plans/P03.md) ile [ADR-005](adr/005-appointment-time-blocks.md) tek hizmet/personel Owner API'sini ve zaman bloğu modelini tanımlar. P03-03 A–D, P03-04A iptal/durum ve B atomik taşıma ana teslimi tamamlandı; P03-04C Pending geliştirme/GitHub/ana teslimi de tamamlandı; P03-04 A–C done. P03-05A ön süre/ufuk/adım Owner API'si geliştirme/GitHub/ana teslimi tamamlandı; P03-05 bütünü `in_progress`. P03-05B müşteri iptal/onay ürün kararları kaydedildi; GitHub teslim şartı planındadır. P03-05C mevcut üç ayar ekranının geliştirme/GitHub/ana yerel teslimi tamamlandı. P03-06 mevcut Owner motorunun yerel son kabulü tamamlandı; müşteri iptal/onay tercihlerinin çalışan uygulaması P04/P05 akışına bağlı açık iştir, P03 bütünü done değildir.
- 29.10.2026 güncel değerlendirme: iptal süresi/işletme onay tercihi ve misafir token/iptal uygulaması P04 içinde teslim edildi; eski P03 açık-uygulama ifadeleri tarihsel kayıttır. Genel faz/pilot kapanışı değildir. [P04-44 kaynak ve kanıt eşlemesi](plans/P04.md#p04-44--ilk-sürümün-zorunlu-eksiklerinin-kaynak-ve-kanıt-değerlendirmesi--29102026).
- Çıkış: Gerçek PostgreSQL'de aynı çalışan/zamana 20 farklı anahtarlı paralel istekten biri kaydolur. Bitişik slot, farklı çalışan, iptal sonrası boşalma, tampon, fiyat snapshot'ı, atomik taşıma ve izin ekleme yarışı testlidir. Pasif hizmet/personel yeni rezervasyonda seçilemez; mevcut P02 saat/yetkinlik tanımları uygunlukta uygulanır, geçmiş randevunun hizmet/fiyat/süre bilgisi korunur. Bunlar P02 tanım kabulüyle doğrulanmış sayılmaz.

### P04 — Rezervasyon ve panel (P03)

- İlk kapsam: 16.10.2026 onaylı çalışanlara göre günlük Owner yönetimi ve Yeni randevu/Ayrıntı/Taşıma formları. Günlük liste API ve dört yüzey geliştirme/GitHub/ana 8080 teslimi tamamlandı; [kanıt ve sınırlar](plans/P04.md#github-ve-ana-yerel-teslim). P04 bütünü ve müşteri politikaları açık kalır.
- İş: Markalı misafir sayfası/token yönetimi; gün/hafta görünümü, çalışan filtresi, form/sürükleme ile taşıma ve günlük özet.
- 28.10.2026 kapsam kararı: aylık görünüm ve müşteri kartı/geçmişi ürün sahibinin isteğiyle çıkarıldı; günlük/haftalık teslim korunur. [P04-43R güncel karar](plans/P04.md#p04-43r--müşteri-kartı-ve-geçmişinin-kapsamdan-çıkarılması--28102026): müşteri taslağı tarihsel kayıttır, uygulama/görsel onay adayı değildir. Kalan özellik satırları ihtiyaç doğrulanmadan otomatik başlamaz.
- P04-45 gerçek Excel ihracı yerelde teslim edildi; kullanıcı CSV istemedi. Tek sonraki aday P07-01 B2B abonelik dönem/ödeme/erişim kaydının dar kapsam ve görsel taslağıdır (`planned`, başlamadı). Sürükleme/ayrı günlük özet ihtiyaç doğrulanmadan başlamaz.
- Çıkış: Mobil rezervasyon panelde yönetilir; çift tıklama çoğaltmaz; çakışan taşıma eski kaydı kaybettirmez. Telefon/ID tahmini geçmişi açmaz. Playwright akışı geçer.

### P05 — Doğrulama/bildirim (P04)

- 23.10.2026: P05-08 gönderim kapalı tek hatırlatma geliştirme/GitHub/ana 8080 teslimi tamamlandı; hedef **3 saat önce**, tek iş ve ortak bütçe. [Kabul/geri dönüş](plans/P05.md#p05-08-kabul-ve-ana-8080-teslimi). Ücretli gerçek kabul proje sonunda, normal SMS/ret ve UI görsel kapanışı ayrı kapılardır; P05 bütünü done değildir.
- 20.10.2026 sıra kararı: ücret gerektiren P05-05 gerçek SMS/OTP kabulü proje geliştirmelerinin sonuna ertelendi; kapanışta hatırlatılıp güncel maliyet/alıcı/koşul onayıyla ele alınır. P05-06 gerçek pilotu bunun önüne geçmez. Sıradaki maliyetsiz aday P05-07 gönderim kapalı tek hatırlatmanın dar planıdır; gerçek SMS gerektirmeyen geliştirme/sentetik kabul sürer, P05 bütünü done sayılmaz. Ayrıntı ve kapanış tetiği [P05 planındadır](plans/P05.md#ücretli-gerçek-sms-kabulünün-proje-sonuna-ertelenmesi--20102026).
- 29.10.2026 eşleme: P05-10 seçili-randevu ret ve P05-11 varsayılan kapalı normal SMS adaptörü tamamlandı; gönderim/gerçek kabul proje sonunda kalır. Üstteki 20.10.2026 sonraki plan ifadesi tarihsel kayıttır. [Güncel P05](plans/P05.md).
- İş: Outbox/worker; tek resmi SMS adaptörü + test sahtesi; misafir için OTP ve onaylı randevuya tek hatırlatma. İşletmenin gerektiğinde manuel onayı vardır; bekleyen istek 30 dakikada onaylanmazsa iptal edilip slot serbest kalır. İşletme e-postası için işlemsel servis. Hesap yoksa sandbox/gönderim kapalı. SMS gönderici başlığı/operatör onayı süresini erken başlat.
- Çıkış: Restart, tekrar, timeout ve eski hatırlatma testleri; SMS kotası/harcama tavanı. Kamu rezervasyonunda OTP var; manuel onay akışı tanımlıdır ve mesaj maliyeti ölçülür.

### P06 — İzolasyon/deploy/kurtarma (P05)

- İş: Aynı imajla A/B sentetik firma; ayrı sır/ağ/volume/anahtar, Compose/Coolify şablonu, HTTPS, migration, yedek/restore, alarm/geri dönüş. Filo yönetimi: N firmaya toplu sürüm dağıtımı ve migration için runbook/script (sırayla, yedek sonrası, firma başına sonuç raporu).
- Planlanan destek ve işletim ekranı: ürün sahibinin kurulum/bakım/destek işlemlerini PowerShell yazmadan firma seçimi ve izinli eylemlerle yürütmesi. Mevcut tüm ürün işletim yardımcıları ve ileride gereken operasyonlar envantere alınır; hedef yalnız parola sıfırlama değildir. Kapsam ve kabul sınırları [OPERATIONS §5](OPERATIONS.md#5-planlanan-destek-ve-işletim-ekranı) içindedir. 02.10.2026 kararı yalnız planlamadır; uygulama ayrıca onaylanır.
- Çıkış: A cookie/token/DB parolası B'de geçmez; A uygulaması B DB/dosyasına erişemez. Deploy/restart veri/anahtar korur; temiz ortamda restore süresi/kayıp aralığı ölçülür. Canlı yetki yoksa yerel kanıtı tamamla.

### P07 — Ticari hazırlık ve pilot (P06)

- 29.10.2026 P07-01: [dar B2B kapsamı ve görsel taslak](plans/P07.md#p07-01--b2b-abonelik-kayıtlarının-kapsam-ve-görsel-taslağı--29102026). Kullanıcı ilk dilimde kayıt/gecikme uyarısı, otomatik kısıtlama olmaması ve operatör kayıt alanı+Owner salt okunur durumunu seçti. Bu bir plan/taslak teslimidir; yeni operatör yetkisi/API/şema/ekran uygulaması yok. Sonraki P07-02 öncesi ayrı görsel onayı gerekir; gerçek pilot kapıları kapanmaz.
- 29.10.2026 görsel onayı sonrası P07-02 veri/Owner okuma temeli yerelde teslim edildi:iki ek tablo, app_user SELECT-only,58 ilgili test, güvenli migration54→56 ve yedekli ana8080. Ayrı operatör MFA/yazma kanalı P07-03; Owner UI bağlantısı P07-04 planned. Otomatik erişim kısıtlaması yok. [Kabul](plans/P07.md#p07-02-kabul-ve-yedekli-ana8080-teslimi).
- İş: Manuel tahsilat, dönem/vade/paid-through/erişim durumları; temel veri ihracı, onboarding/offboarding, sözleşme taslağı, eğitim/destek ve gerçek maliyet hesabı.
- 30.10.2026 P07-03: ayrı operatör uygulaması/Identity DB/MFA ve tek firma dönem-ödeme/ters-düzeltme ekranı gerçek PostgreSQL/TLS/tarayıcı ile teslim edildi; PR192 ve tam head/main CI başarılı, yedekli ana8080 verisi korundu. [Kabul](plans/P07.md#p07-03-kabul-ve-yedekli-ana8080-teslimi). Sıradaki P07-04 Owner salt okunur UI; P07-05 onaylanan vadenin ertesi günü otomatik salt okunur kısıtlama/borç kapanınca açılmadır. Kısıtlama henüz etkin değildir; gerçek operatör hesabı ve internet/P06 kabulü açık kalır.
- 30.10.2026 P07-04: Owner salt okunur abonelik ekranı ve kısa dönem geçmişi mevcut API'ye bağlandı; gerçek MFA/Staff/anonim/veri/tarayıcı ve yedekli ana8080 kabulü geçti. [Kabul](plans/P07.md#p07-04-kabul-ve-yedekli-ana8080-teslimi). Sıradaki P07-05 otomatik kısıtlama/borç kapanınca açılma; çok firma yönetimi ve internet/P06 kurulumu açıktır.
- 30.10.2026 P07-05: vadenin ertesi günü otomatik salt okunur kısıtlama ve gecikmiş borç kapanınca açılma tek firmada gerçek operatör/API/tarayıcı,yarış/timeout ve yedekli ana8080 ile teslim edildi. [Kabul](plans/P07.md#p07-05-kabul-ve-yedekli-ana8080-teslimi). Sıradaki P07-06 merkezi çok firma abonelik yönetimi için bağlantı/kapsam/görsel taslak kararı; gerçek internet/P06 ve pilot kapıları açıktır.
- 30.10.2026 P07-06: merkezi firma seçimi/özet taslağı ve [bağlantı modeli](adr/012-central-subscription-targets.md) kullanıcı tarafından “Modeli ve ekranı onaylıyorum” yanıtıyla onaylandı; [karar dilimi](plans/P07.md#p07-06--merkezi-çok-firma-abonelik-yönetimi-karar-taslağı--30102026) done. PR198/head/main CI ve değişmeyen ana8080 kabulü geçti. [P07-07 hedef/izolasyon temeli](plans/P07.md#p07-07--izinli-firma-hedefleri-ve-izolasyon-temeli--30102026) done; PR199 tam head/main CI, gerçek iki firma izolasyonu ve yerel operatör imajı kabul edildi. Seçili firma mali API'si/özet ve UI entegrasyonu sonraki ayrı işlerdir. Gerçek uzak bağlantı/internet kabulü değildir.
- 30.10.2026 P07-08: seçili firma dönem/ödeme/ters-düzeltme API ve tüm başlamış gecikmiş dönemlerin doğru borç/erişim özeti done. [Kabul](plans/P07.md#p07-08-github-ve-yerel-imaj-teslimi); PR201 tam head/main CI, iki gerçek firma veri koruması ve yerel operatör imajı geçti. Sıradaki tek iş P07-09 onaylı firma seçimi ekranının gerçek API bağlantısıdır.
- 31.10.2026 P07-09: onaylı firma listesi/seçimi gerçek mali API ve yerel operatör imajıyla done. [Kabul](plans/P07.md#p07-09-kabul-ve-yerel-imaj-teslimi); tam head/main CI,36 sunucu/28 web testi, iki firma+ayrı kimlik DB/TLS ve22 Chrome karesi geçti. Sonraki tek iş P06 pilot hedef ortam/izolasyon/host dışı yedek-restore/izleme kabul planıdır; gerçek internet/pilot kapıları açıktır.
- Destek ekranının onaylanan iş dilimleri için satış sonrası kullanım rehberi, operatör erişimi ve yanlış firma/kimlik doğrulama/hata/geri dönüş kabulü hazırlanır; henüz yapılmamış ekran veya canlı işletim tamamlandı sayılmaz.
- Çıkış: Ayın 1'i, ay ortası, gecikme, iptal/yeniden açma testleri. Ticari/hukuki hazırlık sonrası 1–3 gerçek pilot; öncesinde sentetik demo. MFA, yedek alarmı, destek ve ihracat çalışmadan satış yok. İleri modüller ilk satışın şartı değil.

### P08 — Müşteri ilişkileri (P07)

- İş: CSV/Excel önizleme/import/export, mükerrer birleştirme, engelleme, ziyaret puan/yorumu, izinli doğum günü, yardım/kurulum adımları, destek talebi.
- Çıkış: Birleştirme ilişkileri korur/audit edilir; engelleme yanlış kişiye uygulanmaz; yorum bir ziyarete bağlı/modere edilebilir. Demo yalnız sentetik; mesaj otomasyonu P12'de.

### P09 — Kasa ve raporlar (P08)

- İş: Borç, kısmi tahsilat, iade/ters hareket, gider/taksit. Raporlar: hizmet satışı, tahsilat, gider/fark, alacak/vade, çalışan/hizmet, müşteri dönüşü; CSV/Excel.
- Çıkış: Bakiye hareketlerden hesaplanır; mükerrer tamamlama çift borç yaratmaz; satış/nakit ayrıdır; mali rapor yetkili. Online kapora/tahsilat ayrı sağlayıcı/sözleşme gereksinimi olarak planlanır.

### P10 — Paket, stok, prim (P09)

- İş: Paket geçerlilik/hak/iade, aynı çalışanla sıralı çok hizmet; stok giriş/çıkış/uyarı, personel prim raporu.
- Çıkış: Son hak eşzamanlı iki kez tüketilemez; paket iadesi/borç tutarlı; süre/fiyat server hesabı; negatif stok politikası ve yarış testi. Prim raporu bordro değildir.

### P11 — Personel/kaynak/şube (P10)

- İş: Çok çalışan, koltuk/oda/cihaz blokları; aynı firmanın şubeleri, saatleri, yetkileri, birleşik raporları; tek şube verisinin migration'ı. FullCalendar Standard üzerinde özel görünüm kullanılabilir (FullCalendar zorunlu değil).
- Çıkış: Çalışan şubeler arasında çakışmaz; çok kaynak rezervasyonu atomiktir; şube yetkisi API/raporda işler. Firmalar tek DB'de birleştirilmez.

### P12 — Çok kanallı iletişim (P05, P08; taksit için P09)

- İş: Resmi WhatsApp, SMS, sesli hatırlatma; teslim durumuna göre yedek kanal, çok zamanlı hatırlatma, tuşla onay/iptal, taksit ve izinli karşılama/doğum günü; kredi/bütçe.
- Çıkış: Geç/tekrar callback kanal veya masraf patlaması üretmez. İmza, ret listesi, sessiz saat, sınırlı tekrar ve tavan testli. WhatsApp Web/kişisel şifre otomasyonu yok.

### P13 — Otomatik B2B tahsilat (P07)

- İş: Kabul/maliyet koşulları doğrulanmış sağlayıcıda hosted checkout, dönem, webhook, mutabakat, iptal ve başarısız ödeme. iyzico ilk aday; otomatik kabul/ücretsiz modül varsayma.
- Çıkış: Ay başına hizalama sandbox'ta kanıtlı; değilse manuel modeli koru. Sahte/tekrar/sırasız olay yanlış erişim veya çift çekim yaratmaz. Gerekirse ayrı küçük işletim aracı yalnız abonelik metadatasını tutar, randevu verisini merkezileştirmez.

### P14 — Instagram (P08)

- İş: Resmi Meta izin/hesap uygunluğu; mesaj kutusu, kişi eşleştirme, konuşmadan randevu, kurallı SSS yanıtı, bağlantı iptali.
- Çıkış: API onayı/sınırları doğrulanır; yetkisiz konuşma yok; webhook tekrarı güvenli; bağlantı kesilince token iptal. Scraping/hesap şifresiyle engeli aşma.

### P15 — Mobil (P07; modüller kendi aşamalarına bağlı)

- İş: Önce responsive/PWA/push. Native talep varsa ADR ile React Native/Expo, iOS/Android dağıtımı, cihaz biyometrisi/güvenli token, seçili rehber aktarımı.
- Çıkış: Offline rezervasyon onaylanmaz; cihaz kaybında oturum iptali/bildirim mahremiyeti testli. PWA native diye sunulmaz; mağaza ücreti ve işletmelere dağıtım ayrıca bütçelenir.

### P16 — Sektörel/klinik (P11)

- İş: Hasta/tedavi, laboratuvar ve reçete için ayrı keşif; sağlık verisi, yetki, saklama/aktarım ve uzman incelemesi; pilot sektör/kapsam.
- Çıkış: İnceleme bitmeden gerçek sağlık verisi yok. Belge/link resmi e-reçete değildir; resmi entegrasyon yetki/sözleşme ister. Grup ders/veteriner gibi genişlemeler de ayrı iş kuralı planı gerektirir.

### P17 — Telefon asistanı: yalnız keşif kaydı (P12)

- Rakibin konuşmalı rezervasyonu kayıtlıdır; mevcut AI'sız kapsamı genişletme yetkisi değildir. Kullanıcı ayrıca isterse arayarak alma/teyit/taşıma, maliyet/izin/aktarım ve insana devir planlanır.
- Geçiş koşulu: Açık ürün kararı ve bütçe; dar yetkili araç, müşteri doğrulama/onayı, çakışma ve harcama kontrolü, yanlış anlamada güvenli duruş testleri. DB'ye serbest erişim yok; çağrı kaydı varsayılan kapalı.

## 4. Kaynaklar

Resmi kaynaklar; sürüm, tarife ve mevzuat uygulama zamanında yeniden doğrulanır:

- [Codex talimat dosyaları](https://developers.openai.com/codex/guides/agents-md)
- RandevuNet: [ana sayfa](https://randevunet.com/), [kuaför](https://randevunet.com/kuafor-randevu-programi), [güzellik](https://randevunet.com/guzellik-salonu-randevu-programi)
- [.NET destek politikası](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [Npgsql 10](https://www.npgsql.org/efcore/release-notes/10.0.html)
- [PostgreSQL zaman aralıkları](https://www.postgresql.org/docs/current/rangetypes.html)
- [FullCalendar lisansı](https://fullcalendar.io/license)
- [Coolify kurulumu](https://coolify.io/docs/start-with-self-hosted)
- Hetzner: [planlar](https://www.hetzner.com/cloud/cost-optimized/), [faturalandırma](https://docs.hetzner.com/cloud/billing/faq/)
- iyzico: [abonelik](https://docs.iyzico.com/urunler/abonelik/abonelik-entegrasyonu), [webhook](https://docs.iyzico.com/ek-servisler/webhook)
- [OWASP güvenlik rehberleri](https://cheatsheetseries.owasp.org/)
- [KVKK yurt dışı aktarım](https://www.kvkk.gov.tr/Icerik/2053/Yurtdisina-Aktarim)
