# STATUS — Güncel durum ve sıradaki iş

Güncelleme: 2026-10-02. Güncel özet burada; kapsam ve kanıt bağlantıları [P02 planında](plans/P02.md), önceki oturum kayıtları [arşivde](archive/2026-10-02/STATUS.md). Arşivdeki eski durum ve onay bekleme ifadeleri güncel yönlendirme değildir.

## Aşamalar

| Aşama | Durum | Kanıt / kalan kapsam |
| --- | --- | --- |
| P00 — İş ve ürün tanımı | `done` | [P00 planı](plans/P00.md) ve [ürün kararları](PRODUCT.md#2-ürün-kararları); işletme görüşmeleri ve gerçek pilot yapılmış sayılmaz. |
| P01 — Temel ve CI | `done` | [P01 kanıtı](plans/P01.md#kanıt); PR #1 birleşme commit'i `10527c1`. |
| P02 — Kimlik ve tanımlar | `in_progress` | P02-01–P02-15 onaylanan yerel kapsamları tamamlandı. Logo, çalışan/hizmet/mesai/yetkinlik ve diğer kalan işler ayrı onay ister; [aktif plan](plans/P02.md). |
| P03–P17 | `planned` | Yalnız [ROADMAP](ROADMAP.md) düzeyinde; yeni uygulama yetkisi veya kabul kanıtı yok. |

## Son doğrulanan teslim ve sınırlar

- P02-15: sunucu 74/74 ve web 97/97, kalite/bağımlılık kontrolleri, gerçek PostgreSQL ve dört ekran genişliğinde tarayıcı kabulü önceki geliştirme oturumunda geçti. Ayrıntılı kabul ve geri dönüş [planın kanıt bölümünde](plans/P02.md#kabul-ve-kanıt); bu belge düzenlemesinde uygulama testleri yeniden çalıştırılmadı.
- [PR #14](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/14) `1f3584d` ile birleşti; [main CI](https://github.com/fatihemrertekin/firma_randevu_sistemi/actions/runs/36956717787) başarılı. Bu sadeleştirme başlangıcında GitHub API üzerinden birleşme yeniden doğrulandı; yerel main/origin/main/GitHub main `e55be37` ile eşitti ve bu commit'in [CI sonucu](https://github.com/fatihemrertekin/firma_randevu_sistemi/actions/runs/36959199297) başarılıydı. Çalışma ağacında kullanıcıya ait belge değişiklikleri vardı.
- Yerel SMTP kalıcı özel dosyayla etkin; Owner adres doğrulaması ve sıfırlama iletisinin yeni parola ekranına ulaşması kabul edildi. Mevcut Owner parolası/MFA'sı korunmuştur. Tam parola yenileme + normal giriş/MFA kabulü P02-14'te ayrı sentetik hesapta yapıldı; [SMTP kararı](adr/002-kimlik-epostasi-smtp.md).
- Staff daveti ve parola sıfırlama Owner'ın manuel teslim koduyla çalışır. Üretim DNS/HTTPS/işletim kabulü, diğer alıcı sağlayıcılarında gerçek gelen kutusu teslimi ve gerçek pilot yapılmadı; yerel kabul bunların yerine geçmez.
- Eski Türkçe karakter/libgssapi uyarıları ve DB host portu için kullanılan yerel override geçmiş kayıtlarda açık kalmıştır. Kalıcı düzeltme kanıtı olmadan çözülmüş sayılmaz; pilot tarihi ve destek iletişim bilgileri de ayrıca netleşecektir.

## Bakım durumu

Belge sadeleştirmesi `done`: [PR #15](https://github.com/fatihemrertekin/firma_randevu_sistemi/pull/15), birleşme `94e9298` ve [main CI](https://github.com/fatihemrertekin/firma_randevu_sistemi/actions/runs/37040331937) başarılı. Geçmiş içerik ve bağlantılar korundu. Ürün sahibi 02.10.2026 tarihinde sunucu testlerini özellik bazlı bağımsız sınıflara ayırmayı ve bekleyen destek planı belge değişikliklerini uyarlayıp commit'lemeyi onayladı; bu ikinci bakım adımı `in_progress`. Ortak PostgreSQL/oturum/CSRF yardımcıları ayrılır; test senaryoları, yarış/rollback kontrolleri ve mevcut eşzamanlı çalışma sınırı korunur. Uygulama, veri veya SMTP ayarı değiştirilmez.

## Sıradaki iş

Onaylanan test düzenlemesi ve belge commit'leri kapanınca yalnız Program.cs sadeleştirmesinin kapsam/kabul önerisini sun ve ürün sahibinin ayrı onayını bekle. Program.cs değişikliği, yeni AGENTS.md dosyaları veya P02/P03 geliştirmesi bu onayın kapsamında değildir. GitHub işleri PR üzerinden ve yeşil CI sonrası yürütülür; doğrudan main push yoktur.

## Gelecek plan kaydı — destek ve işletim ekranı (02.10.2026)

Özel destek ekranı `planned`: ürün sahibi kurulum/bakım/destek işlemlerini ileride komut yazmadan yürütmek istiyor. [Ürün kararı](PRODUCT.md#satış-sonrası-destek-ekranı-kararı-02102026), [P06/P07 sırası](ROADMAP.md#p06--izolasyondeploykurtarma-p05) ve [işlem/güvenlik/kabul envanteri](OPERATIONS.md#5-planlanan-destek-ve-işletim-ekranı) tek kaynaklarıdır. Ekran geliştirmesi, Staff pasifleştirme veya başka aşama uygulaması onaylanmadı; bu kayıt yalnız gelecekteki kapsamı korur.
