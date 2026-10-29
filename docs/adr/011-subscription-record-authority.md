# ADR-011 — Firma içi abonelik kaydı ve ayrı operatör yazma yetkisi

Tarih:29.10.2026. Durum: P07-02 için kabul edilen dar temel; operatör yazma kanalı henüz uygulanmadı.

## İhtiyaç

Onaylanan abonelik taslağı operatörün dönem/ödeme kaydı yapmasını, firma Owner'ının yalnız kendi özetini görmesini gerektiriyor. İlk sürüm gecikmeyi bildirir; abonelik nedeniyle hizmet/rezervasyon kapatmaz. Mevcut kimlik sistemi firmanın Owner/Staff hesaplarıdır; bu hesaplara ödeme doğrulama veya süre uzatma yetkisi vermek ürün kararına aykırıdır.

## Seçenekler

1. Firma Owner'ına ödeme yazma yetkisi: ödeme otoritesini işletmeye verir; reddedildi.
2. Merkezi yeni abonelik/müşteri veritabanı ve senkronizasyon servisi: ek işletim/kimlik/dağıtım maliyeti; bu dar dilimde gerekli değil. Firma randevu/müşteri kayıtları merkezileştirilmez.
3. Her firmanın kendi DB'sinde abonelik kaydı, normal uygulama rolüne yalnız SELECT; ayrı ve açık operatör yazma sınırı: mevcut kurulum/yedek/izolasyon örüntüsünü korur. Seçildi.

## Karar

P07-02 iki ek tablo ve gerçek Owner+MFA salt okunur API içerir. Dönem, doğrulanmış ödeme ve düzeltme hareketi firma veritabanındadır; finansal tutarlar decimal/TRY, tarihler İstanbul takvimidir. Pozitif ödeme ve ona bağlı tek negatif ters hareket değiştirilemez. Ters çevrilmiş ödeme, aynı gerçek banka referansını koruyan tek bağlantılı yeni tutar/dönem kaydıyla düzeltilebilir; zincir dallanamaz. DB constraint/trigger; çakışan dönem, fazla ödeme, çift ters kayıt, mükerrer referans ve eşzamanlı bakiye yarışını engeller. Read API tutarlı snapshot ve sonlu sayfalama kullanır; operatör kimliği/banka referansı/notlar Owner DTO'suna girmez.

`app_user` iki tabloya yalnız SELECT alır; INSERT/UPDATE/DELETE/TRUNCATE alamaz. Owner/Staff cookie'si veya müşteri HTTP isteği ödeme/dönem yazamaz; bu dilimde public operatör endpoint'i/hesabı/ortak admin sırrı yoktur. Test verileri yalnız ayrı sentetik DB'de hazırlanır. Yeni operatör hesabı/MFA bu dilimde teslim edilmiş sayılmaz.

P07-03'te kayıt işlemleri, ayrı operatör kimliği+MFA ve açık firma hedefiyle [işletim sınırı](../OPERATIONS.md#5-planlanan-destek-ve-işletim-ekranı) üzerinden ele alınacaktır. Son kullanıcı uygulamasına operatör oturumu, genel terminal/SQL, Docker socket veya başka firmanın DB sırrı verilmez. Taşıyıcı/dağıtım ve bağımsız operatör MFA kabulü somutlaştırılmadan yazma kanalı açılmaz. Bu karar bütün işletim panelini veya uzaktan kontrol servisini bugünden kurma yetkisi değildir.

## Bedel ve kabul

Operatör özetleri ileride firma hedefi üzerinden okunur; merkezi tek sorgu yerine firma bazında izinli erişim gerekir. Tam operatör ekranı ve Owner ekranının gerçek UI bağlantısı sonraki dar işlerdir. P07-02 API hazırlığı bunları yapılmış göstermez. Şema eklemelidir; eski imajla uyumlu kalır. Veri varsa Down kayıtları silmez, açık hata verir; imaj geri dönüşünde ek tablolar korunur.

Mevcut54 tablonun tüm eski satır/kolonları ve anahtar/ayarlar korunarak migration uygulanır. Yeni iki tablo ana kurulumda boş kalır; gerçek ödeme, Owner POST veya SMS yoktur. Gerçek PostgreSQL'de tutar/ters hareket/yarış/rollback/rol ve gerçek auth API kabulü gerekir. Abonelik yok/gecikmiş/okuma hatasında randevu/Excel davranışına gate eklenmez.
