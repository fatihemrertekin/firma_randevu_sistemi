# Abonelik — onaylı iki görünüm

Mode: Operate.29.10.2026 kullanıcı, yerel `p07-01-subscription.html` taslağı sunulduktan sonra **“onaylıyor”** diyerek iki görünümün düzenini onayladı. Önceki kararlar: yalnız dönem/ödeme kaydı+gecikme uyarısı; otomatik erişim kısıtlaması yok. Operatöre ayrı kayıt alanı, firma Owner'ına salt okunur durum. Kaynak görsel `.impeccable/mocks/decision/p07-01-subscription.html`; kaynak hash'i ve14 statik ölçüm `.local/p07-01/draft-proof.json` içinde. Bu onay API/MFA/migration başarısı kanıtı değildir.

## Yerleşim ve amaç

Mevcut DESIGN/tokens/Unna ve yönetim kontrolleri korunur. Firma → dönem/ödeme durumu → bedel/vade/net ödeme → kapalı önceki dönemler. Operatörde Dönem ekle ve Ödeme kaydet; yalnız istenince native modeless kısa form. Kaydet/Vazgeç eşit genişlik; mobil/root200 doğal tek sütun, görünür etiket/odak/44px hedef. Owner alanında yazma eylemi/operatör notu/referansı yoktur. Önizlemenin üst kontrol bandı gerçek ürüne taşınmaz. Yeni tema, logo, fotoğraf, grafik/KPI yok.

## API ve uygulama sınırı

P07-02 yalnız veri/okuma temelidir: `GET /api/subscription?page=1&pageSize=12`, Owner+MFA ve ayrı30/dakika okuma kotası; maksimum24 kayıt, no-store/tutarlı snapshot. Seçili kurulumun bugünkü dönemi ve sayfalı dönem kayıtları; TRY tutarlar ondalık metin, tarihler İstanbul. `EndsOn` hariç bitiştir; UI bitişi bir önceki gün gösterir. Finansal ödeme durumu ile abonelik nedeniyle kısıtlama ayrıdır. Ödenmemiş veya okunamayan kayıt uygulamayı kapatmaz.

Yazma endpoint'i/operatör oturumu veya ekran bağlantısı bu dilimde yoktur; Owner/Staff kimliğine yeni operatör rolü verilmez. Tam operatör UI ve bağımsız MFA/izinli yazma kanalı P07-03; Owner UI API bağlantısı P07-04 kabulüdür. Taslakta çalışan formlar gerçek kayıt yapmaz. Banka/operatör referansı, düzeltme gerekçesi, request hash/anahtar ve aktör bilgisi Owner DTO'suna girmez. Taslak “Randevu işlevleri açık” metni, canlıda aboneliğe bağlı kısıtlama bulunmadığını anlatır; gönderim/kamu rezervasyonunun diğer kapılarını açıkmış gibi göstermemelidir.

## Durum ve kabul

29.10.2026 devam kararı: operatör alanı internette bilgisayar/telefon hedeflidir. Kullanıcı otomatik salt okunur kısıtlamayı **vadenin ertesi günü** için seçti. Önceki “otomatik kısıtlama yok” kararı P07-01/02'nin tarihsel teslim sınırıdır; yeni kısıtlı Owner durumu P07-04/05'te gerçek yetki/API ile kabul edilmeden hazır gösterilmez. P07-03 ayrı operatör oturumu, mevcut görsel dilde parola/MFA kurulumu ve onaylı dönem/ödeme formlarını uygular; ödeme düzeltmesi aynı kısa form dilinde gerekçeli ters hareket ve bağlı yeni kayıt kullanır. Operatör UI'si müşteri uygulamasının menüsüne eklenmez.

Loading, kayıt yok, ödeme bekleniyor, vadesi geçmiş, kısmi ödeme, ödenmiş, hata ve yeniden deneme ayrıdır.401 girişe,403 yetki açıklamasına;400/409/429 ölçülmüş API/hata/Retry-After sözleşmesine bağlanır. Hatalı okumada eski tutar doğrulanmış sayılmaz. Para doğrulaması, çift gönderim/iptal/geç yanıt, kaydedilmemiş form ve düzeltmenin değiştirilemez ters kayıt davranışı gerçek yazma diliminde kanıtlanır.

P07-01 yalnız statik14 kare/ölçüm, normal dört genişlik ve klavye/root200 kabulüdür. P07-02 server test/rol/migration/sentetik API kabulü ayrı kaydedilir. Onaylı taslak API entegrasyonu sayılmaz; gerçek ekranlar kendi frontend ve320/390/768/1280 kabulü olmadan tamamlandı sayılmaz. Native comp-led/finish veya tam WCAG iddiası yok; son uygulama kanıtı [P07](../../docs/plans/P07.md)'de tutulur.

## P07-04 gerçek Owner bağlantısı — 30.10.2026

Onaylı salt okunur görünüm `/yonetim/abonelik` adresinde, mevcut kabuğun Hesap grubundadır. Firma kimliği kabukta; mevcut dönem/durum, bedel/vade/doğrulanan ödeme/kalan borç ve kapalı **Dönem kayıtları** bölümü kullanılır. API önceki/bugünkü/gelecek dönemleri birlikte sayfaladığı için bölüm yalnız “önceki” diye adlandırılmaz;12'li sade önceki/sonraki bağlantılarıdır. Yeni tasarım/onay turu yoktur. Abonelik nedeniyle kısıtlama uygulanmadığı açıkça söylenir; başka gönderim/rezervasyon kapıları açıkmış gibi gösterilmez. Owner UI yapıldı diye P07-05 gate'i veya merkezi çok firma yönetimi teslim edilmiş sayılmaz. Son gerçek tarayıcı ve görsel kabul plan kaydındadır.

## P07-05 mevcut salt okunur durumun bağlanması — 30.10.2026

Onaylanan salt okunur durum gerçek API'ye bağlanır; yeni ekran/tema yoktur. Randevu listesi/oluşturma/detay yüzeyinde kısa düz uyarı ve **Erişimi yeniden kontrol et** bulunur. Yeni randevu/taşıma/durum alanları bilinen kısıtlamada veya durum henüz doğrulanamadığında pasif; liste/detay/Excel/hatırlatma ret hakkı korunur. Bilinen yazma reddi uyarıyı tekrar okutur; borç kapanınca butonlar açılır, taslak DOM'da korunur. Hata durumunda eski açık erişim doğrulanmış sayılmaz;401 normal girişe,429 beklemeye bağlanır. Owner abonelik sayfasındaki mevcut salt okunur notu sunucunun tüm başlamış/gecikmiş dönem borcu kararını gösterir; gelecekteki veya kayıtsız dönemden borç uydurulmaz. Gerçek API/320–1280/klavye kabulü P07 planında kaydedilir.
