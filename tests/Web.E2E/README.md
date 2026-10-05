# URL gezinme kabulü

Bu test yalnız `http://127.0.0.1:8092` üzerinde ayrı, geçici kurulum içindir. Ana uygulamadaki hesap veya veriler kullanılmaz. Test; mevcut şeması uygulanmış boş PostgreSQL, Local e-posta modu, henüz MFA kurulmamış `owner@example.test` / `Synthetic!Owner123` sentetik Owner hesabı ve Chrome gerektirir. Aynı ortama ikinci kez çalıştırmadan önce yalnız test kurulumu yeniden hazırlanır. Üretim parolaları bu dosyaya veya test ortamına girilmez.

Depo kökünden Windows PowerShell:

```powershell
cd tests/Web.E2E
npm.cmd ci --ignore-scripts
npm.cmd test
```

Playwright 1.63.0 geliştirme bağımlılığıdır (Apache-2.0); üretim imajına girmez. Test gerçek CSRF/oturum/API kullanır, sahte yanıt üretmez. Bekleyen kayıt kontrolünde istek geciktirilir, sonra gerçek sunucuya iletilir. Sayfalama için 21 sentetik personel hazırlanırken mevcut hız sınırının pencereleri beklenir. API doğrulama/oturum/yetki/çatışma/hız sınırı hataları da kontrol edilir.

Doğrudan adres, yenileme, geri/ileri, liste sayfasına dönüş, yeni sekme, MFA sonrası dönüş, kirli form ve bekleyen işlem koruması doğrulanır. 320/390/768/1280 px görüntüleri, yatay taşma ve görünür işlem hedefleri/kontrast ölçümleri `.local/url-navigation/browser/` içine yazılır. Kabul raporu gerçek API başarısıyla üretilir; ekran görüntülerinin görsel incelemesi ayrıca yapılır. Bunlar sürümlenmez. Test açıkça izin verilen geçici CI dışında 8080'i kabul etmez.
