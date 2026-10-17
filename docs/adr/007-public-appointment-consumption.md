# ADR-007 — Müşteri randevusu ve doğrulama tüketimi

Tarih: 17.10.2026. Durum: P04-09 uygulama kapsamıyla kabul edildi; test/teslim kanıtı [P04](../plans/P04.md) içinde ayrıca tutulur.

## İhtiyaç ve alternatifler

Identity hesabı açmadan doğrulanmış tek talebi kaydetmek; yanıt kaybı ve paralel isteklerde ikinci randevuyu önlemek, müşteriye gösterilen koşulu korumak. Sahte Owner hesabı işlemin kaynağını gizler. Ayrı makbuz tablosu aynı challenge/anahtar/özet/gizliyi çoğaltır. Nullable aktörle mevcut Owner indeksi ise tek başına Public tekrarlarını engellemez.

## Karar

- Randevu `Owner/Public` kaynaklıdır; eski kayıt/default Owner ve aktörü zorunlu. Public aktörü null; sunucudan politika sürümü, iptal dakikası ve onay tercihi snapshot'ı zorunlu. Mevcut hizmet/zaman bloğu snapshot'ı ve GiST korunur.
- Owner anahtar/hash sözleşmesi aynı kalır. Public işlem anahtarı challenge'ın `RequestKey` değeridir ve randevuda ayrı filtreli benzersiz indeksle kalıcı korunur. Doğrulama ve yazım aynı kanonik HMAC talep özetini kullanır.
- Challenge'a tekil randevu FK'sı/tüketim zamanı/24 saatlik tekrar son anı eklenir. Randevu, blok, `PublicCreated` audit ve tüketim aynı transaction'dadır. Başarılı tekrar yalnız aynı talep+gizli ile güncel tek kaydı okur; makbuz genel müşteri/iptal yetkisi değildir.
- Politika → işletme mesaisi → personel → hizmet → mevcut OTP advisory kilidi sırası uygulanır. Ortak oluşturma kuralları Owner ve Public için aynıdır; OTP yolları bu kilidi tutarken takvim kilidi almaz. Kilit sonrası yetki/saat/kurallar yeniden denetlenir.
- Worker makbuz süresi bitmeden challenge'ı silmez. Sonrasında challenge silinse bile randevudaki Public anahtarı ikinci kaydı engeller; aktif OTP/kod/gönderim yetkisi canlanmaz. Yeni makbuz tablosu, token altyapısı veya boş bildirim işi yoktur.

## Bedel ve geri dönüş

Tek OTP kilidi kısa create transaction'ına da katılır; düşük pilot hacmi için kabul edilen sıra bekleme bedeli, 5 saniye kilit sınırı ve 15 saniye HTTP timeout'u vardır. 24 saat sonrası kayıp sonuca telefonla geçmiş açılmaz; sonuç/iptal yetkisi ayrı P04-10'dur. Ana özellik gönderim/yazım kapalı kalır.

İlk Public/tüketim kaydından sonra önceki P05 imajı null aktör/makbuz ömrünü bilmez; otomatik eski imaj dönüşü güvenli sayılmaz. Kamu yazım/gönderimini kapatıp bu şemayı bilen doğrulanmış imaja dön veya ileri düzelt. Public/tüketim varken Down reddedilir. Restore kontrollüdür; DB/anahtar/config birlikte ve gönderim kapalı geri alınır. Ana veri yokken eski imaj uyumu ayrı sentetik kabul gerektirir.
