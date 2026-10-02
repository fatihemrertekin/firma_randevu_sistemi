# STATUS — Güncel durum ve sıradaki iş

Güncelleme: 2026-10-03. Güncel özet burada; kapsam ve kanıt bağlantıları [P02 planında](plans/P02.md), önceki oturum kayıtları [arşivde](archive/2026-10-02/STATUS.md). Arşivdeki eski durum ve onay bekleme ifadeleri güncel yönlendirme değildir.

## Aşamalar

| Aşama | Durum | Kanıt / kalan kapsam |
| --- | --- | --- |
| P00 — İş ve ürün tanımı | `done` | [P00 planı](plans/P00.md) ve [ürün kararları](PRODUCT.md#2-ürün-kararları); işletme görüşmeleri ve gerçek pilot yapılmış sayılmaz. |
| P01 — Temel ve CI | `done` | [P01 kanıtı](plans/P01.md#kanıt); PR #1 birleşme commit'i `10527c1`. |
| P02 — Kimlik ve tanımlar | `in_progress` | P02-01–P02-17 onaylanan yerel kapsamları tamamlandı. Logo/hizmet/mesai/yetkinlik ve diğer kalan işler ayrı onay ister; [aktif plan](plans/P02.md). |
| P03–P17 | `planned` | Yalnız [ROADMAP](ROADMAP.md) düzeyinde; yeni uygulama yetkisi veya kabul kanıtı yok. |

## Son doğrulanan teslim ve sınırlar

- P02-17 yerel kabulü ve ana yerel teslimi `done`: Personel listesi/ekleme/ad/durum, giriş hesabından bağımsızlık ve atomik audit; sunucu 90/90, web 117/117, kalite kapıları ve dört genişlikte gerçek API/Chrome kabulü geçti. Şifreli yedek sonrası ana DB/imaj güncellendi; mevcut Owner/parola/MFA/anahtar/SMTP ve işletme profili korundu, sağlık/restart başarılı. [Kabul](plans/P02.md#p02-17-geliştirme-kabulü--03102026), [yerel teslim/geri dönüş](plans/P02.md#p02-17-ana-yerel-teslim--03102026); GitHub sonucu [PR #21](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/21) son commit kontrollerinden doğrulanır.
- P02-16 yerel kabulü `done`: MFA Owner Staff listesini görüp hesabı onayla pasifleştirir; yeni giriş, eski oturum ve sıfırlama kodu reddedilir, Owner korunur. Sunucu 82/82, web 106/106, kalite kapıları ve dört genişlikte gerçek API/tarayıcı kabulü geçti; [kanıt ve geri dönüş](plans/P02.md#p02-16--staff-hesaplarını-pasifleştirme). 03.10.2026 ayrı onayla ana yerel DB/imajı güncellendi; şifreli yedek, sağlık/restart ve kimlik/anahtar/SMTP koruma kontrolleri geçti; [yerel güncelleme kanıtı](plans/P02.md#p02-16-ana-yerel-güncelleme--03102026).
- [PR #14](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/14) `1f3584d` ile birleşti; [main CI](https://github.com/fatihemrertekin/firma_randevu_sistemi/actions/runs/36956717787) başarılı. Bu sadeleştirme başlangıcında GitHub API üzerinden birleşme yeniden doğrulandı; yerel main/origin/main/GitHub main `e55be37` ile eşitti ve bu commit'in [CI sonucu](https://github.com/fatihemrertekin/firma_randevu_sistemi/actions/runs/36959199297) başarılıydı. Çalışma ağacında kullanıcıya ait belge değişiklikleri vardı.
- Yerel SMTP kalıcı özel dosyayla etkin; Owner adres doğrulaması ve sıfırlama iletisinin yeni parola ekranına ulaşması kabul edildi. Mevcut Owner parolası/MFA'sı korunmuştur. Tam parola yenileme + normal giriş/MFA kabulü P02-14'te ayrı sentetik hesapta yapıldı; [SMTP kararı](adr/002-kimlik-epostasi-smtp.md).
- Staff daveti ve parola sıfırlama Owner'ın manuel teslim koduyla çalışır. Üretim DNS/HTTPS/işletim kabulü, diğer alıcı sağlayıcılarında gerçek gelen kutusu teslimi ve gerçek pilot yapılmadı; yerel kabul bunların yerine geçmez.
- Eski Türkçe karakter/libgssapi uyarıları ve DB host portu için kullanılan yerel override geçmiş kayıtlarda açık kalmıştır. Kalıcı düzeltme kanıtı olmadan çözülmüş sayılmaz; pilot tarihi ve destek iletişim bilgileri de ayrıca netleşecektir.

## Bakım durumu

Belge sadeleştirmesi `done`: [PR #15](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/15), birleşme `94e9298` ve [main CI](https://github.com/fatihemrertekin/firma_randevu_sistemi/actions/runs/37040331937) başarılı. Bekleyen destek ekranı planı yeni belge düzenine uyarlandı ve `1e06cab` ile commit'lendi; yalnız gelecekteki plan kaydıdır.

Test düzenlemesi `done`: 12 bağımsız özellik sınıfı ve altı ortak destek dosyası; önce/sonra sunucu 74/74, 0 atlama ve kalite kapıları başarılı. Test/veri/assertion ve yardımcı uygulamaları korundu; [test düzeni ve kanıt](plans/P02.md#test-düzeni-ve-bakım-kabulü). [PR #16](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/16) `ee0bfa8` ile birleşti; [main CI](https://github.com/fatihemrertekin/firma_randevu_sistemi/actions/runs/37044579643) başarılı.

Program.cs bakımı `done`: başlangıç 265 satırdan 69 satıra indi; önce/sonra 74/74 ve kalite/davranış kontrolleri geçti. [Başlangıç düzeni ve kanıt](plans/P02.md#uygulama-başlangıcı-ve-bakım-kabulü). [PR #17](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/17) `316e1fa` ile birleşti; [main CI](https://github.com/fatihemrertekin/firma_randevu_sistemi/actions/runs/37048992061) başarılı.

Ürün sahibinin ayrı onayladığı üç yerel AGENTS.md dosyasının yerel kabulü `done`: web, sunucu testleri ve belgeler için dokuzar satır; kök kurallarla uyum, bağlantılar ve 32 KiB sınırı doğrulandı. [Klasör talimatları ve kanıt](plans/P02.md#klasör-talimatları-ve-bakım-kabulü). Uygulama/test kodu ve kök AGENTS.md değişmedi; GitHub kanıtı [PR #18](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/18) ve PR'ın son commit kontrollerinden takip edilir.

## Sıradaki iş

P02-17 yerel kabulü ve ana yerel teslimi tamamlandı; GitHub teslimi PR #21 son commit kontrollerinden izlenir. Sonraki geliştirme önerisi temel hizmet tanımlarıdır (ad, süre, fiyat, aktif/pasif); yalnız öneridir, uygulama için ürün sahibi onayı gerekir. Hesap eşleştirme, mesai/yetkinlik veya P03 başlamaz; doğrudan main push yoktur.

## Gelecek plan kaydı — destek ve işletim ekranı (02.10.2026)

Özel destek ekranı `planned`: ürün sahibi kurulum/bakım/destek işlemlerini ileride komut yazmadan yürütmek istiyor. [Ürün kararı](PRODUCT.md#satış-sonrası-destek-ekranı-kararı-02102026), [P06/P07 sırası](ROADMAP.md#p06--izolasyondeploykurtarma-p05) ve [işlem/güvenlik/kabul envanteri](OPERATIONS.md#5-planlanan-destek-ve-işletim-ekranı) tek kaynaklarıdır. Destek ekranı veya başka aşama uygulaması onaylanmadı; Staff hesap işlemi yalnız ayrı P02-16 onayıdır.
