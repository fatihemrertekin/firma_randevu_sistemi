# Personel, hizmet ve çalışan erişimi işlemleri

05.10.2026: Kullanıcı pasif çalışan giriş hesabının yeniden etkinleştirilmesini ve personel/hizmetlerde Sil işlemini istedi. Silme kapsamını “Personel ve hizmetlerde Sil yeterli” yanıtıyla sınırladı. Mevcut onaylı yerleşim, pastel tokenlar ve 16 px dikey işlem boşluğu korunur; yeni ekran/taslak turu yoktur.

- Çalışan erişimleri: pasif giriş hesabında Etkinleştir; mevcut satırın altında açık onay ve Vazgeç. POST `/api/staff-accounts/{id}/activate`, hesap sürümü, CSRF ve MFA Owner. Başarı yalnız 204 sonrası; eski oturumlar geri açılmaz, mevcut parola korunur. Personel kaydıyla bağımsızlığı açıklanır.
- Personel: Ayrıntılar → Bilgiler → Personel durumu içinde ayrı kırmızı Sil. Adı, listeden kaldırmayı, geçmiş bağlantıların korunmasını ve giriş hesabının etkilenmediğini açıklayan onay; POST `/api/staff-members/{id}/delete`, ortak sürüm. 204 sonrası listeye dönülür.
- Hizmet: satırda Düzenle / Pasifleştir veya Aktifleştir / Sil. Silme onayı aynı mevcut alanda; POST `/api/services/{id}/delete`, sürüm. 204 sonrası güncel ilk sayfa; personelin seçimlerinden de çıkarılır.
- Onay öncesi istek yok; Vazgeç odağı açan düğmeye döndürür. Yükleme ve gönderimde geçiş/çift gönderim kilitli; 400/401/403/404/409/429 ve belirsiz sonuç mevcut hata diliyle gösterilir. 409 sonrasında yeniden yükleme gerekir. Personelde kaydedilmemiş taslak koruması sürer.
- Mobil işlemler mevcut flex düzeniyle sarılır, hedefler en az 44 px; renk ve boşluk yalnız tokenlardan. Başarı, iptal, hata, boş liste ve onay 320/390/768/1280 genişliklerinde gerçek sentetik API ile doğrulanır.

Durum: `done`. Web 209 test/kalite kapıları, sunucu 140 test kapsamı ve son commit CI, gerçek sentetik API/Chrome dokuz durum × dört genişlik, yedekli ana 8080 teslimi/sağlık/restart geçti. Kanıt [P02 planında](../../docs/plans/P02.md#tanım-silme-ve-çalışan-hesabını-etkinleştirme--05102026). Randevu/P03, hesap silme ve yeni tasarım ekranı kapsam dışıdır.
