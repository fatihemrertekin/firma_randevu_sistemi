# ROADMAP — Özellik haritası ve aşamalar

Hazırlanma: 2026-09-29. Bu dosya plandır; uygulanmış özelliğin kanıtı değildir. Gerçek durum docs/STATUS.md'dedir.

P00–P07 ilk satılabilir sürüm; P08–P17 sonraki plandır. İleri aşamayı talep/bütçe/bağımlılık sağlanınca seç. Her aşamayı küçük dikey işlere böl; tamamlanma kanıtını STATUS'a yaz. Yerel ilerleme için her geliştirme aşamasından sonra diğerine devam etmek için yaptığın geliştirmeleri not olarak ver ve devamı için kullanıcıdan onay iste. Aşağıdaki testler asgari kabul koşullarıdır.

## 1. Rakip araştırması ve özellik haritası

29.09.2026'da RandevuNet'in kamuya açık ana sayfası ile kuaför/güzellik sayfaları incelendi; oturum içi davranış ve pazarlama vaatleri test edilmedi. Aşağıdaki kısa envanter ürün kabiliyetlerini kapsar; teknik gereksinimlerimiz kendi tasarımımızdır. Kod, marka, metin, ekran görüntüsü veya özgün tasarım kopyalanmaz. Yeni incelemelerde özellik kaynağını ve tarihini bu dosyaya ekle.

| Referansta görülen kabiliyet | Bizde aşama |
| --- | --- |
| Takvim, durumlar, mesai | P02–P04 |
| Markalı rezervasyon, doğrulama | P04–P05 |
| Müşteri kartı, engelleme | P04, P08 |
| Aktarım, birleştirme, dışa aktarım | P08 |
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

- İş: Davet/giriş/çıkış/sıfırlama, Owner/Staff, MFA, audit; profil/logo, çalışan/hizmet, fiyat/süre, mesai ve yetkinlik.
- Çıkış: Anonim/yetkisiz API işlemi ve CSRF engellenir; pasif hizmet yeni rezervasyonda yok, geçmişte durur. Dev kullanıcısı üretime taşınmaz.

### P03 — Randevu motoru (P02)

- İş: Uygunluk, mola/izin/istisna/tampon; oluştur/taşı/iptal/durum; constraint, concurrency, idempotency. ADR: çoklu hizmet/personel için zaman bloğu modeli (randevu satırı modeli), P10–P11 migration'ını zorlaştırmayacak şekilde.
- Çıkış: Gerçek PostgreSQL'de aynı çalışan/zamana 20 farklı anahtarlı paralel istekten biri kaydolur. Bitişik slot, farklı çalışan, iptal sonrası boşalma, tampon, fiyat snapshot'ı, atomik taşıma ve izin ekleme yarışı testlidir.

### P04 — Rezervasyon ve panel (P03)

- İş: Markalı misafir sayfası/token yönetimi; gün/hafta/ay takvimi, çalışan filtresi, form/sürükleme ile taşıma; müşteri kartı/geçmişi ve günlük özet.
- Çıkış: Mobil rezervasyon panelde yönetilir; çift tıklama çoğaltmaz; çakışan taşıma eski kaydı kaybettirmez. Telefon/ID tahmini geçmişi açmaz. Playwright akışı geçer.

### P05 — Doğrulama/bildirim (P04)

- İş: Outbox/worker; tek resmi SMS adaptörü + test sahtesi; misafir için OTP ve onaylı randevuya tek hatırlatma. İşletmenin gerektiğinde manuel onayı vardır; bekleyen istek 30 dakikada onaylanmazsa iptal edilip slot serbest kalır. İşletme e-postası için işlemsel servis. Hesap yoksa sandbox/gönderim kapalı. SMS gönderici başlığı/operatör onayı süresini erken başlat.
- Çıkış: Restart, tekrar, timeout ve eski hatırlatma testleri; SMS kotası/harcama tavanı. Kamu rezervasyonunda OTP var; manuel onay akışı tanımlıdır ve mesaj maliyeti ölçülür.

### P06 — İzolasyon/deploy/kurtarma (P05)

- İş: Aynı imajla A/B sentetik firma; ayrı sır/ağ/volume/anahtar, Compose/Coolify şablonu, HTTPS, migration, yedek/restore, alarm/geri dönüş. Filo yönetimi: N firmaya toplu sürüm dağıtımı ve migration için runbook/script (sırayla, yedek sonrası, firma başına sonuç raporu).
- Planlanan destek ve işletim ekranı: ürün sahibinin kurulum/bakım/destek işlemlerini PowerShell yazmadan firma seçimi ve izinli eylemlerle yürütmesi. Mevcut tüm ürün işletim yardımcıları ve ileride gereken operasyonlar envantere alınır; hedef yalnız parola sıfırlama değildir. Kapsam ve kabul sınırları [OPERATIONS §5](OPERATIONS.md#5-planlanan-destek-ve-işletim-ekranı) içindedir. 02.10.2026 kararı yalnız planlamadır; uygulama ayrıca onaylanır.
- Çıkış: A cookie/token/DB parolası B'de geçmez; A uygulaması B DB/dosyasına erişemez. Deploy/restart veri/anahtar korur; temiz ortamda restore süresi/kayıp aralığı ölçülür. Canlı yetki yoksa yerel kanıtı tamamla.

### P07 — Ticari hazırlık ve pilot (P06)

- İş: Manuel tahsilat, dönem/vade/paid-through/erişim durumları; temel veri ihracı, onboarding/offboarding, sözleşme taslağı, eğitim/destek ve gerçek maliyet hesabı.
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
